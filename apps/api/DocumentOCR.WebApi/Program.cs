using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using DocumentOCR.Infrastructure;
using DocumentOCR.WebApi.Authorization;
using DocumentOCR.WebApi.Middleware;
using Hangfire;
using Hangfire.Dashboard;
using Microsoft.AspNetCore.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// ── Services ──────────────────────────────────────────────────────────────────
// Serialize enums (DocumentStatus, DocumentType, FieldName, ...) as strings so the
// frontend's string-literal types (e.g. DocumentStatus = 'Uploaded' | 'Failed' | ...) match.
builder.Services.AddControllers()
    .AddJsonOptions(options =>
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "DocumentOCR API", Version = "v1" });
});

var corsAllowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];

builder.Services.AddCors(options =>
{
    options.AddPolicy("FrontendDev", policy =>
        policy.WithOrigins("http://localhost:5173")  // Vite dev server
              .AllowAnyHeader()
              .AllowAnyMethod());

    // Production policy: origins must come from config. If none are configured,
    // this policy allows no cross-origin requests (safe default) rather than
    // silently falling back to something permissive.
    options.AddPolicy("Frontend", policy =>
    {
        if (corsAllowedOrigins.Length > 0)
        {
            policy.WithOrigins(corsAllowedOrigins)
                  .AllowAnyHeader()
                  .WithMethods("GET", "POST", "PUT", "DELETE");
        }
    });
});

// Guards against unauthenticated callers flooding OCR-triggering endpoints
// (disk fill, Hangfire queue flood, Azure OCR cost blowout).
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy("OcrProcessing", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));

    // Same cost-guard rationale as OcrProcessing: unauthenticated callers could otherwise
    // flood Excel export generation (CPU cost, disk I/O). Kept as a separate policy so
    // export traffic doesn't consume the OCR-triggering endpoints' budget or vice versa.
    options.AddPolicy("Export", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
});

// Register all Infrastructure services (DB, OCR, Storage, Hangfire, etc.)
builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

// ── Middleware ────────────────────────────────────────────────────────────────
app.UseMiddleware<GlobalExceptionMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors(app.Environment.IsDevelopment() ? "FrontendDev" : "Frontend");
app.UseRateLimiter();
app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = [new LocalOnlyDashboardAuthorizationFilter()]
});
app.MapControllers();

app.Run();

// Expose Program for integration tests
public partial class Program { }

