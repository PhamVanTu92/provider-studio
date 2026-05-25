using Microsoft.EntityFrameworkCore;
using ProviderStudio.Api;
using ProviderStudio.Data;
using ProviderStudio.Push;
using ProviderStudio.Runtime;

var builder = WebApplication.CreateBuilder(args);

// ── Logging ───────────────────────────────────────────────────────────────────
builder.Logging.ClearProviders();
builder.Logging.AddConsole(o => o.FormatterName = "simple");
builder.Logging.SetMinimumLevel(LogLevel.Information);

// ── SQLite config DB ──────────────────────────────────────────────────────────
var dbPath = builder.Configuration["Studio:DbPath"] ?? "studio.db";
builder.Services.AddDbContext<StudioDbContext>(opt =>
    opt.UseSqlite($"Data Source={dbPath}"));

// ── HTTP clients ──────────────────────────────────────────────────────────────
builder.Services.AddHttpClient("provider-token", c => c.Timeout = TimeSpan.FromSeconds(30));
builder.Services.AddHttpClient("ingestion",      c => c.Timeout = TimeSpan.FromSeconds(10));
builder.Services.AddHttpClient("api-source",     c => c.Timeout = TimeSpan.FromSeconds(30));

// ── Runtime + Push engines ────────────────────────────────────────────────────
builder.Services.AddSingleton<RuntimeManager>();
builder.Services.AddSingleton<PushEngine>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<RuntimeManager>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<PushEngine>());

// ── Controllers + CORS ────────────────────────────────────────────────────────
builder.Services.AddControllers();
builder.Services.AddCors(opt =>
    opt.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

// ── Build ─────────────────────────────────────────────────────────────────────
var app = builder.Build();

// ── DB init (migrate + WAL) ───────────────────────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<StudioDbContext>();
    await db.InitializeAsync();
    app.Logger.LogInformation("Studio DB initialized at {Path}", dbPath);
}

// ── Middleware ────────────────────────────────────────────────────────────────
app.UseCors();
app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "provider-studio" }));

// Serve React SPA static files in production
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapFallbackToFile("index.html");

await app.RunAsync();
