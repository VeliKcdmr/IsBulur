using IsBulur.Shared.Models;

namespace IsBulur.API.Services;

public interface IJobScraper
{
    string SourceName { get; }
    Task<List<JobListing>> ScrapeAsync(SearchRequest request);
    Task<JobListing?> GetDetailAsync(string url);
}