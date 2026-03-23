using IsBulur.API.Data;
using IsBulur.API.Services;
using IsBulur.Shared.Models;
using Microsoft.AspNetCore.Mvc;

namespace IsBulur.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class JobsController : ControllerBase
{
    private readonly JobAggregatorService _aggregator;

    public JobsController(JobAggregatorService aggregator)
    {
        _aggregator = aggregator;
    }

    // GET /api/jobs/search?keyword=yazilim&location=istanbul
    [HttpGet("search")]
    public async Task<ActionResult<SearchResponse>> Search([FromQuery] SearchRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Keyword) && string.IsNullOrWhiteSpace(request.Location))
            return BadRequest("Keyword veya location giriniz.");

        var result = await _aggregator.SearchAsync(request);
        return Ok(result);
    }
    // GET /api/jobs/detail?url=https://...&source=kariyer.net
    [HttpGet("detail")]
    public async Task<ActionResult<JobListing>> Detail(
    [FromQuery] string url,
    [FromQuery] string source,
    [FromServices] IEnumerable<IJobScraper> scrapers,
    [FromServices] AppDbContext db)
    {
        if (string.IsNullOrWhiteSpace(url))
            return BadRequest("URL zorunlu.");

        // Önbellekte var mı?
        var cacheKey = $"detail|{url}";
        var cached = db.CachedSearches
    .FirstOrDefault(c => c.CacheKey == cacheKey && c.ExpiresAt > DateTime.UtcNow);

        if (cached != null)
        {
            var cachedJob = System.Text.Json.JsonSerializer.Deserialize<JobListing>(cached.ResultJson);
            return cachedJob is null ? NotFound() : Ok(cachedJob);
        }

        var scraper = scrapers.FirstOrDefault(s => s.SourceName == source);
        if (scraper == null)
            return NotFound($"'{source}' için scraper bulunamadı.");

        var job = await scraper.GetDetailAsync(url);
        if (job is null) return NotFound();

        // Önbelleğe kaydet — 6 saat
        await db.CachedSearches.AddAsync(new CachedSearch
        {
            CacheKey = cacheKey,
            ResultJson = System.Text.Json.JsonSerializer.Serialize(job),
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddHours(6)
        });
        await db.SaveChangesAsync();

        return Ok(job);
    }
    // GET /api/jobs/sources
    [HttpGet("sources")]
    public ActionResult<List<string>> GetSources([FromServices] IEnumerable<IJobScraper> scrapers)
    {
        return Ok(scrapers.Select(s => s.SourceName).ToList());
    }
}