using IsBulur.API.Data;
using IsBulur.API.Services;
using IsBulur.API.Services.Scrapers;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
// SQLite
builder.Services.AddDbContext<AppDbContext>(opt =>
    opt.UseSqlite("Data Source=isbulur.db"));

// Aggregator artık DbContext alıyor
builder.Services.AddScoped<JobAggregatorService>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// CORS — Blazor ve MAUI için
builder.Services.AddCors(opt =>
    opt.AddDefaultPolicy(p =>
        p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

// Scrapers
builder.Services.AddSingleton<IJobScraper, KariyerNetScraper>(sp =>
    new KariyerNetScraper(
        sp.GetRequiredService<IHttpClientFactory>().CreateClient(),
        sp.GetRequiredService<ILogger<KariyerNetScraper>>()));

builder.Services.AddSingleton<IJobScraper, YenibirisScraper>(sp =>
    new YenibirisScraper(
        sp.GetRequiredService<IHttpClientFactory>().CreateClient(),
        sp.GetRequiredService<ILogger<YenibirisScraper>>()));

builder.Services.AddSingleton<IJobScraper, ElemanNetScraper>(sp =>
    new ElemanNetScraper(
        sp.GetRequiredService<IHttpClientFactory>().CreateClient(),
        sp.GetRequiredService<ILogger<ElemanNetScraper>>()));

builder.Services.AddSingleton<IJobScraper, SecretCvScraper>(sp =>
    new SecretCvScraper(
        sp.GetRequiredService<IHttpClientFactory>().CreateClient(),
        sp.GetRequiredService<ILogger<SecretCvScraper>>()));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();
app.UseHttpsRedirection();
app.MapControllers();
app.Run();