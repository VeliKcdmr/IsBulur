namespace IsBulur.Shared.Models;

public class JobListing
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Title { get; set; } = string.Empty;
    public string Company { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string Sector { get; set; } = string.Empty;
    public string WorkType { get; set; } = string.Empty;
    public string WorkModel { get; set; } = string.Empty;
    public string TimeAgo { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string CompanyLogoUrl { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Experience { get; set; } = string.Empty;
    public string EducationLevel { get; set; } = string.Empty;
    public string ClosingDate { get; set; } = string.Empty;
    public string LastPublishDate { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;      // kariyer.net, yenibiris.com vs
    public DateTime ScrapedAt { get; set; } = DateTime.UtcNow;
}

public record SearchRequest(
    string Keyword = "",
    string Location = "",
    string WorkModel = "",   // Uzaktan, Hibrit, İş Yerinde
    string WorkType = "",    // Tam zamanlı, Yarı zamanlı
    List<string>? Sources = null,  // Hangi sitelerden aransın
    int Page = 1,
    int PageSize = 20
);

public class SearchResponse
{
    public List<JobListing> Jobs { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
    public Dictionary<string, int> SourceCounts { get; set; } = new(); // Her siteden kaç ilan
}