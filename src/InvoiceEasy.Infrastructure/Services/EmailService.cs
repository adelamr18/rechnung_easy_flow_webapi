using System.Net;
using System.Net.Mail;
using System.Text;
using InvoiceEasy.Domain.Entities;
using InvoiceEasy.Domain.Interfaces.Services;
using Microsoft.Extensions.Options;

namespace InvoiceEasy.Infrastructure.Services;

public class EmailService : IEmailService
{
    private readonly SmtpOptions _options;

    public EmailService(IOptions<SmtpOptions> options)
    {
        _options = options.Value;
    }

   public async Task SendWelcomeEmailAsync(User user)
    {
        if (string.IsNullOrWhiteSpace(_options.Host) ||
            string.IsNullOrWhiteSpace(_options.Username) ||
            string.IsNullOrWhiteSpace(_options.Password) ||
            string.IsNullOrWhiteSpace(_options.FromEmail))
        {
            _logger.LogWarning(
                "EmailService: SMTP not configured, skipping welcome email. Host={Host} Username={Username} FromEmail={FromEmail}",
                _options.Host, _options.Username, _options.FromEmail);
            return;
        }

        _logger.LogInformation(
            "EmailService: Sending welcome email to {Email} via {Host}:{Port}",
            user.Email, _options.Host, _options.Port);

        var message = new MailMessage
        {
            From = new MailAddress(_options.FromEmail, _options.FromName ?? "InvoiceEasy – Beta Team"),
            Subject = "Welcome to InvoiceEasy — Thanks for joining the beta!",
            Body = BuildWelcomeBody(user),
            BodyEncoding = Encoding.UTF8,
            SubjectEncoding = Encoding.UTF8,
            IsBodyHtml = false
        };

        message.To.Add(new MailAddress(user.Email));

        using var client = new SmtpClient(_options.Host, _options.Port)
        {
            EnableSsl = _options.EnableTls,
            Credentials = new NetworkCredential(_options.Username, _options.Password),
            DeliveryMethod = SmtpDeliveryMethod.Network,
            Timeout = 5000 // 5 seconds
        };

        try
        {
            await client.SendMailAsync(message);
            _logger.LogInformation("EmailService: Welcome email sent successfully to {Email}", user.Email);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "EmailService: Failed to send welcome email to {Email} via {Host}:{Port}",
                user.Email, _options.Host, _options.Port);
            throw;
        }
    }

    private static string BuildWelcomeBody(User user)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Hi there,");
        sb.AppendLine();
        sb.AppendLine("Thank you for creating your account with InvoiceEasy! 🎉");
        sb.AppendLine("We’re excited to have you as an early beta tester and help shape the next generation of fast, simple invoicing for local businesses.");
        sb.AppendLine();
        sb.AppendLine("Here’s what you can do right away:");
        sb.AppendLine();
        sb.AppendLine("• Create clean, professional invoices in under 30 seconds");
        sb.AppendLine("• Upload receipts and have line-items extracted automatically with OCR");
        sb.AppendLine("• Download polished PDF invoices with your branding");
        sb.AppendLine("• Track expenses in a lightweight, simple dashboard");
        sb.AppendLine("• Switch between Starter and Pro during beta completely for free");
        sb.AppendLine();
        sb.AppendLine("Why this matters:");
        sb.AppendLine("InvoiceEasy is currently in a non-commercial beta phase. This means:");
        sb.AppendLine();
        sb.AppendLine("No payments, no subscriptions, no obligations");
        sb.AppendLine("All features are free while we build the best product for small businesses");
        sb.AppendLine("Your usage and feedback directly influence what we improve next");
        sb.AppendLine();
        sb.AppendLine("If you have ideas, feature requests, or notice anything confusing, reply anytime — we read every message.");
        sb.AppendLine();
        sb.AppendLine("Thanks again for joining us early and helping shape InvoiceEasy.");
        sb.AppendLine("We’re happy to have you onboard!");
        sb.AppendLine();
        sb.AppendLine("Best regards,");
        sb.AppendLine("InvoiceEasy – Beta Team");
        sb.Append("support@invoiceeasy.org");
        return sb.ToString();
    }
}

public class SmtpOptions
{
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    public bool EnableTls { get; set; } = true;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FromEmail { get; set; } = string.Empty;
    public string? FromName { get; set; }
}
