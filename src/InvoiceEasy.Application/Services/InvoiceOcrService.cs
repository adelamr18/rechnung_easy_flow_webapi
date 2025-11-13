using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Azure;
using Azure.AI.DocumentIntelligence;
using InvoiceEasy.Domain.Interfaces.Services;
using InvoiceEasy.Domain.Models;

namespace InvoiceEasy.Application.Services;

public class InvoiceOcrService : IInvoiceOcrService
{
    private readonly DocumentIntelligenceClient? _documentClient;
    private const string ReceiptModelId = "prebuilt-receipt";

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
            ReceiptModelId,
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
            else if (doc.Fields.TryGetValue("MerchantName", out var merchantField) && merchantField.FieldType == DocumentFieldType.String)
            {
                analysis.VendorName = merchantField.ValueString;
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
            else if (doc.Fields.TryGetValue("TransactionDate", out var transactionField))
            {
                if (transactionField.FieldType == DocumentFieldType.Date && transactionField.ValueDate.HasValue)
                {
                    analysis.InvoiceDate = DateTime.SpecifyKind(transactionField.ValueDate.Value.UtcDateTime, DateTimeKind.Utc);
                }
            }

            if (doc.Fields.TryGetValue("InvoiceTotal", out var totalField))
            {
                analysis.TotalAmount = ExtractDecimalFromField(totalField, out var currency);
                analysis.CurrencyCode = currency ?? analysis.CurrencyCode;
            }
            else if (doc.Fields.TryGetValue("Total", out var receiptTotalField))
            {
                analysis.TotalAmount = ExtractDecimalFromField(receiptTotalField, out var currency);
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
                    else if (dict.TryGetValue("Weight", out var weightField))
                    {
                        item.Quantity = ExtractDecimalFromField(weightField, out _);
                    }

                    if (dict.TryGetValue("UnitPrice", out var unitPriceField))
                    {
                        item.UnitPrice = ExtractDecimalFromField(unitPriceField, out var currencySymbol);
                        if (!string.IsNullOrWhiteSpace(currencySymbol))
                        {
                            analysis.CurrencyCode ??= currencySymbol;
                        }
                    }
                    else if (dict.TryGetValue("Price", out var priceField))
                    {
                        item.UnitPrice = ExtractDecimalFromField(priceField, out var currencySymbol);
                        if (!string.IsNullOrWhiteSpace(currencySymbol))
                        {
                            analysis.CurrencyCode ??= currencySymbol;
                        }
                    }

                    if (dict.TryGetValue("TotalPrice", out var totalPriceField))
                    {
                        item.TotalPrice = ExtractDecimalFromField(totalPriceField, out var currencySymbol);
                        if (!string.IsNullOrWhiteSpace(currencySymbol))
                        {
                            analysis.CurrencyCode ??= currencySymbol;
                        }
                    }
                    else if (dict.TryGetValue("Amount", out var amountField))
                    {
                        item.TotalPrice = ExtractDecimalFromField(amountField, out var currencySymbol);
                        if (!string.IsNullOrWhiteSpace(currencySymbol))
                        {
                            analysis.CurrencyCode ??= currencySymbol;
                        }
                    }
                    else if (dict.TryGetValue("Subtotal", out var subtotalField))
                    {
                        item.TotalPrice = ExtractDecimalFromField(subtotalField, out var currencySymbol);
                        if (!string.IsNullOrWhiteSpace(currencySymbol))
                        {
                            analysis.CurrencyCode ??= currencySymbol;
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(item.Description) ||
                        item.Quantity.HasValue ||
                        item.UnitPrice.HasValue ||
                        item.TotalPrice.HasValue)
                    {
                        analysis.Items.Add(item);
                    }
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

        var rawContent = result.Content ?? string.Empty;
        analysis.Notes = rawContent;

        var hasPages = result.Pages != null && result.Pages.Count > 0;
        var hasStructuredItems = analysis.Items.Any();

        if (hasPages || !string.IsNullOrWhiteSpace(rawContent))
        {
            if (!hasStructuredItems)
            {
                MergeFallbackItems(analysis, result.Pages, rawContent);
            }

            if (string.IsNullOrWhiteSpace(analysis.CurrencyCode) &&
                TryInferCurrency(rawContent, out var inferredCurrency))
            {
                analysis.CurrencyCode = inferredCurrency;
            }
        }

        if (!analysis.TotalAmount.HasValue && !string.IsNullOrWhiteSpace(rawContent))
        {
            var fallback = ExtractLargestAmountFromText(rawContent, out var currency);
            if (fallback.HasValue)
            {
                analysis.TotalAmount = fallback;
                analysis.CurrencyCode ??= currency;
            }
        }

        if (!analysis.InvoiceDate.HasValue && !string.IsNullOrWhiteSpace(rawContent))
        {
            var date = ExtractFirstDateFromText(rawContent);
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

    private static bool TryInferCurrency(string content, out string currencyCode)
    {
        currencyCode = "EUR";
        if (string.IsNullOrWhiteSpace(content))
        {
            return false;
        }

        var symbols = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "€", "EUR" },
            { "eur", "EUR" },
            { "usd", "USD" },
            { "$", "USD" },
            { "cad", "CAD" },
            { "aud", "AUD" },
            { "gbp", "GBP" },
            { "£", "GBP" },
            { "chf", "CHF" },
            { "¥", "JPY" },
            { "jpy", "JPY" },
            { "cny", "CNY" },
            { "₽", "RUB" },
            { "rub", "RUB" },
            { "₹", "INR" },
            { "inr", "INR" },
            { "kr", "SEK" },
            { "sek", "SEK" },
            { "nok", "NOK" },
            { "dkk", "DKK" },
            { "zł", "PLN" },
            { "pln", "PLN" },
            { "₺", "TRY" },
            { "try", "TRY" }
        };

        foreach (var kvp in symbols)
        {
            if (content.IndexOf(kvp.Key, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                currencyCode = kvp.Value;
                return true;
            }
        }

        return false;
    }

    private static void MergeFallbackItems(
        InvoiceAnalysisResult analysis,
        IReadOnlyList<DocumentPage>? pages,
        string content)
    {
        var fallbackItems = ExtractLineItemsFromPages(pages).ToList();
        if (fallbackItems.Count == 0 && !string.IsNullOrWhiteSpace(content))
        {
            fallbackItems = ExtractLineItemsFromRawText(content).ToList();
        }

        if (fallbackItems.Count == 0)
        {
            return;
        }

        var existingDescriptions = new HashSet<string>(
            analysis.Items
                .Where(i => !string.IsNullOrWhiteSpace(i.Description))
                .Select(i => NormalizeDescription(i.Description!)));

        foreach (var item in fallbackItems)
        {
            if (string.IsNullOrWhiteSpace(item.Description))
            {
                continue;
            }

            var normalized = NormalizeDescription(item.Description);
            if (existingDescriptions.Contains(normalized))
            {
                continue;
            }

            analysis.Items.Add(item);
            existingDescriptions.Add(normalized);
        }
    }

    private sealed record LineInfo(
        int Index,
        string Text,
        float CenterX,
        bool HasLetters,
        bool LooksLikeLabel,
        bool LooksLikeQuantity,
        decimal? Price);

    private static IEnumerable<InvoiceAnalysisItem> ExtractLineItemsFromPages(IReadOnlyList<DocumentPage>? pages)
    {
        if (pages == null || pages.Count == 0)
        {
            return Enumerable.Empty<InvoiceAnalysisItem>();
        }

        var lines = pages
            .SelectMany(page => page.Lines)
            .Select((line, index) =>
            {
                var text = line.Content?.Trim();
                if (string.IsNullOrWhiteSpace(text))
                {
                    return null;
                }

                var centerX = ComputeCenterX(line.Polygon);
                var hasLetters = text.Any(char.IsLetter);
                var looksLikeLabel = IsLikelyLabel(text);
                var looksLikeQuantity = LooksLikeQuantity(text);
                decimal parsedPrice;
                var hasPrice = TryParseStandalonePrice(text, out parsedPrice);
                var price = hasPrice ? parsedPrice : (decimal?)null;

                return new LineInfo(index, text, centerX, hasLetters, looksLikeLabel, looksLikeQuantity, price);
            })
            .Where(line => line != null)
            .Select(line => line!)
            .ToList();

        if (lines.Count == 0)
        {
            return Enumerable.Empty<InvoiceAnalysisItem>();
        }

        var usedDescriptionIndices = new HashSet<int>();
        var results = new List<InvoiceAnalysisItem>();

        foreach (var priceLine in lines.Where(l => l.Price.HasValue))
        {
            var descriptionLine = FindDescriptionLine(lines, priceLine, usedDescriptionIndices);
            if (descriptionLine == null)
            {
                continue;
            }

            usedDescriptionIndices.Add(descriptionLine.Index);

            var betweenLines = lines
                .Where(l => l.Index > descriptionLine.Index && l.Index < priceLine.Index && l.LooksLikeQuantity)
                .Select(l => l.Text)
                .ToList();

            var description = descriptionLine.Text;
            if (betweenLines.Count > 0)
            {
                description = $"{description}\n{string.Join("\n", betweenLines)}";
            }

            results.Add(new InvoiceAnalysisItem
            {
                Description = description.Trim(),
                TotalPrice = priceLine.Price
            });
        }

        return results;
    }

    private static LineInfo? FindDescriptionLine(
        IReadOnlyList<LineInfo> lines,
        LineInfo priceLine,
        HashSet<int> usedDescriptionIndices)
    {
        var pointer = priceLine.Index - 1;
        while (pointer >= 0)
        {
            var candidate = lines[pointer];

            if (candidate.Price.HasValue)
            {
                pointer--;
                continue;
            }

            if (!candidate.HasLetters || candidate.LooksLikeLabel || candidate.LooksLikeQuantity ||
                usedDescriptionIndices.Contains(candidate.Index))
            {
                pointer--;
                continue;
            }

            return candidate;
        }

        return null;
    }

    private static float ComputeCenterX(IReadOnlyList<float>? polygon)
    {
        if (polygon == null || polygon.Count < 2)
        {
            return 0;
        }

        var sum = 0f;
        var count = 0;
        for (var i = 0; i < polygon.Count; i += 2)
        {
            sum += polygon[i];
            count++;
        }

        return count == 0 ? 0 : sum / count;
    }

    private static bool IsLikelyLabel(string text)
    {
        var cleaned = Regex.Replace(text, @"\s+", string.Empty);
        if (cleaned.Length <= 4 && cleaned.All(char.IsLetter))
        {
            return true;
        }

        return cleaned.Contains(':') || cleaned.Contains('=');
    }

    private static IEnumerable<InvoiceAnalysisItem> ExtractLineItemsFromRawText(string content)
    {
        var lines = content.Split('\n')
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select((text, index) => (Text: text, Index: index))
            .ToList();

        var usedDescriptionLines = new HashSet<int>();

        for (var i = 0; i < lines.Count; i++)
        {
            var (line, _) = lines[i];

            if (!TryParseStandalonePrice(line, out var price))
            {
                continue;
            }

            var descriptionParts = new List<string>();
            var quantityParts = new List<string>();

            var pointer = i - 1;
            while (pointer >= 0)
            {
                var (candidate, candidateIndex) = lines[pointer];
                if (TryParseStandalonePrice(candidate, out _))
                {
                    pointer--;
                    continue;
                }

                var lowerCandidate = candidate.ToLowerInvariant();
                if (IsSummaryLine(lowerCandidate))
                {
                    descriptionParts.Clear();
                    quantityParts.Clear();
                    break;
                }

                if (usedDescriptionLines.Contains(candidateIndex))
                {
                    pointer--;
                    continue;
                }

                if (IsSkippableLine(lowerCandidate))
                {
                    pointer--;
                    continue;
                }

            if (LooksLikeQuantity(candidate))
            {
                quantityParts.Insert(0, candidate);
                pointer--;
                continue;
            }

                descriptionParts.Insert(0, candidate);
                usedDescriptionLines.Add(candidateIndex);
                pointer--;
                break;
            }

            if (descriptionParts.Count == 0)
            {
                continue;
            }

            var fullDescription = string.Join("\n", descriptionParts.Concat(quantityParts));
            if (string.IsNullOrWhiteSpace(fullDescription))
            {
                continue;
            }

            yield return new InvoiceAnalysisItem
            {
                Description = fullDescription.Trim(),
                TotalPrice = price
            };
        }
    }

    private static bool TryParseStandalonePrice(string line, out decimal price)
    {
        price = 0;

        var sanitized = line
            .Replace("EUR", "", StringComparison.OrdinalIgnoreCase)
            .Replace(" ", string.Empty)
            .Trim('*');

        sanitized = Regex.Replace(sanitized, @"[A-Z]$", string.Empty, RegexOptions.IgnoreCase).Trim();

        if (!Regex.IsMatch(sanitized, @"^-?\d+[.,]\d{2}$"))
        {
            return false;
        }

        return decimal.TryParse(sanitized.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out price);
    }

    private static readonly string[] MeasurementTokens =
    {
        "g", "kg", "mg", "l", "ml", "lt", "cl", "oz", "lb",
        "pcs", "pc", "stk", "stück", "ud", "uds", "pz", "pz.", "шт", "szt", "pzla"
    };

    private static readonly string[] SummaryKeywords =
    {
        "summe", "gesamt", "total", "totale", "totales", "totaux", "subtotal", "importe",
        "sumatoria", "suma", "somme", "合計", "合計金額", "totaal", "toplam",
        "datum", "fecha", "date", "data", "datahora", "hora", "uhrzeit", "tijd", "heure",
        "receipt", "ticket", "beleg", "factura", "nota", "bon", "kvitto", "recibo",
        "betaling", "zahlung", "pago", "pagamento", "paiement", "оплата",
        "tax", "steuer", "iva", "tva", "impuesto", "alv", "pps",
        "terminal", "pos", "trace", "transak", "transacción", "transaktion",
        "card", "debit", "credit", "mastercard", "visa", "amex",
        "signature", "firma", "signatur", "sign", "uid", "nif", "cif", "rfc", "gst",
        "cashier", "kasse", "caja", "caisse", "market", "store", "shop", "branch"
    };

    private static bool IsSkippableLine(string lower)
    {
        if (string.IsNullOrWhiteSpace(lower))
        {
            return true;
        }

        var trimmed = lower.Trim('*', ':');
        if (trimmed.Length <= 2 && !trimmed.Any(char.IsLetter))
        {
            return true;
        }

        if (trimmed is "eur" or "usd" or "gbp" or "cad" or "chf" or "jpy" or "¥" or "€" or "$")
        {
            return true;
        }

        if (MeasurementTokens.Any(token => trimmed == token))
        {
            return true;
        }

        return false;
    }

    private static bool IsSummaryLine(string lower)
    {
        if (string.IsNullOrWhiteSpace(lower))
        {
            return true;
        }

        if (Regex.IsMatch(lower, @"\d{1,2}[:\.]\d{2}(:\d{2})?") ||
            Regex.IsMatch(lower, @"\d{1,2}[./-]\d{1,2}[./-]\d{2,4}"))
        {
            return true;
        }

        return SummaryKeywords.Any(keyword => lower.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }

    private static bool LooksLikeQuantity(string text)
    {
        var lower = text.ToLowerInvariant();
        if (lower.Contains("stk") || lower.Contains("stück") || lower.Contains("x ") || lower.EndsWith("x"))
        {
            return true;
        }

        if (Regex.IsMatch(lower, @"\d+\s*(x|×)\s*\d+"))
        {
            return true;
        }

        return Regex.IsMatch(lower, @"\d+\s?(kg|g|l|ml|pcs|pc|шт|st|pkt)");
    }

    private static string NormalizeDescription(string description)
    {
        var cleaned = Regex.Replace(description, @"\s+", " ").Trim();
        return cleaned.ToLowerInvariant();
    }
}
