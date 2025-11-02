using InvoiceEasy.Domain.Entities;

namespace InvoiceEasy.Domain.Interfaces;

public interface IRefreshTokenRepository : IRepository<RefreshToken>
{
    Task<RefreshToken?> GetByTokenAsync(string token);
    Task RevokeAllByUserIdAsync(Guid userId);
}

