namespace InvoiceEasy.Domain.Models;

public class Receipt
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string? MerchantName { get; set; }
    public decimal? TotalAmount { get; set; }
    public DateTime? TransactionDate { get; set; }
    public DateTime UploadDate { get; set; } = DateTime.UtcNow;
    public Dictionary<string, string>? ExtractedData { get; set; }
}
