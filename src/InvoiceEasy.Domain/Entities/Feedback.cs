namespace InvoiceEasy.Domain.Entities;

public class Feedback
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public string Message { get; set; } = string.Empty;
    public string Source { get; set; } = "general";
    public int? Rating { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
