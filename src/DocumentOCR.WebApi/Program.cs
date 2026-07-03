using DocumentOCR.Infrastructure;
using DocumentOCR.WebApi.Middleware;
using Hangfire;

var builder = WebApplication.CreateBuilder(args);

// ── Services ──────────────────────────────────────────────────────────────────
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "DocumentOCR API", Version = "v1" });
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("FrontendDev", policy =>
        policy.WithOrigins("http://localhost:5173")  // Vite dev server
              .AllowAnyHeader()
              .AllowAnyMethod());
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

app.UseCors("FrontendDev");
app.UseHangfireDashboard("/hangfire");
app.MapControllers();

app.Run();

// Expose Program for integration tests
public partial class Program { }

