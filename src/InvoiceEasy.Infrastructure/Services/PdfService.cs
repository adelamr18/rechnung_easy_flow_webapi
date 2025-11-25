using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using InvoiceEasy.Domain.Entities;
using InvoiceEasy.Domain.Enums;
using InvoiceEasy.Domain.Interfaces;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace InvoiceEasy.Infrastructure.Services;

public class PdfService
{
    private readonly IFileStorage _fileStorage;
    private record PdfLineItem(string Description, decimal? Quantity, decimal? UnitPrice, decimal? Total);
    private class StoredLineItem
    {
        public string? Description { get; set; }
        public decimal? Quantity { get; set; }
        public decimal? UnitPrice { get; set; }
        public decimal? TotalPrice { get; set; }
    }

    public PdfService(IFileStorage fileStorage)
    {
        _fileStorage = fileStorage;
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public async Task<string> GenerateInvoicePdfAsync(Invoice invoice, User user, InvoicePdfTemplate template)
    {
        var fileName = $"invoice_{invoice.Id}.pdf";
        var pdfStream = new MemoryStream();
        var lineItems = ResolveLineItems(invoice);
        var paymentDate = DateTime.UtcNow;
        var t = PdfTexts.For(user.Locale);

        var document = template switch
        {
            InvoicePdfTemplate.Advanced => CreateAdvancedTemplate(invoice, user, lineItems, GenerateNotesTitle(invoice, t), paymentDate, t),
            InvoicePdfTemplate.Elite => CreateEliteTemplate(invoice, user, lineItems, GenerateNotesTitle(invoice, t), paymentDate, t),
            _ => CreateBasicTemplate(invoice, user, lineItems, GenerateNotesTitle(invoice, t), paymentDate, t)
        };

        document.GeneratePdf(pdfStream);
        pdfStream.Position = 0;

        var savedPath = await _fileStorage.SaveFileAsync(pdfStream, fileName, "invoices");
        return savedPath;
    }

    private static string GenerateNotesTitle(Invoice invoice, PdfTexts t)
    {
        var datePart = invoice.InvoiceDate != default
            ? invoice.InvoiceDate.ToString("dd MMM yyyy")
            : DateTime.UtcNow.ToString("dd MMM yyyy");

        string anchor;
        if (!string.IsNullOrWhiteSpace(invoice.CustomerName))
        {
            anchor = invoice.CustomerName.Trim();
        }
        else if (!string.IsNullOrWhiteSpace(invoice.ServiceDescription))
        {
            anchor = $"Ref {invoice.Id.ToString("N")[..6].ToUpperInvariant()}";
        }
        else
        {
            anchor = invoice.Id.ToString("N")[..6].ToUpperInvariant();
        }

        return $"{t.NotesLabel} • {datePart} • {anchor}";
    }

    private static IDocument CreateBasicTemplate(
        Invoice invoice,
        User user,
        IReadOnlyList<PdfLineItem> lineItems,
        string notesTitle,
        DateTime paymentDate,
        PdfTexts t)
    {
        var billedTo = string.IsNullOrWhiteSpace(invoice.CustomerName)
            ? null
            : invoice.CustomerName.Trim();
        var businessName = user.CompanyName ?? user.Email;

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(2, Unit.Centimetre);
                page.PageColor(Colors.White);
                page.DefaultTextStyle(x => x.FontSize(12));

                page.Header()
                    .Column(column =>
                    {
                        column.Spacing((float)0.5, Unit.Centimetre);
                        column.Item().Text(t.InvoiceTitle)
                            .FontSize(22)
                            .FontColor("#2563EB")
                            .Bold();

                        column.Item().Row(row =>
                        {
                            row.RelativeItem().Column(sender =>
                            {
                                sender.Spacing(4);
                                sender.Item().Text(t.From).Bold();
                                sender.Item().Text(businessName);
                                sender.Item().Text(user.Email);
                            });

                            if (!string.IsNullOrWhiteSpace(billedTo))
                            {
                                row.RelativeItem().Column(recipient =>
                                {
                                    recipient.Spacing(4);
                                    recipient.Item().Text(t.To).Bold();
                                    recipient.Item().Text(billedTo);
                                });
                            }
                        });
                    });

                page.Content()
                    .PaddingTop((float)1.0, Unit.Centimetre)
                    .Column(column =>
                    {
                        column.Spacing(12);
                        column.Item().Column(details =>
                        {
                            details.Item().Text($"{t.InvoiceDate}: {invoice.InvoiceDate:dd.MM.yyyy}")
                                .FontSize(13)
                                .Bold();
                            details.Item().Text($"{t.PaymentStatus}: {t.PaymentAccepted}")
                                .FontSize(11)
                                .FontColor("#64748B");
                            details.Item().Text($"{t.PaymentDate}: {paymentDate:dd.MM.yyyy}")
                                .FontSize(11)
                                .FontColor("#64748B");
                        });

                        column.Item().Column(itemsColumn =>
                        {
                            itemsColumn.Spacing(6);
                            itemsColumn.Item().Text(notesTitle).Bold();

                            if (lineItems.Any())
                            {
                                foreach (var item in lineItems)
                                {
                                    itemsColumn.Item().Row(row =>
                                    {
                                        row.RelativeItem().Text(text =>
                                        {
                                            text.Span(item.Description);
                                            if (item.Quantity.HasValue)
                                            {
                                                text.Span($"  × {FormatQuantity(item.Quantity.Value)}").FontColor("#475569");
                                            }
                                            if (item.UnitPrice.HasValue)
                                            {
                                                text.Span($"  @ {item.UnitPrice.Value:F2} {invoice.Currency}")
                                                    .FontColor("#475569");
                                            }
                                        });
                                        if (item.Total.HasValue)
                                        {
                                            row.AutoItem().Text($"{item.Total.Value:F2} {invoice.Currency}")
                                                .FontColor("#2563EB");
                                        }
                                    });
                                }
                            }
                            else
                            {
                                itemsColumn.Item().Text(t.NoNotes).Italic();
                            }
                        });

                        column.Item().AlignRight().Text($"{t.Total}: {invoice.Amount:F2} {invoice.Currency}")
                            .FontSize(16)
                            .Bold();

                        column.Item().Container().Background("#F1F5F9").Padding(10).Column(box =>
                        {
                            box.Item().Text(t.UpgradeHint)
                                .FontSize(10)
                                .FontColor("#475569");
                        });
                    });

                page.Footer()
                    .AlignCenter()
                    .Text(t.FooterBasic)
                    .FontSize(9)
                    .FontColor(Colors.Grey.Darken2);
            });
        });
    }

    private static IDocument CreateAdvancedTemplate(
        Invoice invoice,
        User user,
        IReadOnlyList<PdfLineItem> lineItems,
        string notesTitle,
        DateTime paymentDate,
        PdfTexts t)
    {
        var billedTo = string.IsNullOrWhiteSpace(invoice.CustomerName)
            ? null
            : invoice.CustomerName.Trim();
        var businessName = user.CompanyName ?? user.Email;
        var invoiceNumber = invoice.Id.ToString("N")[..8].ToUpper();
        var planLabel = (user.Plan ?? "pro").ToUpperInvariant();

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(2, Unit.Centimetre);
                page.PageColor("#F8FAFC");
                page.DefaultTextStyle(x => x.FontSize(12).FontColor("#1E293B"));

                page.Header().Container().Background("#1E3A8A").Padding(20).Column(column =>
                {
                    column.Spacing(8);
                    column.Item().Text(t.ProHeader)
                        .FontSize(26)
                        .FontColor(Colors.White)
                        .SemiBold();
                    column.Item().Text($"{t.PlanLabel}: {planLabel}")
                        .FontColor("#C7D2FE");
                });

                page.Content()
                    .PaddingVertical(20)
                    .Column(column =>
                    {
                        column.Spacing(20);

                        column.Item().Row(row =>
                        {
                            row.Spacing(20);
                            row.RelativeItem().Container().Background("#EEF2FF").Padding(16).Column(info =>
                            {
                                info.Spacing(10);
                                info.Item().Text(t.PreparedBy).FontColor("#1E3A8A").Bold();
                                info.Item().Text(businessName).FontSize(12);
                                info.Item().Text(user.Email).FontSize(11).FontColor("#334155");
                                info.Item().Text($"{t.InvoiceNumber}: {invoiceNumber}").FontSize(11).FontColor("#334155");
                            });

                            if (!string.IsNullOrWhiteSpace(billedTo))
                            {
                                row.RelativeItem().Container().Background("#E0E7FF").Padding(16).Column(recipient =>
                                {
                                    recipient.Spacing(10);
                                    recipient.Item().Text(t.BilledTo).FontColor("#1E3A8A").Bold();
                                    recipient.Item().Text(billedTo).FontSize(12);
                                    recipient.Item().Text($"{t.Issued}: {invoice.InvoiceDate:dd MMM yyyy}").FontSize(11);
                                    recipient.Item().Text($"{t.PaymentDate}: {paymentDate:dd MMM yyyy}").FontSize(11);
                                });
                            }
                        });

                        column.Item().Row(row =>
                        {
                            row.Spacing(20);
                            row.RelativeItem().Container().Border(1).BorderColor("#CBD5F5").Padding(16).Column(items =>
                            {
                                items.Spacing(12);
                                items.Item().Text(notesTitle).Bold().FontColor("#1E3A8A");

                                if (lineItems.Any())
                                {
                                    var index = 1;
                                    foreach (var item in lineItems)
                                    {
                                        var position = index++;
                                        items.Item().Row(rowItem =>
                                        {
                                            rowItem.RelativeItem().Text(text =>
                                            {
                                                text.Span($"{position}. {item.Description}");
                                                if (item.Quantity.HasValue)
                                                {
                                                    text.Span($"  × {FormatQuantity(item.Quantity.Value)}").FontColor("#475569");
                                                }
                                                if (item.UnitPrice.HasValue)
                                                {
                                                    text.Span($"  @ {item.UnitPrice.Value:F2} {invoice.Currency}")
                                                        .FontColor("#475569");
                                                }
                                            });
                                            if (item.Total.HasValue)
                                            {
                                                rowItem.AutoItem().Text($"{item.Total.Value:F2} {invoice.Currency}")
                                                    .FontColor("#1E3A8A")
                                                    .Bold();
                                            }
                                        });
                                    }
                                }
                                else
                                {
                                    items.Item().Text(t.NoNotes).Italic();
                                }
                            });

                            row.ConstantItem(180).Container().Background("#1E3A8A").Padding(16).Column(summary =>
                            {
                                summary.Spacing(8);
                                summary.Item().Text(t.Summary).FontColor(Colors.White).SemiBold();
                                summary.Item().Text(t.TotalAmount).FontColor("#C7D2FE");
                                summary.Item().Text($"{invoice.Amount:F2} {invoice.Currency}")
                                    .FontSize(20)
                                    .FontColor(Colors.White)
                                    .Bold();
                                summary.Item().Text($"{t.PaidOn} {paymentDate:dd MMM yyyy}")
                                    .FontColor("#C7D2FE");
                                summary.Item().Text(t.ProSummaryNote)
                                    .FontSize(10)
                                    .FontColor("#E0E7FF");
                            });
                        });
                    });

                page.Footer()
                    .PaddingTop(10)
                    .AlignCenter()
                    .Text(text =>
                    {
                        text.Span(t.ProFooter).FontColor("#1E3A8A");
                    });
            });
        });
    }

    private static IDocument CreateEliteTemplate(
        Invoice invoice,
        User user,
        IReadOnlyList<PdfLineItem> lineItems,
        string notesTitle,
        DateTime paymentDate,
        PdfTexts t)
    {
        var billedTo = string.IsNullOrWhiteSpace(invoice.CustomerName)
            ? null
            : invoice.CustomerName.Trim();
        var businessName = user.CompanyName ?? "InvoiceEasy Elite";
        var shortInvoiceId = invoice.Id.ToString("N")[..8].ToUpper();
        var planLabel = (user.Plan ?? "elite").ToUpperInvariant();
        var paymentStatus = t.PaymentAccepted;

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(0);
                page.PageColor("#0F172A");
                page.DefaultTextStyle(x => x.FontSize(12).FontColor(Colors.White));

                page.Content().Padding(40).Column(column =>
                {
                    column.Spacing(24);

                    column.Item().Row(row =>
                    {
                        row.RelativeItem().Column(left =>
                        {
                            left.Spacing(6);
                            left.Item().Text(businessName)
                                .FontSize(30)
                                .Bold();
                            left.Item().Text($"{t.ElitePlanLabel} • {planLabel}")
                                .FontColor("#38BDF8");
                        });
                        row.AutoItem().Column(meta =>
                        {
                            meta.Spacing(4);
                            meta.Item().Text($"{t.InvoiceNumberShort} {shortInvoiceId}")
                                .FontColor("#38BDF8")
                                .AlignRight();
                            meta.Item().Text($"{t.Issued} {invoice.InvoiceDate:dd MMM yyyy}")
                                .FontSize(11)
                                .AlignRight();
                            meta.Item().Text($"{t.PaymentDate} {paymentDate:dd MMM yyyy}")
                                .FontSize(11)
                                .FontColor("#94A3B8")
                                .AlignRight();
                        });
                    });

                    column.Item().Row(row =>
                    {
                        if (!string.IsNullOrWhiteSpace(billedTo))
                        {
                            row.RelativeItem().Container().Background("#1E293B").Padding(16).Column(info =>
                            {
                                info.Spacing(8);
                                info.Item().Text(t.Client).FontColor("#38BDF8").SemiBold();
                                info.Item().Text(billedTo).FontSize(13);
                                info.Item().Text(user.Email).FontSize(11).FontColor("#94A3B8");
                            });
                        }

                        row.RelativeItem().Container().Background("#1E293B").Padding(16).Column(info =>
                        {
                            info.Spacing(8);
                            info.Item().Text(t.EliteBenefits).FontColor("#38BDF8").SemiBold();
                            info.Item().Text($"• {t.EliteBenefit1}").FontColor("#E0F2FE").FontSize(11);
                            info.Item().Text($"• {t.EliteBenefit2}").FontColor("#E0F2FE").FontSize(11);
                            info.Item().Text($"• {t.EliteBenefit3}").FontColor("#E0F2FE").FontSize(11);
                        });
                    });

                    column.Item().Row(row =>
                    {
                        row.Spacing(12);
                        row.RelativeItem().Container().Background("#38BDF8").Padding(18).Column(card =>
                        {
                            card.Item().Text(t.TotalDue).FontColor("#0F172A").SemiBold();
                            card.Item().Text($"{invoice.Amount:F2} {invoice.Currency}")
                                .FontSize(26)
                                .FontColor("#0F172A")
                                .Bold();
                        });
                        row.RelativeItem().Container().Background("#1E293B").Padding(18).Column(card =>
                        {
                            card.Item().Text(t.PaymentStatus).FontColor("#38BDF8").SemiBold();
                            card.Item().Text(paymentStatus).FontColor("#E2E8F0");
                            card.Item().Text($"{t.Reference}: ELITE-{shortInvoiceId}")
                                .FontSize(11)
                                .FontColor("#94A3B8");
                        });
                        row.RelativeItem().Container().Background("#1E293B").Padding(18).Column(card =>
                        {
                            card.Item().Text(t.PaymentDate).FontColor("#38BDF8").SemiBold();
                            card.Item().Text(paymentDate.ToString("dd MMM yyyy")).FontColor(Colors.White);
                        });
                    });

                    column.Item().Column(list =>
                    {
                        list.Spacing(12);
                        list.Item().Text(notesTitle)
                            .FontSize(16)
                            .FontColor("#38BDF8")
                            .SemiBold();

                        if (lineItems.Any())
                        {
                            var index = 1;
                            list.Item().Column(items =>
                            {
                                items.Spacing(10);
                                foreach (var item in lineItems)
                                {
                                    var current = index++;
                                    items.Item().Container().Background("#1E293B").Padding(16).Row(row =>
                                    {
                                        row.ConstantItem(40).Text($"{current:00}")
                                            .FontSize(16)
                                            .FontColor("#38BDF8")
                                            .Bold();
                                        row.RelativeItem().Column(card =>
                                        {
                                            card.Item().Text(item.Description).FontSize(13);
                                            if (item.Quantity.HasValue || item.UnitPrice.HasValue)
                                            {
                                                card.Item().Text(text =>
                                                {
                                                    if (item.Quantity.HasValue)
                                                    {
                                                        text.Span($"×{FormatQuantity(item.Quantity.Value)}").FontColor("#94A3B8").FontSize(11);
                                                    }
                                                    if (item.UnitPrice.HasValue)
                                                    {
                                                        if (item.Quantity.HasValue) text.Span(" · ").FontSize(11).FontColor("#94A3B8");
                                                        text.Span($"{item.UnitPrice.Value:F2} {invoice.Currency} ea").FontColor("#94A3B8").FontSize(11);
                                                    }
                                                });
                                            }
                                        });
                                        if (item.Total.HasValue)
                                        {
                                            row.AutoItem().Text($"{item.Total.Value:F2} {invoice.Currency}")
                                                .FontColor("#38BDF8")
                                                .Bold();
                                        }
                                    });
                                }
                            });
                        }
                        else
                        {
                            list.Item().Container().Background("#1E293B").Padding(16).Text(t.NoNotes)
                                .FontSize(12)
                                .FontColor("#E2E8F0");
                        }
                    });

                    column.Item().Container().Background("#0F172A").Border(1).BorderColor("#1E293B").Padding(20).Row(row =>
                    {
                        row.RelativeItem().Column(left =>
                        {
                            left.Item().Text("Concierge support").FontColor("#38BDF8").SemiBold();
                            left.Item().Text(t.ConciergeBody)
                                .FontSize(11)
                                .FontColor("#E2E8F0");
                            left.Item().Text($"{t.Contact}: {user.Email}")
                                .FontSize(11)
                                .FontColor("#94A3B8");
                        });
                        row.AutoItem().Column(right =>
                        {
                            right.Item().Text(t.ElectronicSignature).FontColor("#38BDF8").SemiBold().AlignRight();
                            right.Item().Text(businessName)
                                .FontSize(14)
                                .Bold()
                                .AlignRight();
                            right.Item().Text(t.AuthorizedRepresentative)
                                .FontSize(10)
                                .FontColor("#94A3B8")
                                .AlignRight();
                        });
                    });

                    column.Item().Text(t.EliteFooter)
                        .FontSize(9)
                        .FontColor("#94A3B8")
                        .AlignCenter();
                });
            });
        });
    }

    private static IReadOnlyList<PdfLineItem> ResolveLineItems(Invoice invoice)
    {
        var stored = DeserializeStoredLineItems(invoice.LineItemsJson);
        if (stored.Count > 0)
        {
            return stored;
        }

        return ExtractLineItems(invoice.ServiceDescription);
    }

    private static List<PdfLineItem> DeserializeStoredLineItems(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new List<PdfLineItem>();
        }

        try
        {
            var stored = JsonSerializer.Deserialize<List<StoredLineItem>>(json);
            if (stored == null)
            {
                return new List<PdfLineItem>();
            }

            return stored
                .Where(i => !string.IsNullOrWhiteSpace(i.Description))
                .Select(i => new PdfLineItem(
                    i.Description!.Trim(),
                    i.Quantity,
                    i.UnitPrice,
                    i.TotalPrice))
                .ToList();
        }
        catch
        {
            return new List<PdfLineItem>();
        }
    }

    private static IReadOnlyList<PdfLineItem> ExtractLineItems(string serviceDescription)
    {
        if (string.IsNullOrWhiteSpace(serviceDescription))
            return Array.Empty<PdfLineItem>();

        var separators = new[] { "\r\n", "\n", ";", "•" };

        var items = serviceDescription
            .Split(separators, StringSplitOptions.RemoveEmptyEntries)
            .Select(i => i.Replace("•", string.Empty).Trim())
            .Where(i => !string.IsNullOrWhiteSpace(i))
            .Select(text => new PdfLineItem(text, null, null, null))
            .ToList();

        if (items.Count == 0)
        {
            items.Add(new PdfLineItem(serviceDescription.Trim(), null, null, null));
        }

        return items;
    }

    private static string FormatQuantity(decimal value)
    {
        return value % 1 == 0 ? value.ToString("0") : value.ToString("0.##");
    }
}

internal class PdfTexts
{
    public string InvoiceTitle { get; init; } = "Invoice";
    public string From { get; init; } = "From";
    public string To { get; init; } = "To";
    public string InvoiceDate { get; init; } = "Invoice Date";
    public string PaymentStatus { get; init; } = "Payment Status";
    public string PaymentAccepted { get; init; } = "Payment accepted";
    public string PaymentDate { get; init; } = "Payment Date";
    public string NotesLabel { get; init; } = "Notes";
    public string NoNotes { get; init; } = "No notes were provided.";
    public string Total { get; init; } = "Total";
    public string UpgradeHint { get; init; } = "Want richer PDFs? Upgrade to Pro or Elite to unlock detailed breakdowns, richer payment summaries, and premium branding.";
    public string FooterBasic { get; init; } = "Generated by InvoiceEasy • No VAT shown according to § 19 UStG.";

    public string ProHeader { get; init; } = "Professional billing summary";
    public string PlanLabel { get; init; } = "Plan";
    public string PreparedBy { get; init; } = "Prepared by";
    public string InvoiceNumber { get; init; } = "Invoice #";
    public string InvoiceNumberShort { get; init; } = "Invoice #";
    public string BilledTo { get; init; } = "Billed to";
    public string Issued { get; init; } = "Issued";
    public string Summary { get; init; } = "Summary";
    public string TotalAmount { get; init; } = "Total amount";
    public string PaidOn { get; init; } = "Paid on";
    public string ProSummaryNote { get; init; } = "Includes itemized breakdown with branded styling.";
    public string PlanAdvantageTitle { get; init; } = "Plan advantage";
    public string PlanAdvantageBody { get; init; } = "Pro templates include plan labeling and structured summaries to reassure your clients.";
    public string ProFooter { get; init; } = "InvoiceEasy Pro template";

    public string ElitePlanLabel { get; init; } = "Elite plan";
    public string Client { get; init; } = "Client";
    public string EliteBenefits { get; init; } = "Elite benefits";
    public string EliteBenefit1 { get; init; } = "Concierge success manager";
    public string EliteBenefit2 { get; init; } = "Automatic backups";
    public string EliteBenefit3 { get; init; } = "Signature-worthy PDF showcase";
    public string TotalDue { get; init; } = "Total due";
    public string Reference { get; init; } = "Reference";
    public string ConciergeSupport { get; init; } = "Concierge support";
    public string ConciergeBody { get; init; } = "Need adjustments? Reply directly to this invoice or call your dedicated manager.";
    public string Contact { get; init; } = "Contact";
    public string ElectronicSignature { get; init; } = "Electronic signature";
    public string AuthorizedRepresentative { get; init; } = "Authorized representative";
    public string EliteFooter { get; init; } = "Powered by InvoiceEasy Elite • Premium automation and concierge success team";

    public static PdfTexts For(string? locale)
    {
        return new PdfTexts();
    }
}
