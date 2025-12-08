using InvoiceEasy.Domain.Entities;

namespace InvoiceEasy.Domain.Interfaces;

public interface IExpenseRepository : IRepository<Expense>
{
    Task<IEnumerable<Expense>> GetByUserIdAsync(Guid userId, int page, int pageSize);
    Task<int> CountByUserIdAndMonthAsync(Guid userId, DateTime monthStart);
    Task<decimal> SumAmountByUserIdAndMonthAsync(Guid userId, DateTime monthStart, DateTime monthEnd);
    Task<decimal> SumAmountByUserIdAsync(Guid userId);
}
