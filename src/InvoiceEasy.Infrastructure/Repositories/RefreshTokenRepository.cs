using InvoiceEasy.Domain.Entities;
using InvoiceEasy.Domain.Interfaces;
using InvoiceEasy.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace InvoiceEasy.Infrastructure.Repositories;

public class RefreshTokenRepository : BaseRepository<RefreshToken>, IRefreshTokenRepository
{
    public RefreshTokenRepository(ApplicationDbContext context) : base(context)
    {
    }

    public async Task<RefreshToken?> GetByTokenAsync(string token)
    {
        return await _dbSet
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.Token == token && !t.IsRevoked);
    }

    public async Task RevokeAllByUserIdAsync(Guid userId)
    {
        var tokens = await _dbSet
            .Where(t => t.UserId == userId && !t.IsRevoked)
            .ToListAsync();
        
        foreach (var token in tokens)
        {
            token.IsRevoked = true;
        }
        
        await _context.SaveChangesAsync();
    }
}

