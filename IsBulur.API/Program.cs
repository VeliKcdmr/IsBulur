using IsBulur.API.Services;
using IsBulur.API.Services.Scrapers;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// CORS — Blazor ve MAUI için
builder.Services.AddCors(opt =>
    opt.AddDefaultPolicy(p =>
        p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

// Scrapers
builder.Services.AddHttpClient<KariyerNetScraper>();
builder.Services.AddSingleton<IJobScraper, KariyerNetScraper>(sp =>
    new KariyerNetScraper(
        sp.GetRequiredService<IHttpClientFactory>().CreateClient(),
        sp.GetRequiredService<ILogger<KariyerNetScraper>>()));

// Aggregator
builder.Services.AddScoped<JobAggregatorService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();
app.UseHttpsRedirection();
app.MapControllers();
app.Run();