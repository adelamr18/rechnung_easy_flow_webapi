using InvoiceEasy.Domain.Interfaces;
using InvoiceEasy.WebApi.Extensions;
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

        var frontendUrl = _configuration["FRONTEND_URL"] ?? "http://localhost:5173";

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
                            Description = "Unlimited invoices and expenses"
                        },
                        UnitAmount = 700, // €7.00
                        Recurring = new SessionLineItemPriceDataRecurringOptions
                        {
                            Interval = "month"
                        }
                    },
                    Quantity = 1
                }
            },
            Mode = "subscription",
            SuccessUrl = $"{frontendUrl}/settings?success=true",
            CancelUrl = $"{frontendUrl}/settings?canceled=true",
            CustomerEmail = user.Email,
            Metadata = new Dictionary<string, string>
            {
                { "userId", userId.ToString() }
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
                    user.Plan = "pro";
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
                        user.Plan = "pro";
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
            ReturnUrl = _configuration["FRONTEND_URL"] ?? "http://localhost:5173/settings"
        };

        var service = new Stripe.BillingPortal.SessionService();
        var session = await service.CreateAsync(options);

        return Ok(new { url = session.Url });
    }
}

