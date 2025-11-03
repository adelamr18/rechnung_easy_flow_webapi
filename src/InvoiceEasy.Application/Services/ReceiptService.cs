using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Azure;
using Azure.AI.DocumentIntelligence;
using InvoiceEasy.Domain.Interfaces;
using InvoiceEasy.Domain.Interfaces.Repositories;
using InvoiceEasy.Domain.Interfaces.Services;
using InvoiceEasy.Domain.Models;
using Microsoft.Extensions.Logging;

namespace InvoiceEasy.Application.Services;

public class ReceiptService : IReceiptService
{
    private readonly IFileStorage _fileStorage;
    private readonly IReceiptRepository _receiptRepository;
    private readonly ILogger<ReceiptService> _logger;
    private readonly DocumentIntelligenceClient? _documentClient;

    private const string ReceiptModelId = "prebuilt-receipt";

    public ReceiptService(
        IFileStorage fileStorage,
        IReceiptRepository receiptRepository,
        ILogger<ReceiptService> logger,
        DocumentIntelligenceClient? documentClient = null)
    {
        _fileStorage = fileStorage;
        _receiptRepository = receiptRepository;
        _logger = logger;
        _documentClient = documentClient;
    }

    public async Task<Receipt> ProcessReceiptUploadAsync(Stream fileStream, string fileName)
    {
        if (fileStream == null || fileStream.Length == 0)
        {
            throw new ArgumentException("No file uploaded");
        }

        var fileExtension = Path.GetExtension(fileName).ToLowerInvariant();
        if (!IsSupportedFileType(fileExtension))
        {
            throw new NotSupportedException($"File type {fileExtension} is not supported");
        }

        await using var workingStream = new MemoryStream();
        await fileStream.CopyToAsync(workingStream);
        workingStream.Position = 0;

        try
        {
            var filePath = await _fileStorage.SaveFileAsync(
                workingStream,
                fileName,
                "receipts");

            var receipt = new Receipt
            {
                FileName = fileName,
                FilePath = filePath,
                UploadDate = DateTime.UtcNow,
                ExtractedData = new Dictionary<string, string>()
            };

            workingStream.Position = 0;

            if (_documentClient != null)
            {
                await AnalyzeReceiptAsync(receipt, workingStream);
            }

            return await _receiptRepository.AddAsync(receipt);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing receipt upload");
            throw new Exception("Error processing receipt", ex);
        }
    }

    public async Task<Receipt> GetReceiptAsync(Guid id)
    {
        var receipt = await _receiptRepository.GetByIdAsync(id);
        if (receipt == null)
        {
            throw new FileNotFoundException($"Receipt with ID {id} not found");
        }

        return receipt;
    }

    public Task<IEnumerable<Receipt>> GetAllReceiptsAsync()
    {
        return _receiptRepository.GetAllAsync();
    }

    public async Task DeleteReceiptAsync(Guid id)
    {
        var receipt = await _receiptRepository.GetByIdAsync(id);
        if (receipt != null)
        {
            await _fileStorage.DeleteFileAsync(receipt.FilePath);
            await _receiptRepository.DeleteAsync(id);
        }
    }

    private bool IsSupportedFileType(string fileExtension) =>
        fileExtension switch
        {
            ".pdf" => true,
            ".jpg" or ".jpeg" or ".png" => true,
            _ => false
        };

    private async Task AnalyzeReceiptAsync(Receipt receipt, Stream contentStream)
    {
        try
        {
            var operation = await _documentClient!.AnalyzeDocumentAsync(
                WaitUntil.Completed,
                ReceiptModelId,
                BinaryData.FromStream(contentStream));

            var result = operation.Value;
            var analyzedDocument = result.Documents.FirstOrDefault();
            if (analyzedDocument == null)
            {
                _logger.LogWarning("Document Intelligence returned no documents for receipt {ReceiptFile}", receipt.FileName);
                return;
            }

            var extractedData = receipt.ExtractedData ?? new Dictionary<string, string>();

            MapMerchant(analyzedDocument, receipt, extractedData);
            if (!string.IsNullOrWhiteSpace(result.Content))
            {
                extractedData["content"] = result.Content;
            }

            MapTotals(analyzedDocument, result, receipt, extractedData);
            MapTransactionDate(analyzedDocument, receipt, extractedData);
            CaptureRawFields(analyzedDocument, extractedData);
            CaptureParagraphs(result, extractedData);

            receipt.ExtractedData = extractedData;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to analyze receipt {ReceiptFile} with Document Intelligence", receipt.FileName);
        }
        finally
        {
            if (contentStream.CanSeek)
            {
                contentStream.Position = 0;
            }
        }
    }

    private static void MapMerchant(
        AnalyzedDocument analyzedDocument,
        Receipt receipt,
        Dictionary<string, string> extractedData)
    {
        if (!analyzedDocument.Fields.TryGetValue("MerchantName", out var merchantField))
        {
            return;
        }

        string? merchant = merchantField.FieldType == DocumentFieldType.String
            ? merchantField.ValueString
            : merchantField.Content;

        if (!string.IsNullOrWhiteSpace(merchant))
        {
            receipt.MerchantName = merchant;
            extractedData["merchantName"] = merchant;
        }
    }

    private static void MapTotals(
        AnalyzedDocument analyzedDocument,
        AnalyzeResult analyzeResult,
        Receipt receipt,
        Dictionary<string, string> extractedData)
    {
        decimal? totalAmount = null;

        if (analyzedDocument.Fields.TryGetValue("Total", out var totalField))
        {
            if (totalField.FieldType == DocumentFieldType.Currency)
            {
                var currency = totalField.ValueCurrency;
                totalAmount = ConvertToDecimal(currency.Amount);

                if (!string.IsNullOrWhiteSpace(currency.CurrencySymbol))
                {
                    extractedData["currencySymbol"] = currency.CurrencySymbol;
                }

                if (!string.IsNullOrWhiteSpace(currency.CurrencyCode))
                {
                    extractedData["currencyCode"] = currency.CurrencyCode;
                }
            }
            else if (totalField.FieldType == DocumentFieldType.Double && totalField.ValueDouble.HasValue)
            {
                totalAmount = ConvertToDecimal(totalField.ValueDouble.Value);
            }
            else if (totalField.FieldType == DocumentFieldType.Int64 && totalField.ValueInt64.HasValue)
            {
                totalAmount = totalField.ValueInt64.Value;
            }
            else if (totalField.FieldType == DocumentFieldType.String)
            {
                totalAmount = TryParseDecimal(totalField.ValueString);
            }

            if (!string.IsNullOrWhiteSpace(totalField.Content))
            {
                extractedData["totalRaw"] = totalField.Content;
            }
        }

        var itemsTotal = TryExtractItemsTotal(analyzedDocument, extractedData);
        if (itemsTotal.HasValue)
        {
            if (!totalAmount.HasValue || totalAmount.Value <= 0)
            {
                totalAmount = itemsTotal;
            }
        }

        if ((!totalAmount.HasValue || totalAmount.Value <= 0) && analyzeResult != null)
        {
            var fallback = TryExtractTotalFromContent(analyzeResult.Content, extractedData);
            if (fallback.HasValue)
            {
                totalAmount = fallback;
            }
        }

        if (totalAmount.HasValue && totalAmount.Value > 0)
        {
            var rounded = Math.Round(totalAmount.Value, 2, MidpointRounding.AwayFromZero);
            receipt.TotalAmount = rounded;
            extractedData["total"] = rounded.ToString("0.00");
        }
    }

    private static void MapTransactionDate(
        AnalyzedDocument analyzedDocument,
        Receipt receipt,
        Dictionary<string, string> extractedData)
    {
        if (!analyzedDocument.Fields.TryGetValue("TransactionDate", out var dateField))
        {
            return;
        }

        if (dateField.FieldType == DocumentFieldType.Date && dateField.ValueDate.HasValue)
        {
            var date = dateField.ValueDate.Value.UtcDateTime;
            receipt.TransactionDate = date;
            extractedData["transactionDate"] = date.ToString("O");
        }
        else if (dateField.FieldType == DocumentFieldType.String)
        {
            var rawDate = dateField.ValueString;
            if (!string.IsNullOrWhiteSpace(rawDate) && DateTime.TryParse(rawDate, out var parsed))
            {
                var utc = DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
                receipt.TransactionDate = utc;
                extractedData["transactionDate"] = utc.ToString("O");
            }
        }

        if (!string.IsNullOrWhiteSpace(dateField.Content) && !extractedData.ContainsKey("transactionDateRaw"))
        {
            extractedData["transactionDateRaw"] = dateField.Content;
        }
    }

    private static void CaptureRawFields(
        AnalyzedDocument analyzedDocument,
        Dictionary<string, string> extractedData)
    {
        foreach (var field in analyzedDocument.Fields)
        {
            if (string.IsNullOrWhiteSpace(field.Value?.Content))
            {
                continue;
            }

            extractedData.TryAdd(field.Key, field.Value.Content);
        }
    }

    private static void CaptureParagraphs(AnalyzeResult result, Dictionary<string, string> extractedData)
    {
        if (result.Paragraphs == null || result.Paragraphs.Count == 0)
        {
            return;
        }

        var combined = string.Join(
            Environment.NewLine,
            result.Paragraphs.Select(p => p.Content));

        if (!string.IsNullOrWhiteSpace(combined))
        {
            extractedData["paragraphs"] = combined;
        }
    }

    private static decimal ConvertToDecimal(double value) => Convert.ToDecimal(value);

    private static decimal? TryExtractTotalFromContent(string? content, Dictionary<string, string> extractedData)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        var lines = content.Split('\n');
        var totalLineRegex = new Regex(@"(total|summe|gesamt|amount due|due)\s*[:\-]?\s*([0-9]+[.,][0-9]{2})", RegexOptions.IgnoreCase);

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrEmpty(line))
            {
                continue;
            }

            var match = totalLineRegex.Match(line);
            if (match.Success && match.Groups.Count >= 3)
            {
                var parsed = TryParseDecimal(match.Groups[2].Value);
                if (parsed.HasValue)
                {
                    extractedData["totalLine"] = line;
                    return parsed;
                }
            }
        }

        var amountRegex = new Regex(@"(?:€|eur|\$)?\s*([0-9]+[.,][0-9]{2})", RegexOptions.IgnoreCase);
        decimal? max = null;
        foreach (Match match in amountRegex.Matches(content))
        {
            if (match.Groups.Count < 2)
            {
                continue;
            }

            var parsed = TryParseDecimal(match.Groups[1].Value);
            if (parsed.HasValue)
            {
                if (!max.HasValue || parsed.Value > max.Value)
                {
                    max = parsed.Value;
                }
            }
        }

        if (max.HasValue)
        {
            extractedData["totalDetectedFromText"] = max.Value.ToString("0.00");
            return max;
        }

        return null;
    }

    private static decimal? TryExtractItemsTotal(
        AnalyzedDocument analyzedDocument,
        Dictionary<string, string> extractedData)
    {
        if (!analyzedDocument.Fields.TryGetValue("Items", out var itemsField) ||
            itemsField.FieldType != DocumentFieldType.List ||
            itemsField.ValueList == null ||
            itemsField.ValueList.Count == 0)
        {
            return null;
        }

        var itemSummaries = new List<object>();
        decimal runningTotal = 0;

        foreach (var itemField in itemsField.ValueList)
        {
            if (itemField.FieldType != DocumentFieldType.Dictionary ||
                itemField.ValueDictionary == null)
            {
                continue;
            }

            var itemDictionary = itemField.ValueDictionary;
            var description = itemDictionary.TryGetValue("Description", out var descriptionField)
                ? GetFieldContent(descriptionField)
                : null;

            var quantity = itemDictionary.TryGetValue("Quantity", out var quantityField)
                ? ConvertQuantityField(quantityField)
                : (decimal?)null;

            decimal? lineTotal = null;

            if (itemDictionary.TryGetValue("TotalPrice", out var totalPriceField))
            {
                lineTotal = ConvertCurrencyField(totalPriceField);
            }

            if (!lineTotal.HasValue && itemDictionary.TryGetValue("Price", out var priceField))
            {
                var unitPrice = ConvertCurrencyField(priceField);
                if (unitPrice.HasValue)
                {
                    lineTotal = quantity.HasValue
                        ? unitPrice.Value * quantity.Value
                        : unitPrice.Value;
                }
            }

            decimal? unitPriceValue = null;
            if (itemDictionary.TryGetValue("Price", out var unitPriceField))
            {
                unitPriceValue = ConvertCurrencyField(unitPriceField);
            }

            if (lineTotal.HasValue && lineTotal.Value > 0)
            {
                runningTotal += lineTotal.Value;
            }

            itemSummaries.Add(new
            {
                description,
                quantity,
                unitPrice = unitPriceValue,
                total = lineTotal
            });
        }

        if (itemSummaries.Count > 0)
        {
            extractedData["items"] = JsonSerializer.Serialize(itemSummaries);
        }

        if (runningTotal > 0)
        {
            extractedData["itemsTotal"] = runningTotal.ToString("0.00");
            return runningTotal;
        }

        return null;
    }

    private static decimal? ConvertCurrencyField(DocumentField field)
    {
        if (field.FieldType == DocumentFieldType.Currency)
        {
            return ConvertToDecimal(field.ValueCurrency.Amount);
        }

        if (field.FieldType == DocumentFieldType.Double && field.ValueDouble.HasValue)
        {
            return ConvertToDecimal(field.ValueDouble.Value);
        }

        if (field.FieldType == DocumentFieldType.Int64 && field.ValueInt64.HasValue)
        {
            return field.ValueInt64.Value;
        }

        if (field.FieldType == DocumentFieldType.String)
        {
            return TryParseDecimal(field.ValueString);
        }

        return null;
    }

    private static decimal? ConvertQuantityField(DocumentField field)
    {
        if (field.FieldType == DocumentFieldType.Double && field.ValueDouble.HasValue)
        {
            return ConvertToDecimal(field.ValueDouble.Value);
        }

        if (field.FieldType == DocumentFieldType.Int64 && field.ValueInt64.HasValue)
        {
            return field.ValueInt64.Value;
        }

        if (field.FieldType == DocumentFieldType.String)
        {
            return TryParseDecimal(field.ValueString);
        }

        return null;
    }

    private static string? GetFieldContent(DocumentField field)
    {
        if (field.FieldType == DocumentFieldType.String)
        {
            return field.ValueString;
        }

        return field.Content;
    }

    private static decimal? TryParseDecimal(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        if (decimal.TryParse(
            input,
            NumberStyles.Number | NumberStyles.AllowCurrencySymbol,
            CultureInfo.InvariantCulture,
            out var invariantValue))
        {
            return invariantValue;
        }

        if (decimal.TryParse(
                input,
                NumberStyles.Number | NumberStyles.AllowCurrencySymbol,
                CultureInfo.GetCultureInfo("de-DE"),
                out var germanValue))
        {
            return germanValue;
        }

        return decimal.TryParse(
            input,
            NumberStyles.Number | NumberStyles.AllowCurrencySymbol,
            CultureInfo.CurrentCulture,
            out var currentValue)
            ? currentValue
            : (decimal?)null;
    }
}
