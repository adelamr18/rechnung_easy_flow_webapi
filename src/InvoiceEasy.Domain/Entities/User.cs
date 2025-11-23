namespace InvoiceEasy.Domain.Entities;

public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string? CompanyName { get; set; }
    public string Locale { get; set; } = "de";
    public string Plan { get; set; } = "starter";
    public bool ClickedProButton { get; set; }
    public DateTime? ProBetaEnabledAt { get; set; }
    public int ProBetaInvoiceCount { get; set; }
    public bool FeedbackSubmitted { get; set; }
    public bool StarterLimitReached { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<Invoice> Invoices { get; set; } = new();
    public List<Expense> Expenses { get; set; } = new();
    public List<Payment> Payments { get; set; } = new();
    public List<RefreshToken> RefreshTokens { get; set; } = new();
    public List<Feedback> FeedbackEntries { get; set; } = new();
}
