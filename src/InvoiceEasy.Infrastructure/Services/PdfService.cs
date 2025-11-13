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

        var document = template switch
        {
            InvoicePdfTemplate.Advanced => CreateAdvancedTemplate(invoice, user, lineItems, GenerateNotesTitle(invoice)),
            InvoicePdfTemplate.Elite => CreateEliteTemplate(invoice, user, lineItems, GenerateNotesTitle(invoice)),
            _ => CreateBasicTemplate(invoice, user, lineItems, GenerateNotesTitle(invoice))
        };

        document.GeneratePdf(pdfStream);
        pdfStream.Position = 0;

        var savedPath = await _fileStorage.SaveFileAsync(pdfStream, fileName, "invoices");
        return savedPath;
    }

    private static string GenerateNotesTitle(Invoice invoice)
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

        return $"Notes • {datePart} • {anchor}";
    }

    private static IDocument CreateBasicTemplate(Invoice invoice, User user, IReadOnlyList<PdfLineItem> lineItems, string notesTitle)
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
                        column.Item().Text("Invoice")
                            .FontSize(22)
                            .FontColor("#2563EB")
                            .Bold();

                        column.Item().Row(row =>
                        {
                            row.RelativeItem().Column(sender =>
                            {
                                sender.Spacing(4);
                                sender.Item().Text("From").Bold();
                                sender.Item().Text(businessName);
                                sender.Item().Text(user.Email);
                            });

                            if (!string.IsNullOrWhiteSpace(billedTo))
                            {
                                row.RelativeItem().Column(recipient =>
                                {
                                    recipient.Spacing(4);
                                    recipient.Item().Text("To").Bold();
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
                        column.Item().Text($"Invoice Date: {invoice.InvoiceDate:dd.MM.yyyy}")
                            .FontSize(13)
                            .Bold();

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
                                itemsColumn.Item().Text("No notes were provided.").Italic();
                            }
                        });

                        column.Item().AlignRight().Text($"Total: {invoice.Amount:F2} {invoice.Currency}")
                            .FontSize(16)
                            .Bold();

                        column.Item().Container().Background("#F1F5F9").Padding(10).Column(box =>
                        {
                            box.Item().Text("Want richer PDFs? Upgrade to Pro or Elite to unlock detailed breakdowns, payment instructions, and premium branding.")
                                .FontSize(10)
                                .FontColor("#475569");
                        });
                    });

                page.Footer()
                    .AlignCenter()
                    .Text("Generated by InvoiceEasy • No VAT shown according to § 19 UStG.")
                    .FontSize(9)
                    .FontColor(Colors.Grey.Darken2);
            });
        });
    }

    private static IDocument CreateAdvancedTemplate(Invoice invoice, User user, IReadOnlyList<PdfLineItem> lineItems, string notesTitle)
    {
        var billedTo = string.IsNullOrWhiteSpace(invoice.CustomerName)
            ? null
            : invoice.CustomerName.Trim();
        var businessName = user.CompanyName ?? user.Email;
        var invoiceNumber = invoice.Id.ToString("N")[..8].ToUpper();
        var dueDate = invoice.InvoiceDate.AddDays(14);
        var paymentReference = $"INV-{invoice.InvoiceDate:yyyyMMdd}-{invoiceNumber[..4]}";
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
                    column.Item().Text("Professional billing summary")
                        .FontSize(26)
                        .FontColor(Colors.White)
                        .SemiBold();
                    column.Item().Text($"Plan: {planLabel}")
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
                                info.Item().Text("Prepared by").FontColor("#1E3A8A").Bold();
                                info.Item().Text(businessName).FontSize(12);
                                info.Item().Text(user.Email).FontSize(11).FontColor("#334155");
                                info.Item().Text($"Invoice #: {invoiceNumber}").FontSize(11).FontColor("#334155");
                            });

                            if (!string.IsNullOrWhiteSpace(billedTo))
                            {
                                row.RelativeItem().Container().Background("#E0E7FF").Padding(16).Column(recipient =>
                                {
                                    recipient.Spacing(10);
                                    recipient.Item().Text("Billed to").FontColor("#1E3A8A").Bold();
                                    recipient.Item().Text(billedTo).FontSize(12);
                                    recipient.Item().Text($"Issued: {invoice.InvoiceDate:dd MMM yyyy}").FontSize(11);
                                    recipient.Item().Text($"Due: {dueDate:dd MMM yyyy}").FontSize(11);
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
                                    items.Item().Text("No notes were provided.").Italic();
                                }
                            });

                            row.ConstantItem(180).Container().Background("#1E3A8A").Padding(16).Column(summary =>
                            {
                                summary.Spacing(8);
                                summary.Item().Text("Summary").FontColor(Colors.White).SemiBold();
                                summary.Item().Text($"Total amount").FontColor("#C7D2FE");
                                summary.Item().Text($"{invoice.Amount:F2} {invoice.Currency}")
                                    .FontSize(20)
                                    .FontColor(Colors.White)
                                    .Bold();
                                summary.Item().Text($"Due by {dueDate:dd MMM yyyy}")
                                    .FontColor("#C7D2FE");
                                summary.Item().Text("Includes itemized breakdown and payment reminders.")
                                    .FontSize(10)
                                    .FontColor("#E0E7FF");
                            });
                        });

                        column.Item().Row(row =>
                        {
                            row.Spacing(20);
                            row.RelativeItem().Container().Background("#F8FAFC").Padding(16).Column(payment =>
                            {
                                payment.Spacing(8);
                                payment.Item().Text("Payment instructions").Bold().FontColor("#1E3A8A");
                                payment.Item().Text($"Reference: {paymentReference}").FontSize(11);
                                payment.Item().Text("Bank: InvoiceEasy Demo Bank").FontSize(11);
                                payment.Item().Text("IBAN: DE00 1234 5678 9000 0000 01 • BIC: INVEDEFFXXX").FontSize(11);
                                payment.Item().Text("Please settle within 14 days. Late fees may apply.")
                                    .FontSize(10)
                                    .FontColor("#475569");
                            });

                            row.RelativeItem().Container().Border(1).BorderColor("#E2E8F0").Padding(16).Column(notes =>
                            {
                                notes.Spacing(8);
                                notes.Item().Text("Plan advantage").Bold();
                                notes.Item().Text("Pro templates include payment guidance, plan labeling, and structured service summaries to reassure your clients.")
                                    .FontSize(11)
                                    .FontColor("#475569");
                            });
                        });
                    });

                page.Footer()
                    .PaddingTop(10)
                    .AlignCenter()
                    .Text(text =>
                    {
                        text.Span("InvoiceEasy Pro template • ").FontColor("#1E3A8A");
                        text.Span("Includes automatic payment instructions & reminders.").FontSize(9);
                    });
            });
        });
    }

    private static IDocument CreateEliteTemplate(Invoice invoice, User user, IReadOnlyList<PdfLineItem> lineItems, string notesTitle)
    {
        var billedTo = string.IsNullOrWhiteSpace(invoice.CustomerName)
            ? null
            : invoice.CustomerName.Trim();
        var businessName = user.CompanyName ?? "InvoiceEasy Elite";
        var shortInvoiceId = invoice.Id.ToString("N")[..8].ToUpper();
        var dueDate = invoice.InvoiceDate.AddDays(7);
        var planLabel = (user.Plan ?? "elite").ToUpperInvariant();

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
                            left.Item().Text($"Elite plan • {planLabel}")
                                .FontColor("#38BDF8");
                        });
                        row.AutoItem().Column(meta =>
                        {
                            meta.Spacing(4);
                            meta.Item().Text($"Invoice #{shortInvoiceId}")
                                .FontColor("#38BDF8")
                                .AlignRight();
                            meta.Item().Text($"Issued {invoice.InvoiceDate:dd MMM yyyy}")
                                .FontSize(11)
                                .AlignRight();
                            meta.Item().Text($"Due in 7 days ({dueDate:dd MMM yyyy})")
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
                                info.Item().Text("Client").FontColor("#38BDF8").SemiBold();
                                info.Item().Text(billedTo).FontSize(13);
                                info.Item().Text(user.Email).FontSize(11).FontColor("#94A3B8");
                            });
                        }

                        row.RelativeItem().Container().Background("#1E293B").Padding(16).Column(info =>
                        {
                            info.Spacing(8);
                            info.Item().Text("Premium features").FontColor("#38BDF8").SemiBold();
                            info.Item().Text("• Concierge support").FontColor("#E0F2FE").FontSize(11);
                            info.Item().Text("• Automatic reminders").FontColor("#E0F2FE").FontSize(11);
                            info.Item().Text("• Branded PDF showcase").FontColor("#E0F2FE").FontSize(11);
                        });
                    });

                    column.Item().Row(row =>
                    {
                        row.Spacing(12);
                        row.RelativeItem().Container().Background("#38BDF8").Padding(18).Column(card =>
                        {
                            card.Item().Text("Total due").FontColor("#0F172A").SemiBold();
                            card.Item().Text($"{invoice.Amount:F2} {invoice.Currency}")
                                .FontSize(26)
                                .FontColor("#0F172A")
                                .Bold();
                        });
                        row.RelativeItem().Container().Background("#1E293B").Padding(18).Column(card =>
                        {
                            card.Item().Text("Payment status").FontColor("#38BDF8").SemiBold();
                            card.Item().Text("Pending confirmation").FontColor("#E2E8F0");
                            card.Item().Text($"Reference: ELITE-{shortInvoiceId}")
                                .FontSize(11)
                                .FontColor("#94A3B8");
                        });
                        row.RelativeItem().Container().Background("#1E293B").Padding(18).Column(card =>
                        {
                            card.Item().Text("Due date").FontColor("#38BDF8").SemiBold();
                            card.Item().Text(dueDate.ToString("dd MMM yyyy")).FontColor(Colors.White);
                            card.Item().Text("Priority reminders enabled").FontSize(11).FontColor("#94A3B8");
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
                            list.Item().Container().Background("#1E293B").Padding(16).Text("No notes captured for this receipt.")
                                .FontSize(12)
                                .FontColor("#E2E8F0");
                        }
                    });

                    column.Item().Container().Background("#0F172A").Border(1).BorderColor("#1E293B").Padding(20).Row(row =>
                    {
                        row.RelativeItem().Column(left =>
                        {
                            left.Item().Text("Concierge support").FontColor("#38BDF8").SemiBold();
                            left.Item().Text("Need adjustments? Reply directly to this invoice or call your dedicated manager.")
                                .FontSize(11)
                                .FontColor("#E2E8F0");
                            left.Item().Text($"Contact: {user.Email}")
                                .FontSize(11)
                                .FontColor("#94A3B8");
                        });
                        row.AutoItem().Column(right =>
                        {
                            right.Item().Text("Electronic signature").FontColor("#38BDF8").SemiBold().AlignRight();
                            right.Item().Text(businessName)
                                .FontSize(14)
                                .Bold()
                                .AlignRight();
                            right.Item().Text("Authorized representative")
                                .FontSize(10)
                                .FontColor("#94A3B8")
                                .AlignRight();
                        });
                    });

                    column.Item().Text("Powered by InvoiceEasy Elite • Premium automation, reminders, and concierge success team")
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
