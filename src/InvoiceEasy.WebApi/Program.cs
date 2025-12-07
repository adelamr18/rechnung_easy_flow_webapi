using System.Text;
using Azure;
using Azure.AI.DocumentIntelligence;
using InvoiceEasy.Application.Services;
using InvoiceEasy.Domain.Interfaces;
using InvoiceEasy.Domain.Interfaces.Repositories;
using InvoiceEasy.Domain.Interfaces.Services;
using InvoiceEasy.Infrastructure.Data;
using InvoiceEasy.Infrastructure.Data.Repositories;
using InvoiceEasy.Infrastructure.Repositories;
using InvoiceEasy.Infrastructure.Services;
using InvoiceEasy.WebApi.Middleware;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Npgsql;
using Stripe;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = null;
});

builder.Services.Configure<FormOptions>(opts =>
{
    opts.MultipartBodyLengthLimit = 500L * 1024 * 1024;
    opts.ValueLengthLimit = int.MaxValue;
    opts.MultipartHeadersLengthLimit = int.MaxValue;
});

builder.Configuration
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true)
    .AddEnvironmentVariables();

var jwtSecret = builder.Configuration["JWT_SECRET"];
if (string.IsNullOrWhiteSpace(jwtSecret))
    throw new InvalidOperationException("JWT_SECRET must be provided (env or appsettings).");

string NormalizeConnectionString(string? value)
{
    if (string.IsNullOrWhiteSpace(value))
        return value ?? string.Empty;

    if (value.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) ||
        value.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
    {
        var uri = new Uri(value);
        var userInfoParts = uri.UserInfo.Split(':', 2);

        var csBuilder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.Port,
            Username = userInfoParts.ElementAtOrDefault(0) ?? string.Empty,
            Password = userInfoParts.ElementAtOrDefault(1) ?? string.Empty,
            Database = uri.AbsolutePath.TrimStart('/')
        };

        var queryParams = QueryHelpers.ParseQuery(uri.Query);
        foreach (var kvp in queryParams)
        {
            csBuilder[kvp.Key] = kvp.Value.LastOrDefault();
        }

        if (!queryParams.Keys.Any(k => string.Equals(k, "sslmode", StringComparison.OrdinalIgnoreCase)))
        {
            csBuilder.SslMode = SslMode.Require;
        }

        return csBuilder.ConnectionString;
    }

    return value;
}

var rawConnectionString =
    builder.Configuration.GetConnectionString("Default")
    ?? builder.Configuration.GetConnectionString("DefaultConnection")
    ?? builder.Configuration["DATABASE_URL"];

var connectionString = NormalizeConnectionString(rawConnectionString);
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "Connection string not configured. Set ConnectionStrings:Default (or DefaultConnection) or DATABASE_URL.");
}

AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

var storageRoot = builder.Configuration["STORAGE_ROOT"]
                  ?? Path.Combine(Directory.GetCurrentDirectory(), "uploads");

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString));

builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IInvoiceRepository, InvoiceRepository>();
builder.Services.AddScoped<IExpenseRepository, ExpenseRepository>();
builder.Services.AddScoped<IPaymentRepository, PaymentRepository>();
builder.Services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
builder.Services.AddScoped<IReceiptRepository, ReceiptRepository>();
builder.Services.AddScoped<IFeedbackRepository, FeedbackRepository>();

builder.Services.Configure<SmtpOptions>(builder.Configuration.GetSection("Smtp"));
builder.Services.AddScoped<IEmailService, EmailService>();

builder.Services.AddScoped<IFileStorage>(_ => new LocalFileStorage(storageRoot));
builder.Services.AddScoped<IReceiptService, ReceiptService>();
builder.Services.AddScoped<IInvoiceOcrService, InvoiceOcrService>();
builder.Services.AddScoped<PdfService>(sp =>
{
    var fileStorage = sp.GetRequiredService<IFileStorage>();
    return new PdfService(fileStorage);
});
builder.Services.AddScoped<JwtService>();

var documentEndpoint = builder.Configuration["DocumentIntelligence:Endpoint"]
                      ?? builder.Configuration["DOCUMENT_INTELLIGENCE_ENDPOINT"];
var documentKey = builder.Configuration["DocumentIntelligence:ApiKey"]
                  ?? builder.Configuration["DOCUMENT_INTELLIGENCE_KEY"];

if (!string.IsNullOrWhiteSpace(documentEndpoint) &&
    !string.IsNullOrWhiteSpace(documentKey))
{
    builder.Services.AddSingleton(
        new DocumentIntelligenceClient(new Uri(documentEndpoint), new AzureKeyCredential(documentKey)));
}

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey =
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "InvoiceEasy API", Version = "v1" });
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme. Example: 'Bearer {token}'",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});

var defaultFrontendUrls =
    "https://invoiceeasy.org;" +
    "https://www.invoiceeasy.org;" +
    "http://localhost:5173;" +
    "http://localhost:8080";

var frontendUrlSetting = builder.Configuration["FRONTEND_URL"] ?? defaultFrontendUrls;

var frontendUrls = frontendUrlSetting
    .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

if (frontendUrls.Length == 0)
{
    frontendUrls = new[] { "http://localhost:5173" };
}

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins(frontendUrls)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

var stripeSecretKey = builder.Configuration["STRIPE_SECRET_KEY"];
if (string.IsNullOrWhiteSpace(stripeSecretKey))
{
    throw new InvalidOperationException(
        "STRIPE_SECRET_KEY not configured. Set it to your Stripe test or live secret key.");
}
StripeConfiguration.ApiKey = stripeSecretKey;

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    db.Database.Migrate();
}

app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});

app.UseSwagger();
app.UseSwaggerUI();

app.UseMiddleware<GlobalExceptionHandlingMiddleware>();

app.UseCors("AllowFrontend");
app.UseMiddleware<ApiKeyMiddleware>();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
