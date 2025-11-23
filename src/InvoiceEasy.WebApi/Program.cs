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
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Stripe;

var builder = WebApplication.CreateBuilder(args);

var jwtSecret = builder.Configuration["JWT_SECRET"] ?? throw new InvalidOperationException("JWT_SECRET not configured");
var connectionString = builder.Configuration.GetConnectionString("Default") 
    ?? builder.Configuration.GetConnectionString("DefaultConnection") 
    ?? builder.Configuration["DATABASE_URL"];
if (string.IsNullOrEmpty(connectionString))
    throw new InvalidOperationException("Connection string 'Default' or 'DefaultConnection' not configured in appsettings.json");

var storageRoot = builder.Configuration["STORAGE_ROOT"] ?? Path.Combine(Directory.GetCurrentDirectory(), "uploads");

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString));

builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IInvoiceRepository, InvoiceRepository>();
builder.Services.AddScoped<IExpenseRepository, ExpenseRepository>();
builder.Services.AddScoped<IPaymentRepository, PaymentRepository>();
builder.Services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
builder.Services.AddScoped<IReceiptRepository, ReceiptRepository>();
builder.Services.AddScoped<IFeedbackRepository, FeedbackRepository>();

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
if (!string.IsNullOrWhiteSpace(documentEndpoint) && !string.IsNullOrWhiteSpace(documentKey))
{
    builder.Services.AddSingleton(new DocumentIntelligenceClient(new Uri(documentEndpoint), new AzureKeyCredential(documentKey)));
}

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "InvoiceEasy API", Version = "v1" });
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme",
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

var frontendUrlSetting = builder.Configuration["FRONTEND_URL"] ?? "http://localhost:5173;http://localhost:8080";
var frontendUrls = frontendUrlSetting.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
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
    throw new InvalidOperationException("STRIPE_SECRET_KEY not configured. Set it to your Stripe test or live secret key.");
}
StripeConfiguration.ApiKey = stripeSecretKey;

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    db.Database.Migrate();
}

app.UseSwagger();
app.UseSwaggerUI();
app.UseMiddleware<GlobalExceptionHandlingMiddleware>();
app.UseCors("AllowFrontend");
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
