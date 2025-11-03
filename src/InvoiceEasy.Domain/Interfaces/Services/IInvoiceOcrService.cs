using InvoiceEasy.Domain.Models;

namespace InvoiceEasy.Domain.Interfaces.Services;

public interface IInvoiceOcrService
{
    Task<InvoiceAnalysisResult> AnalyzeAsync(Stream fileStream, string fileName);
}
