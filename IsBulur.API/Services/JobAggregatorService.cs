using System.Text.Json;
using IsBulur.API.Data;
using IsBulur.Shared.Models;
using Microsoft.EntityFrameworkCore;

namespace IsBulur.API.Services;

public class JobAggregatorService
{
    private readonly IEnumerable<IJobScraper> _scrapers;
    private readonly ILogger<JobAggregatorService> _log;
    private readonly AppDbContext _db;
    private readonly TimeSpan _cacheDuration = TimeSpan.FromHours(1);

    public JobAggregatorService(
        IEnumerable<IJobScraper> scrapers,
        ILogger<JobAggregatorService> log,
        AppDbContext db)
    {
        _scrapers = scrapers;
        _log = log;
        _db = db;
    }

    public async Task<SearchResponse> SearchAsync(SearchRequest request)
    {
        // Önbellek anahtarı — sayfa dahil
        var cacheKey = $"{request.Keyword?.ToLower()}|{request.Location?.ToLower()}|{request.WorkModel}|{request.WorkType}|{request.Page}";

        // Önbellekte var mı?
        var cached = await _db.CachedSearches
            .FirstOrDefaultAsync(c => c.CacheKey == cacheKey && c.ExpiresAt > DateTime.UtcNow);

        if (cached != null)
        {
            _log.LogInformation("Önbellekten döndürüldü: {Key}", cacheKey);
            return JsonSerializer.Deserialize<SearchResponse>(cached.ResultJson)!;
        }

        // Tüm sayfalar için önbellekte ham veri var mı?
        var allJobsCacheKey = $"{request.Keyword?.ToLower()}|{request.Location?.ToLower()}|{request.WorkModel}|{request.WorkType}|all";
        var allJobsCached = await _db.CachedSearches
            .FirstOrDefaultAsync(c => c.CacheKey == allJobsCacheKey && c.ExpiresAt > DateTime.UtcNow);

        List<JobListing> allJobs;
        Dictionary<string, int> sourceCounts;

        if (allJobsCached != null)
        {
            // Ham veri önbellekten
            allJobs = JsonSerializer.Deserialize<List<JobListing>>(allJobsCached.ResultJson)!;
            sourceCounts = allJobs.GroupBy(j => j.Source).ToDictionary(g => g.Key, g => g.Count());
        }
        else
        {
            // Scrape et
            var activeScraper = request.Sources?.Any() == true
                ? _scrapers.Where(s => request.Sources.Contains(s.SourceName))
                : _scrapers;

            var tasks = activeScraper.Select(s => s.ScrapeAsync(request with { Page = 1, PageSize = 50 }));
            var results = await Task.WhenAll(tasks);
            allJobs = results.SelectMany(r => r).ToList();

            // Filtreler
            if (!string.IsNullOrWhiteSpace(request.WorkModel))
                allJobs = allJobs.Where(j => j.WorkModel.Contains(request.WorkModel, StringComparison.OrdinalIgnoreCase)).ToList();

            if (!string.IsNullOrWhiteSpace(request.WorkType))
                allJobs = allJobs.Where(j => j.WorkType.Contains(request.WorkType, StringComparison.OrdinalIgnoreCase)).ToList();

            sourceCounts = allJobs.GroupBy(j => j.Source).ToDictionary(g => g.Key, g => g.Count());

            _log.LogInformation("Toplam {Count} ilan bulundu. Kaynaklar: {Sources}",
                allJobs.Count,
                string.Join(", ", sourceCounts.Select(kv => $"{kv.Key}:{kv.Value}")));

            // Ham veriyi önbelleğe kaydet
            await _db.CachedSearches.AddAsync(new CachedSearch
            {
                CacheKey = allJobsCacheKey,
                ResultJson = JsonSerializer.Serialize(allJobs),
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.Add(_cacheDuration)
            });
            await _db.SaveChangesAsync();
        }

        // Sayfalama uygula
        var pageSize = request.PageSize > 0 ? request.PageSize : 10;
        var pagedJobs = allJobs
            .Skip((request.Page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        var response = new SearchResponse
        {
            Jobs = pagedJobs,
            TotalCount = allJobs.Count,
            Page = request.Page,
            PageSize = pageSize,
            SourceCounts = sourceCounts
        };

        // Sayfalı sonucu önbelleğe kaydet
        var existing = await _db.CachedSearches.FirstOrDefaultAsync(c => c.CacheKey == cacheKey);
        if (existing != null)
        {
            existing.ResultJson = JsonSerializer.Serialize(response);
            existing.CreatedAt = DateTime.UtcNow;
            existing.ExpiresAt = DateTime.UtcNow.Add(_cacheDuration);
        }
        else
        {
            await _db.CachedSearches.AddAsync(new CachedSearch
            {
                CacheKey = cacheKey,
                ResultJson = JsonSerializer.Serialize(response),
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.Add(_cacheDuration)
            });
        }

        await _db.SaveChangesAsync();

        return response;
    }
}