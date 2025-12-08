using InvoiceEasy.Domain.Entities;
using InvoiceEasy.Domain.Interfaces;
using InvoiceEasy.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace InvoiceEasy.Infrastructure.Repositories;

public class ExpenseRepository : BaseRepository<Expense>, IExpenseRepository
{
    public ExpenseRepository(ApplicationDbContext context) : base(context)
    {
    }

    public async Task<IEnumerable<Expense>> GetByUserIdAsync(Guid userId, int page, int pageSize)
    {
        return await _dbSet
            .Where(e => e.UserId == userId)
            .OrderByDescending(e => e.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();
    }

    public async Task<int> CountByUserIdAndMonthAsync(Guid userId, DateTime monthStart)
    {
        return await _dbSet
            .CountAsync(e => e.UserId == userId && e.CreatedAt >= monthStart);
    }

    public async Task<decimal> SumAmountByUserIdAndMonthAsync(Guid userId, DateTime monthStart, DateTime monthEnd)
    {
        return await _dbSet
            .Where(e => e.UserId == userId && 
                       e.ExpenseDate >= DateOnly.FromDateTime(monthStart) && 
                       e.ExpenseDate < DateOnly.FromDateTime(monthEnd))
            .SumAsync(e => e.Amount);
    }

    public async Task<decimal> SumAmountByUserIdAsync(Guid userId)
    {
        var total = await _dbSet
            .Where(e => e.UserId == userId)
            .Select(e => (decimal?)e.Amount)
            .SumAsync();

        return total ?? 0m;
    }
}
