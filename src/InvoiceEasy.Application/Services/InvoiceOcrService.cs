using System.Globalization;
using System.Text.RegularExpressions;
using Azure;
using Azure.AI.DocumentIntelligence;
using InvoiceEasy.Domain.Interfaces.Services;
using InvoiceEasy.Domain.Models;

namespace InvoiceEasy.Application.Services;

public class InvoiceOcrService : IInvoiceOcrService
{
    private readonly DocumentIntelligenceClient? _documentClient;
    private const string InvoiceModelId = "prebuilt-invoice";

    public InvoiceOcrService(DocumentIntelligenceClient? documentClient = null)
    {
        _documentClient = documentClient;
    }

    public async Task<InvoiceAnalysisResult> AnalyzeAsync(Stream fileStream, string fileName)
    {
        if (_documentClient == null)
        {
            throw new InvalidOperationException("Document Intelligence client is not configured.");
        }

        if (fileStream == null || fileStream.Length == 0)
        {
            throw new ArgumentException("File stream is empty.", nameof(fileStream));
        }

        fileStream.Position = 0;
        var operation = await _documentClient.AnalyzeDocumentAsync(
            WaitUntil.Completed,
            InvoiceModelId,
            BinaryData.FromStream(fileStream));

        var result = operation.Value;
        var analysis = new InvoiceAnalysisResult();

        var doc = result.Documents.FirstOrDefault();
        if (doc != null)
        {
            if (doc.Fields.TryGetValue("VendorName", out var vendorField) && vendorField.FieldType == DocumentFieldType.String)
            {
                analysis.VendorName = vendorField.ValueString;
            }

            if (doc.Fields.TryGetValue("CustomerName", out var customerField) && customerField.FieldType == DocumentFieldType.String)
            {
                analysis.CustomerName = customerField.ValueString;
            }

            if (doc.Fields.TryGetValue("InvoiceId", out var invoiceIdField) && invoiceIdField.FieldType == DocumentFieldType.String)
            {
                analysis.InvoiceNumber = invoiceIdField.ValueString;
            }

            if (doc.Fields.TryGetValue("InvoiceDate", out var dateField))
            {
                if (dateField.FieldType == DocumentFieldType.Date && dateField.ValueDate.HasValue)
                {
                    analysis.InvoiceDate = DateTime.SpecifyKind(dateField.ValueDate.Value.UtcDateTime, DateTimeKind.Utc);
                }
                else if (dateField.FieldType == DocumentFieldType.String &&
                         DateTime.TryParse(dateField.ValueString, out var parsedDate))
                {
                    analysis.InvoiceDate = DateTime.SpecifyKind(parsedDate, DateTimeKind.Utc);
                }
            }

            if (doc.Fields.TryGetValue("InvoiceTotal", out var totalField))
            {
                analysis.TotalAmount = ExtractDecimalFromField(totalField, out var currency);
                analysis.CurrencyCode = currency ?? analysis.CurrencyCode;
            }

            if (doc.Fields.TryGetValue("Items", out var itemsField) &&
                itemsField.FieldType == DocumentFieldType.List &&
                itemsField.ValueList != null)
            {
                foreach (var itemField in itemsField.ValueList)
                {
                    if (itemField.FieldType != DocumentFieldType.Dictionary || itemField.ValueDictionary == null)
                    {
                        continue;
                    }

                    var item = new InvoiceAnalysisItem();
                    var dict = itemField.ValueDictionary;

                    if (dict.TryGetValue("Description", out var descriptionField) && descriptionField.FieldType == DocumentFieldType.String)
                    {
                        item.Description = descriptionField.ValueString;
                    }

                    if (dict.TryGetValue("Quantity", out var quantityField))
                    {
                        item.Quantity = ExtractDecimalFromField(quantityField, out _);
                    }

                    if (dict.TryGetValue("UnitPrice", out var unitPriceField))
                    {
                        item.UnitPrice = ExtractDecimalFromField(unitPriceField, out var currencySymbol);
                        if (!string.IsNullOrWhiteSpace(currencySymbol))
                        {
                            analysis.CurrencyCode ??= currencySymbol;
                        }
                    }

                    if (dict.TryGetValue("Amount", out var amountField))
                    {
                        item.TotalPrice = ExtractDecimalFromField(amountField, out var currencySymbol);
                        if (!string.IsNullOrWhiteSpace(currencySymbol))
                        {
                            analysis.CurrencyCode ??= currencySymbol;
                        }
                    }

                    analysis.Items.Add(item);
                }
            }

            foreach (var entry in doc.Fields)
            {
                var content = entry.Value?.Content;
                if (!string.IsNullOrWhiteSpace(content))
                {
                    analysis.RawFields[entry.Key] = content;
                }
            }
        }

        analysis.Notes = result.Content;

        if (!analysis.TotalAmount.HasValue && !string.IsNullOrWhiteSpace(result.Content))
        {
            var fallback = ExtractLargestAmountFromText(result.Content, out var currency);
            if (fallback.HasValue)
            {
                analysis.TotalAmount = fallback;
                analysis.CurrencyCode ??= currency;
            }
        }

        if (!analysis.InvoiceDate.HasValue && !string.IsNullOrWhiteSpace(result.Content))
        {
            var date = ExtractFirstDateFromText(result.Content);
            if (date.HasValue)
            {
                analysis.InvoiceDate = date;
            }
        }

        return analysis;
    }

    private static decimal? ExtractDecimalFromField(DocumentField field, out string? currencyCode)
    {
        currencyCode = null;
        if (field.FieldType == DocumentFieldType.Currency)
        {
            currencyCode = field.ValueCurrency?.CurrencyCode ?? field.ValueCurrency?.CurrencySymbol;
            return ConvertCurrency(field.ValueCurrency);
        }

        if (field.FieldType == DocumentFieldType.Double && field.ValueDouble.HasValue)
        {
            return Convert.ToDecimal(field.ValueDouble.Value);
        }

        if (field.FieldType == DocumentFieldType.Int64 && field.ValueInt64.HasValue)
        {
            return field.ValueInt64.Value;
        }

        if (field.FieldType == DocumentFieldType.String)
        {
            var parsed = TryParseCurrency(field.ValueString);
            if (parsed.HasValue)
            {
                return parsed.Value.Amount;
            }
        }

        return null;
    }

    private static decimal? ConvertCurrency(CurrencyValue? currencyValue)
    {
        if (currencyValue == null)
        {
            return null;
        }

        return Convert.ToDecimal(currencyValue.Amount);
    }

    private static decimal? ExtractLargestAmountFromText(string content, out string? currencyCode)
    {
        currencyCode = null;
        decimal? max = null;
        var regex = new Regex(@"(?:(€|eur|\$|usd|gbp)\s*)?([0-9]+[.,][0-9]{2})", RegexOptions.IgnoreCase);

        foreach (Match match in regex.Matches(content))
        {
            if (match.Groups.Count < 3) continue;

            var valueGroup = match.Groups[2].Value;
            if (decimal.TryParse(valueGroup.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var amount))
            {
                if (!max.HasValue || amount > max.Value)
                {
                    max = amount;
                    currencyCode = match.Groups[1].Success ? match.Groups[1].Value.ToUpperInvariant() : currencyCode;
                }
            }
        }

        return max;
    }

    private static DateTime? ExtractFirstDateFromText(string content)
    {
        var regex = new Regex(@"\b(\d{1,2}[./-]\d{1,2}[./-]\d{2,4})\b");
        var match = regex.Match(content);
        if (match.Success && DateTime.TryParse(match.Groups[1].Value, out var parsed))
        {
            return DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
        }

        return null;
    }

    private static (decimal Amount, string? CurrencyCode)? TryParseCurrency(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        var regex = new Regex(@"(?:(€|eur|\$|usd|gbp)\s*)?([0-9]+[.,][0-9]{2})", RegexOptions.IgnoreCase);
        var match = regex.Match(input);
        if (!match.Success || match.Groups.Count < 3)
        {
            return null;
        }

        if (decimal.TryParse(match.Groups[2].Value.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var amount))
        {
            var currency = match.Groups[1].Success ? match.Groups[1].Value.ToUpperInvariant() : null;
            return (amount, currency);
        }

        return null;
    }
}
