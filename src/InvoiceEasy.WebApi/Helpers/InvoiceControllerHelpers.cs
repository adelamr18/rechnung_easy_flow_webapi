using System.Text.Json;
using InvoiceEasy.Domain.Enums;
using InvoiceEasy.WebApi.DTOs;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace InvoiceEasy.WebApi.Helpers;

public static class InvoiceControllerHelpers
{
    public static string ResolveBaseUrl(HttpRequest? request, IConfiguration configuration)
    {
        var configured = configuration["BASE_URL"];
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured.TrimEnd('/');
        }

        if (request?.Scheme != null && request.Host.HasValue)
        {
            return $"{request.Scheme}://{request.Host}".TrimEnd('/');
        }

        return "http://localhost:5000";
    }

    public static InvoicePdfTemplate? GetAutomaticTemplateForPlan(string? plan)
    {
        var normalized = (plan ?? "starter").ToLowerInvariant();
        return normalized switch
        {
            "elite" => InvoicePdfTemplate.Elite,
            "pro" => InvoicePdfTemplate.Advanced,
            "pro-beta" => InvoicePdfTemplate.Advanced,
            _ => InvoicePdfTemplate.Basic
        };
    }

    public static InvoicePdfTemplate ResolveTemplateForPlan(string? plan, string? requestedTemplate)
    {
        var normalizedPlan = (plan ?? "starter").ToLowerInvariant();
        var defaultTemplate = normalizedPlan switch
        {
            "elite" => InvoicePdfTemplate.Elite,
            "pro" => InvoicePdfTemplate.Advanced,
            "pro-beta" => InvoicePdfTemplate.Advanced,
            _ => InvoicePdfTemplate.Basic
        };

        if (string.IsNullOrWhiteSpace(requestedTemplate))
        {
            return defaultTemplate;
        }

        var normalizedRequest = requestedTemplate.Trim().ToLowerInvariant();

        return normalizedPlan switch
        {
            "elite" => normalizedRequest switch
            {
                "elite" => InvoicePdfTemplate.Elite,
                "advanced" => InvoicePdfTemplate.Advanced,
                "basic" => InvoicePdfTemplate.Basic,
                _ => defaultTemplate
            },
            "pro" => normalizedRequest switch
            {
                "advanced" => InvoicePdfTemplate.Advanced,
                "basic" => InvoicePdfTemplate.Basic,
                _ => defaultTemplate
            },
            "pro-beta" => normalizedRequest switch
            {
                "advanced" => InvoicePdfTemplate.Advanced,
                "basic" => InvoicePdfTemplate.Basic,
                _ => defaultTemplate
            },
            _ => InvoicePdfTemplate.Basic
        };
    }

    public static bool PlanAllowsUnlimitedInvoices(string? plan)
    {
        var normalized = (plan ?? "starter").ToLowerInvariant();
        return normalized switch
        {
            "elite" => true,
            "pro-beta" => true,
            "pro" => true,
            _ => false
        };
    }

    public static int GetInvoiceLimit(string? plan)
    {
        var normalized = (plan ?? "starter").ToLowerInvariant();
        return normalized switch
        {
            "starter" => 100,
            "free" => 100,
            _ => 100
        };
    }

    public static int GetSoftInvoiceLimit(string? plan)
    {
        var normalized = (plan ?? "starter").ToLowerInvariant();
        return normalized switch
        {
            "starter" => 5,
            "free" => 5,
            _ => 5
        };
    }

    public static string GetBetaWarning(string? plan)
    {
        return "beta.noticeStrong";
    }

    public static string? SerializeLineItems(List<InvoiceLineItemDto>? items)
    {
        if (items == null || items.Count == 0)
        {
            return null;
        }

        var cleaned = items
            .Where(i => !string.IsNullOrWhiteSpace(i.Description))
            .Select(i => new InvoiceLineItemDto
            {
                Description = i.Description.Trim(),
                Quantity = i.Quantity,
                UnitPrice = i.UnitPrice,
                TotalPrice = i.TotalPrice
            })
            .ToList();

        if (cleaned.Count == 0)
        {
            return null;
        }

        return JsonSerializer.Serialize(cleaned);
    }

    public static List<InvoiceLineItemDto>? DeserializeLineItems(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            var items = JsonSerializer.Deserialize<List<InvoiceLineItemDto>>(json);
            if (items == null || items.Count == 0)
            {
                return null;
            }

            return items
                .Where(i => !string.IsNullOrWhiteSpace(i.Description))
                .ToList();
        }
        catch
        {
            return null;
        }
    }
}
