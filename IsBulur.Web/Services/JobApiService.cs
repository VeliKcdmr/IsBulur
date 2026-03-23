using IsBulur.Shared.Models;
using System.Net.Http.Json;

namespace IsBulur.Web.Services;

public class JobApiService
{
    private readonly HttpClient _http;
    private const string ApiBase = "http://localhost:5258";

    public JobApiService(HttpClient http)
    {
        _http = http;
        _http.BaseAddress = new Uri(ApiBase);
    }

    public async Task<SearchResponse?> SearchAsync(
        string keyword,
        string location = "",
        string workModel = "",
        string workType = "",
        int page = 1)
    {
        var url = $"/api/jobs/search?keyword={Uri.EscapeDataString(keyword)}" +
                  $"&location={Uri.EscapeDataString(location)}" +
                  $"&workModel={Uri.EscapeDataString(workModel)}" +
                  $"&workType={Uri.EscapeDataString(workType)}" +
                  $"&page={page}&pageSize=10";

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
}