using InvoiceEasy.Domain.Models;
using System.IO;

namespace InvoiceEasy.Domain.Interfaces.Services;

public interface IReceiptService
{
    Task<Receipt> ProcessReceiptUploadAsync(Stream fileStream, string fileName);
    Task<Receipt> GetReceiptAsync(Guid id);
    Task<IEnumerable<Receipt>> GetAllReceiptsAsync();
    Task DeleteReceiptAsync(Guid id);
}
