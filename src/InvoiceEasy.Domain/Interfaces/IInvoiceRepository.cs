using InvoiceEasy.Domain.Entities;

namespace InvoiceEasy.Domain.Interfaces;

public interface IInvoiceRepository : IRepository<Invoice>
{
    Task<IEnumerable<Invoice>> GetByUserIdAsync(Guid userId, int page, int pageSize);
    Task<int> CountByUserIdAndMonthAsync(Guid userId, DateTime monthStart);
    Task<decimal> SumAmountByUserIdAndMonthAsync(Guid userId, DateTime monthStart, DateTime monthEnd);
}

