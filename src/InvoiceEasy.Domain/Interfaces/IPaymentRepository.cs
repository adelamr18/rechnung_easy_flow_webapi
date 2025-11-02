using InvoiceEasy.Domain.Entities;

namespace InvoiceEasy.Domain.Interfaces;

public interface IPaymentRepository : IRepository<Payment>
{
    Task<Payment?> GetActiveByUserIdAsync(Guid userId);
    Task<Payment?> GetByStripeSubscriptionIdAsync(string subscriptionId);
}

