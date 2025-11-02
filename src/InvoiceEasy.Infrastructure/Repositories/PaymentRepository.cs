using InvoiceEasy.Domain.Entities;
using InvoiceEasy.Domain.Interfaces;
using InvoiceEasy.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace InvoiceEasy.Infrastructure.Repositories;

public class PaymentRepository : BaseRepository<Payment>, IPaymentRepository
{
    public PaymentRepository(ApplicationDbContext context) : base(context)
    {
    }

    public async Task<Payment?> GetActiveByUserIdAsync(Guid userId)
    {
        return await _dbSet
            .FirstOrDefaultAsync(p => p.UserId == userId && 
                                     p.Status == "active" && 
                                     !string.IsNullOrEmpty(p.StripeSubscriptionId));
    }

    public async Task<Payment?> GetByStripeSubscriptionIdAsync(string subscriptionId)
    {
        return await _dbSet
            .FirstOrDefaultAsync(p => p.StripeSubscriptionId == subscriptionId);
    }
}

