using InvoiceEasy.Domain.Entities;
using InvoiceEasy.Domain.Interfaces;
using InvoiceEasy.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace InvoiceEasy.Infrastructure.Repositories;

public class InvoiceRepository : BaseRepository<Invoice>, IInvoiceRepository
{
    public InvoiceRepository(ApplicationDbContext context) : base(context)
    {
    }

    public async Task<IEnumerable<Invoice>> GetByUserIdAsync(Guid userId, int page, int pageSize)
    {
        return await _dbSet
            .Where(i => i.UserId == userId)
            .OrderByDescending(i => i.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();
    }

    public async Task<int> CountByUserIdAndMonthAsync(Guid userId, DateTime monthStart)
    {
        return await _dbSet
            .CountAsync(i => i.UserId == userId && i.CreatedAt >= monthStart);
    }

    public async Task<decimal> SumAmountByUserIdAndMonthAsync(Guid userId, DateTime monthStart, DateTime monthEnd)
    {
        return await _dbSet
            .Where(i => i.UserId == userId && 
                       i.InvoiceDate >= DateOnly.FromDateTime(monthStart) && 
                       i.InvoiceDate < DateOnly.FromDateTime(monthEnd))
            .SumAsync(i => i.Amount);
    }
}

