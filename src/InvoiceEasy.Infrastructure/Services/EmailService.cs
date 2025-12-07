using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Mail;
using System.Text;
using System.Text.Json;
using InvoiceEasy.Domain.Entities;
using InvoiceEasy.Domain.Interfaces.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InvoiceEasy.Infrastructure.Services;

public class EmailService : IEmailService
{
    private static readonly HttpClient HttpClient = new();

    private readonly ILogger<EmailService> _logger;
    private readonly SmtpOptions _options;

    public EmailService(IOptions<SmtpOptions> options, ILogger<EmailService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    // Keep your public API exactly as-is, but avoid extra Task.Run wrapping
    public Task SendWelcomeEmailAsync(User user)
    {
        // The internal method is already async and handles its own awaits.
        // If you want fire-and-forget, do it at the call site (e.g. in the controller).
        return SendWelcomeEmailInternal(user);
    }

    private async Task SendWelcomeEmailInternal(User user)
    {
        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            await SendWithResendAsync(user);
            return;
        }

        await SendWithSmtpAsync(user);
    }

    private async Task SendWithResendAsync(User user)
    {
        if (string.IsNullOrWhiteSpace(_options.FromEmail))
        {
            _logger.LogWarning(
                "EmailService: Resend ApiKey configured but FromEmail is empty. Skipping email.");
            return;
        }

        var fromName = _options.FromName ?? "InvoiceEasy – Beta Team";
        var fromAddress = $"{fromName} <{_options.FromEmail}>";

        _logger.LogInformation(
            "EmailService: Sending welcome email via Resend to {Email} from {From}",
            user.Email, fromAddress);

        var bodyText = BuildWelcomeBody(user);

        var payload = new
        {
            from = fromAddress,
            to = new[] { user.Email },
            subject = "Welcome to InvoiceEasy — Thanks for joining the beta!",
            text = bodyText
        };

        var json = JsonSerializer.Serialize(payload);
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.resend.com/emails")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);

        try
        {
            using var response = await HttpClient.SendAsync(request);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation(
                    "EmailService: Welcome email sent successfully via Resend to {Email}",
                    user.Email);
            }
            else
            {
                var errorText = await response.Content.ReadAsStringAsync();
                _logger.LogError(
                    "EmailService: Resend API returned {StatusCode} for {Email}. Body={Body}",
                    (int)response.StatusCode, user.Email, errorText);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "EmailService: Failed to send welcome email via Resend to {Email}",
                user.Email);
        }
    }

    private async Task SendWithSmtpAsync(User user)
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
            "EmailService: Sending welcome email via SMTP to {Email} using {Host}:{Port}",
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
            Timeout = 15000 // 15 seconds
        };

        try
        {
            client.Send(message);

            _logger.LogInformation(
                "EmailService: Welcome email sent successfully via SMTP to {Email}",
                user.Email);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "EmailService: Failed to send welcome email via SMTP to {Email} using {Host}:{Port}",
                user.Email, _options.Host, _options.Port);
        }

        await Task.CompletedTask;
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
    public string ApiKey { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    public bool EnableTls { get; set; } = true;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FromEmail { get; set; } = string.Empty;
    public string? FromName { get; set; }
}
