using IsBulur.Shared.Models;
using System.Net.Http.Json;

namespace IsBulur.Web.Services;

public class JobApiService
{
    private readonly HttpClient _http;

    public JobApiService(HttpClient http, IConfiguration config)
    {
        _http = http;
        _http.BaseAddress = new Uri(config["ApiBaseUrl"] ?? "http://localhost:5258");
    }

    public async Task<SearchResponse?> SearchAsync(
        string keyword,
        string location = "",
        string workModel = "",
        string workType = "",
        int page = 1,
        List<string>? sources = null)
    {
        var url = $"/api/jobs/search?keyword={Uri.EscapeDataString(keyword)}" +
                  $"&location={Uri.EscapeDataString(location)}" +
                  $"&workModel={Uri.EscapeDataString(workModel)}" +
                  $"&workType={Uri.EscapeDataString(workType)}" +
                  $"&page={page}&pageSize=10";

        if (sources?.Count > 0)
            url += string.Concat(sources.Select(s => $"&sources={Uri.EscapeDataString(s)}"));

        return await _http.GetFromJsonAsync<SearchResponse>(url);
    }

    public async Task<List<string>> GetSourcesAsync()
    {
        return await _http.GetFromJsonAsync<List<string>>("/api/jobs/sources")
               ?? new List<string>();
    }

    public async Task<JobListing?> GetDetailAsync(string url, string source)
    {
        var apiUrl = $"/api/jobs/detail?url={Uri.EscapeDataString(url)}&source={Uri.EscapeDataString(source)}";
        return await _http.GetFromJsonAsync<JobListing>(apiUrl);
    }

    public async Task<int> ClearCacheAsync()
    {
        var resp = await _http.DeleteAsync("/api/jobs/cache");
        if (!resp.IsSuccessStatusCode) return 0;
        var result = await resp.Content.ReadFromJsonAsync<ClearCacheResult>();
        return result?.Deleted ?? 0;
    }

    private record ClearCacheResult(int Deleted, string Message);
}
