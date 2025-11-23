namespace InvoiceEasy.WebApi.DTOs;

public class BetaFeedbackRequest
{
    public string? Message { get; set; }
}

public class BetaUnlockResponse
{
    public string Plan { get; set; } = "pro-beta";
}
