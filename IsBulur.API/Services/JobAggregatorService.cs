using IsBulur.Shared.Models;

namespace IsBulur.API.Services;

public class JobAggregatorService
{
    private readonly IEnumerable<IJobScraper> _scrapers;
    private readonly ILogger<JobAggregatorService> _log;

    public JobAggregatorService(IEnumerable<IJobScraper> scrapers, ILogger<JobAggregatorService> log)
    {
        _scrapers = scrapers;
        _log = log;
    }

    public async Task<SearchResponse> SearchAsync(SearchRequest request)
    {
        // Hangi siteler aransın?
        var activeScraper = request.Sources?.Any() == true
            ? _scrapers.Where(s => request.Sources.Contains(s.SourceName))
            : _scrapers;

        // Hepsi paralel çalışsın
        var tasks = activeScraper.Select(s => s.ScrapeAsync(request));
        var results = await Task.WhenAll(tasks);

        // Sonuçları birleştir
        var allJobs = results.SelectMany(r => r).ToList();

        // WorkModel filtresi
        if (!string.IsNullOrWhiteSpace(request.WorkModel))
            allJobs = allJobs
                .Where(j => j.WorkModel.Contains(request.WorkModel, StringComparison.OrdinalIgnoreCase))
                .ToList();

        // WorkType filtresi
        if (!string.IsNullOrWhiteSpace(request.WorkType))
            allJobs = allJobs
                .Where(j => j.WorkType.Contains(request.WorkType, StringComparison.OrdinalIgnoreCase))
                .ToList();

        // Her siteden kaç ilan geldi
        var sourceCounts = allJobs
            .GroupBy(j => j.Source)
            .ToDictionary(g => g.Key, g => g.Count());

        _log.LogInformation("Toplam {Count} ilan bulundu. Kaynaklar: {Sources}",
            allJobs.Count,
            string.Join(", ", sourceCounts.Select(kv => $"{kv.Key}:{kv.Value}")));

        return new SearchResponse
        {
            Jobs = allJobs,
            TotalCount = allJobs.Count,
            Page = request.Page,
            PageSize = request.PageSize,
            SourceCounts = sourceCounts
        };
    }
}