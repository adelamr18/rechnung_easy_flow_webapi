using InvoiceEasy.Domain.Entities;
using InvoiceEasy.Domain.Interfaces;
using InvoiceEasy.Infrastructure.Data;

namespace InvoiceEasy.Infrastructure.Repositories;

public class FeedbackRepository : BaseRepository<Feedback>, IFeedbackRepository
{
    public FeedbackRepository(ApplicationDbContext context) : base(context)
    {
    }
}
