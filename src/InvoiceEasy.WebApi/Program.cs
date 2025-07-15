var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo { Title = "InvoiceEasy", Version = "v1" });
});
builder.Services.AddCors(opt =>
{
    opt.AddPolicy("DevCors", p => p
        .WithOrigins("http://localhost:5173",
                     "http://localhost:8080")
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials());
});
builder.Services.AddAuthorization();
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

var app = builder.Build();
app.UseSwagger();
app.UseSwaggerUI();
app.UseCors("DevCors");
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.Run();
