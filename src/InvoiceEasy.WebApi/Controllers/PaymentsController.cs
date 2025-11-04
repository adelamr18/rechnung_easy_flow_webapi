using InvoiceEasy.Domain.Interfaces;
using InvoiceEasy.WebApi.Extensions;
using InvoiceEasy.WebApi.DTOs.Payments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Stripe;
using Stripe.Checkout;

namespace InvoiceEasy.WebApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PaymentsController : ControllerBase
{
    private readonly IUserRepository _userRepository;
    private readonly IPaymentRepository _paymentRepository;
    private readonly IConfiguration _configuration;

    public PaymentsController(
        IUserRepository userRepository,
        IPaymentRepository paymentRepository,
        IConfiguration configuration)
    {
        _userRepository = userRepository;
        _paymentRepository = paymentRepository;
        _configuration = configuration;
    }

    [HttpPost("checkout")]
    [Authorize]
    public async Task<IActionResult> CreateCheckout()
    {
        var userId = User.GetUserId();
        var user = await _userRepository.GetByIdAsync(userId);

        if (user == null)
            return NotFound();

        var baseUrl = GetFrontendBaseUrl();

        var options = new SessionCreateOptions
        {
            PaymentMethodTypes = new List<string> { "card" },
            LineItems = new List<SessionLineItemOptions>
            {
                new SessionLineItemOptions
                {
                    PriceData = new SessionLineItemPriceDataOptions
                    {
                        Currency = "eur",
                        ProductData = new SessionLineItemPriceDataProductDataOptions
                        {
                            Name = "InvoiceEasy Pro",
                            Description = "Unlimited invoices, 15 expenses/month"
                        },
                        Recurring = new SessionLineItemPriceDataRecurringOptions
                        {
                            Interval = "month"
                        }
                    },
                    Quantity = 1
                }
            },
            Mode = "subscription",
            SuccessUrl = $"{baseUrl}/settings?success=true&session_id={{CHECKOUT_SESSION_ID}}",
            CancelUrl = $"{baseUrl}/settings?canceled=true",
            CustomerEmail = user.Email,
            Metadata = new Dictionary<string, string>
            {
                { "userId", userId.ToString() },
                { "plan", "pro" }
            }
        };

        var service = new SessionService();
        var session = await service.CreateAsync(options);

        return Ok(new { url = session.Url, sessionId = session.Id });
    }
    [HttpPost("checkout/elite")]
    [Authorize]
    public async Task<IActionResult> CreateEliteCheckout()
    {
        var userId = User.GetUserId();
        var user = await _userRepository.GetByIdAsync(userId);

        if (user == null)
            return NotFound();

        var baseUrl = GetFrontendBaseUrl();

        var options = new SessionCreateOptions
        {
            PaymentMethodTypes = new List<string> { "card" },
            LineItems = new List<SessionLineItemOptions>
            {
                new SessionLineItemOptions
                {
                    PriceData = new SessionLineItemPriceDataOptions
                    {
                        Currency = "eur",
                        ProductData = new SessionLineItemPriceDataProductDataOptions
                        {
                            Name = "InvoiceEasy Elite",
                            Description = "Unlimited invoices & expenses, advanced automation"
                        },
                        Recurring = new SessionLineItemPriceDataRecurringOptions
                        {
                            Interval = "month"
                        }
                    },
                    Quantity = 1
                }
            },
            Mode = "subscription",
            SuccessUrl = $"{baseUrl}/settings?success=true&session_id={{CHECKOUT_SESSION_ID}}",
            CancelUrl = $"{baseUrl}/settings?canceled=true",
            CustomerEmail = user.Email,
            Metadata = new Dictionary<string, string>
            {
                { "userId", userId.ToString() },
                { "plan", "elite" }
            }
        };

        var service = new SessionService();
        var session = await service.CreateAsync(options);

        return Ok(new { url = session.Url, sessionId = session.Id });
    }

    [HttpPost("webhook")]
    [AllowAnonymous]
    public async Task<IActionResult> Webhook()
    {
        var json = await new StreamReader(Request.Body).ReadToEndAsync();
        var stripeSignature = Request.Headers["Stripe-Signature"].ToString();

        var webhookSecret = _configuration["STRIPE_WEBHOOK_SECRET"];
        if (string.IsNullOrEmpty(webhookSecret))
            return BadRequest();

        Event stripeEvent;
        try
        {
            stripeEvent = EventUtility.ConstructEvent(json, stripeSignature, webhookSecret);
        }
        catch (StripeException)
        {
            return BadRequest();
        }

        if (stripeEvent.Type == "checkout.session.completed")
        {
            var session = stripeEvent.Data.Object as Session;
            if (session?.Metadata?.ContainsKey("userId") == true)
            {
                var userId = Guid.Parse(session.Metadata["userId"]);
                var user = await _userRepository.GetByIdAsync(userId);
                if (user != null)
                {
                    user.Plan = session.Metadata.GetValueOrDefault("plan") ?? "pro";
                    await _userRepository.UpdateAsync(user);
                }
            }
        }
        else if (stripeEvent.Type == "invoice.paid")
        {
            var invoice = stripeEvent.Data.Object as Invoice;
            if (invoice?.Subscription != null && !string.IsNullOrEmpty(invoice.Subscription.Id))
            {
                var subscription = await new SubscriptionService().GetAsync(invoice.Subscription.Id);
                var userIdStr = subscription.Metadata.GetValueOrDefault("userId");
                if (!string.IsNullOrEmpty(userIdStr) && Guid.TryParse(userIdStr, out var userId))
                {
                    var user = await _userRepository.GetByIdAsync(userId);
                    if (user != null)
                    {
                        user.Plan = subscription.Metadata.GetValueOrDefault("plan") ?? "pro";
                        await _userRepository.UpdateAsync(user);

                                var payment = await _paymentRepository.GetByStripeSubscriptionIdAsync(subscription.Id);
                        var currentPeriodEnd = subscription.CurrentPeriodEnd != default(DateTime) 
                            ? subscription.CurrentPeriodEnd 
                            : DateTime.UtcNow.AddMonths(1);

                        if (payment != null)
                        {
                            payment.CurrentPeriodEnd = currentPeriodEnd;
                            payment.Status = "active";
                            await _paymentRepository.UpdateAsync(payment);
                        }
                        else
                        {
                            await _paymentRepository.AddAsync(new Domain.Entities.Payment
                            {
                                UserId = userId,
                                Provider = "stripe",
                                Status = "active",
                                StripeSubscriptionId = subscription.Id,
                                CurrentPeriodEnd = currentPeriodEnd
                            });
                        }
                    }
                }
            }
        }

        return Ok();
    }

    [HttpGet("portal")]
    [Authorize]
    public async Task<IActionResult> GetBillingPortal()
    {
        var userId = User.GetUserId();
        var payment = await _paymentRepository.GetActiveByUserIdAsync(userId);

        if (payment == null || string.IsNullOrEmpty(payment.StripeSubscriptionId))
            return BadRequest(new { error = "No active subscription found" });

        var subscription = await new SubscriptionService().GetAsync(payment.StripeSubscriptionId);
        var options = new Stripe.BillingPortal.SessionCreateOptions
        {
            Customer = subscription.CustomerId,
            ReturnUrl = GetFrontendBaseUrl() + "/settings"
        };

        var service = new Stripe.BillingPortal.SessionService();
        var session = await service.CreateAsync(options);

        return Ok(new { url = session.Url });
    }

    [HttpPost("confirm")]
    [Authorize]
    public async Task<IActionResult> ConfirmSession([FromBody] ConfirmSessionRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.SessionId))
        {
            return BadRequest(new { error = "sessionId is required" });
        }

        var session = await new SessionService().GetAsync(request.SessionId);
        if (session == null)
        {
            return NotFound(new { error = "Session not found" });
        }

        if (!session.Metadata.TryGetValue("userId", out var userIdStr) ||
            !Guid.TryParse(userIdStr, out var sessionUserId))
        {
            return BadRequest(new { error = "Session missing user metadata" });
        }

        var currentUserId = User.GetUserId();
        if (currentUserId != sessionUserId)
        {
            return Forbid();
        }

        if (!session.Metadata.TryGetValue("plan", out var plan) || string.IsNullOrWhiteSpace(plan))
        {
            return Ok(new { plan = plan });
        }

        var user = await _userRepository.GetByIdAsync(currentUserId);
        if (user == null)
        {
            return NotFound(new { error = "User not found" });
        }

        var normalizedPlan = plan.ToLowerInvariant();

        if (!string.Equals(user.Plan, normalizedPlan, StringComparison.OrdinalIgnoreCase))
        {
            user.Plan = normalizedPlan;
            await _userRepository.UpdateAsync(user);
        }

        return Ok(new { plan = user.Plan });
    }

    private string GetFrontendBaseUrl()
    {
        var allowed = (_configuration["FRONTEND_URL"] ?? string.Empty)
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        var requestOrigin = Request.Headers["Origin"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(requestOrigin) &&
            (allowed.Count == 0 || allowed.Contains(requestOrigin, StringComparer.OrdinalIgnoreCase)))
        {
            return requestOrigin.TrimEnd('/');
        }

        if (allowed.Count > 0)
        {
            return allowed[0].TrimEnd('/');
        }

        if (Request?.Scheme != null && Request.Host.HasValue)
        {
            return $"{Request.Scheme}://{Request.Host}".TrimEnd('/');
        }

        return "http://localhost:8080";
    }
}
