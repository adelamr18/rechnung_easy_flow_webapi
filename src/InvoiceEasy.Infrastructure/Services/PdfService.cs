using InvoiceEasy.Domain.Entities;
using InvoiceEasy.Domain.Interfaces;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace InvoiceEasy.Infrastructure.Services;

public class PdfService
{
    private readonly IFileStorage _fileStorage;
    private readonly string _baseUrl;

    public PdfService(IFileStorage fileStorage, string baseUrl)
    {
        _fileStorage = fileStorage;
        _baseUrl = baseUrl;
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public async Task<string> GenerateInvoicePdfAsync(Invoice invoice, User user)
    {
        var fileName = $"invoice_{invoice.Id}.pdf";
        var filePath = $"invoices/{fileName}";

        var pdfStream = new MemoryStream();
        
        var document = Document.Create(container =>
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
                        column.Item().Text($"Rechnung")
                            .FontSize(24)
                            .FontColor("#B7D3F2")
                            .Bold();
                        
                        column.Item().PaddingTop(1, Unit.Centimetre);
                        column.Item().Row(row =>
                        {
                            row.Spacing(2, Unit.Centimetre);
                            row.RelativeItem().Column(column =>
                            {
                                column.Item().Text("Rechnungssteller:").Bold();
                                column.Item().Text(user.CompanyName ?? user.Email);
                                column.Item().Text(user.Email);
                            });
                            row.RelativeItem().Column(column =>
                            {
                                column.Item().Text("Rechnungsempfänger:").Bold();
                                column.Item().Text(invoice.CustomerName);
                            });
                        });
                    });

                page.Content()
                    .PaddingVertical(1, Unit.Centimetre)
                    .Column(column =>
                    {
                        column.Item().Text($"Rechnungsdatum: {invoice.InvoiceDate:dd.MM.yyyy}")
                            .FontSize(14)
                            .Bold()
                            .PaddingBottom(0.5, Unit.Centimetre);

                        column.Item().Text("Leistung:").Bold().PaddingTop(1, Unit.Centimetre);
                        column.Item().Text(invoice.ServiceDescription)
                            .PaddingBottom(1, Unit.Centimetre);

                        column.Item().AlignRight().Text($"Betrag: {invoice.Amount:F2} {invoice.Currency}")
                            .FontSize(18)
                            .Bold()
                            .PaddingTop(1, Unit.Centimetre);
                    });

                page.Footer()
                    .AlignCenter()
                    .PaddingTop(1, Unit.Centimetre)
                    .Text("Gemäß § 19 UStG enthält der ausgewiesene Betrag keine Umsatzsteuer.")
                    .FontSize(9)
                    .FontColor(Colors.Grey.Medium);
            });
        });

        document.GeneratePdf(pdfStream);
        pdfStream.Position = 0;

        var savedPath = await _fileStorage.SaveFileAsync(pdfStream, fileName, "invoices");
        return savedPath;
    }
}

