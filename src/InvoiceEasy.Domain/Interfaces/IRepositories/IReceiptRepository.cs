using InvoiceEasy.Domain.Models;

namespace InvoiceEasy.Domain.Interfaces.Repositories;

public interface IReceiptRepository
{
    Task<Receipt> AddAsync(Receipt receipt);
    Task<Receipt?> GetByIdAsync(Guid id);
    Task<IEnumerable<Receipt>> GetAllAsync();
    Task UpdateAsync(Receipt receipt);
    Task DeleteAsync(Guid id);
}
