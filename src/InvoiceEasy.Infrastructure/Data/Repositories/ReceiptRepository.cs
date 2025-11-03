using InvoiceEasy.Domain.Interfaces.Repositories;
using InvoiceEasy.Domain.Models;
using InvoiceEasy.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace InvoiceEasy.Infrastructure.Data.Repositories;

public class ReceiptRepository : IReceiptRepository
{
    private readonly ApplicationDbContext _context;

    public ReceiptRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Receipt> AddAsync(Receipt receipt)
    {
        _context.Receipts.Add(receipt);
        await _context.SaveChangesAsync();
        return receipt;
    }

    public async Task<Receipt?> GetByIdAsync(Guid id)
    {
        return await _context.Receipts.FindAsync(id);
    }

    public async Task<IEnumerable<Receipt>> GetAllAsync()
    {
        return await _context.Receipts.ToListAsync();
    }

    public async Task UpdateAsync(Receipt receipt)
    {
        _context.Entry(receipt).State = EntityState.Modified;
        await _context.SaveChangesAsync();
    }

    public async Task DeleteAsync(Guid id)
    {
        var receipt = await _context.Receipts.FindAsync(id);
        if (receipt != null)
        {
            _context.Receipts.Remove(receipt);
            await _context.SaveChangesAsync();
        }
    }
}
