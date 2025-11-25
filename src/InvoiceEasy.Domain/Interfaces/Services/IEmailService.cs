using InvoiceEasy.Domain.Entities;

namespace InvoiceEasy.Domain.Interfaces.Services;

public interface IEmailService
{
    Task SendWelcomeEmailAsync(User user);
}
