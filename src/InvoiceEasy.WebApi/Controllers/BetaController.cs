using InvoiceEasy.Domain.Entities;
using InvoiceEasy.Domain.Interfaces;
using InvoiceEasy.WebApi.DTOs;
using InvoiceEasy.WebApi.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InvoiceEasy.WebApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class BetaController : ControllerBase
{
    private readonly IUserRepository _userRepository;
    private readonly IFeedbackRepository _feedbackRepository;

    public BetaController(IUserRepository userRepository, IFeedbackRepository feedbackRepository)
    {
        _userRepository = userRepository;
        _feedbackRepository = feedbackRepository;
    }

    [HttpPost("feedback")]
    public async Task<IActionResult> SubmitFeedback([FromBody] BetaFeedbackRequest request)
    {
        var userId = User.GetUserId();
        var user = await _userRepository.GetByIdAsync(userId);
        if (user == null)
        {
            return NotFound();
        }

        var message = request.Message?.Trim();
        var rating = request.Rating;
        if (string.IsNullOrWhiteSpace(message) && rating == null)
        {
            return BadRequest(new { error = "Feedback message or rating is required." });
        }

        var feedback = new Feedback
        {
            UserId = userId,
            Message = message ?? string.Empty,
            Source = string.IsNullOrWhiteSpace(request.Source) ? "general" : request.Source!,
            Rating = rating
        };

        await _feedbackRepository.AddAsync(feedback);
        if (!user.FeedbackSubmitted)
        {
            user.FeedbackSubmitted = true;
            await _userRepository.UpdateAsync(user);
        }

        return Ok(new { saved = true });
    }

    [HttpPost("unlock")]
    public async Task<IActionResult> UnlockProBeta()
    {
        var userId = User.GetUserId();
        var user = await _userRepository.GetByIdAsync(userId);
        if (user == null)
        {
            return NotFound();
        }

        var normalizedPlan = (user.Plan ?? string.Empty).ToLowerInvariant();
        if (normalizedPlan != "pro-beta")
        {
            user.Plan = "pro-beta";
            user.ClickedProButton = true;
            user.ProBetaEnabledAt ??= DateTime.UtcNow;
            await _userRepository.UpdateAsync(user);
        }

        return Ok(new BetaUnlockResponse { Plan = user.Plan });
    }
}
