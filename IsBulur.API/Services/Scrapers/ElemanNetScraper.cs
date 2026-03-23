using HtmlAgilityPack;
using IsBulur.Shared.Models;

namespace IsBulur.API.Services.Scrapers;

public class ElemanNetScraper : IJobScraper
{
    private readonly HttpClient _http;
    private readonly ILogger<ElemanNetScraper> _log;
    private const string Base = "https://www.eleman.net";

    public string SourceName => "eleman.net";

    public ElemanNetScraper(HttpClient http, ILogger<ElemanNetScraper> log)
    {
        _http = http;
        _log = log;

        _http.DefaultRequestHeaders.Clear();
        _http.DefaultRequestHeaders.Add("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) " +
            "AppleWebKit/537.36 Chrome/124.0.0.0 Safari/537.36");
        _http.DefaultRequestHeaders.Add("Accept-Language", "tr-TR,tr;q=0.9");
        _http.DefaultRequestHeaders.Add("Accept",
            "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
    }

    public async Task<List<JobListing>> ScrapeAsync(SearchRequest request)
    {
        try
        {
            // URL formatı: /is-ilanlari/keyword/sehir
            var kw = request.Keyword.Trim().ToLower()
                .Replace("ı", "i").Replace("ğ", "g")
                .Replace("ü", "u").Replace("ş", "s")
                .Replace("ö", "o").Replace("ç", "c")
                .Replace(" ", "-");

            var loc = string.IsNullOrWhiteSpace(request.Location)
                ? ""
                : "/" + request.Location.Trim().ToLower()
                    .Replace("ı", "i").Replace("ğ", "g")
                    .Replace("ü", "u").Replace("ş", "s")
                    .Replace("ö", "o").Replace("ç", "c")
                    .Replace(" ", "-");

            var page = request.Page > 1 ? $"?page={request.Page}" : "";

            var url = string.IsNullOrWhiteSpace(request.Keyword)
                ? $"{Base}/is-ilanlari{loc}{page}"
                : $"{Base}/is-ilanlari/{kw}{loc}{page}";

            _log.LogInformation("[eleman.net] Scraping: {Url}", url);

            await Task.Delay(Random.Shared.Next(500, 1000));
            var html = await _http.GetStringAsync(url);

            return ParseJobs(html);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "[eleman.net] Hata");
            return new List<JobListing>();
        }
    }

    private List<JobListing> ParseJobs(string html)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var cards = doc.DocumentNode.SelectNodes("//div[contains(@class,'c-box__body') and contains(@class,'ilan_listeleme_bol')]");
        if (cards == null) return new List<JobListing>();

        var jobs = new List<JobListing>();

        foreach (var card in cards)
        {
            try
            {
                // Link ve başlık
                var linkNode = card.SelectSingleNode(".//a");
                var url = linkNode?.GetAttributeValue("href", "") ?? "";
                var title = card.SelectSingleNode(".//h3")?.InnerText.Trim() ?? "";

                // Telefon ikonunu başlıktan temizle
                title = System.Text.RegularExpressions.Regex
                    .Replace(title, @"\s+", " ").Trim();

                if (string.IsNullOrEmpty(title)) continue;

                // Şirket ve konum — subtitle içinden parse
                var subtitle = card.SelectSingleNode(".//*[contains(@class,'c-showcase-box__subtitle')]")
                    ?.InnerText.Trim() ?? "";

                var parts = subtitle.Split('-');
                var company = parts.Length > 0 ? parts[0].Trim() : "";
                var city = parts.Length > 1 ? parts[1].Trim() : "";

                // Açıklama
                var description = card.SelectSingleNode(".//*[contains(@class,'c-showcase-box__text')]")
                    ?.InnerText.Trim() ?? "";

                jobs.Add(new JobListing
                {
                    Title = title,
                    Company = company,
                    City = city,
                    Description = description,
                    Url = url.StartsWith("http") ? url : Base + url,
                    Source = SourceName,
                    ScrapedAt = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _log.LogWarning("[eleman.net] Kart parse hatası: {Msg}", ex.Message);
            }
        }

        return jobs;
    }
    public async Task<JobListing?> GetDetailAsync(string url)
    {
        try
        {
            await Task.Delay(100);
            var html = await _http.GetStringAsync(url);
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var job = new JobListing { Url = url, Source = SourceName };

            job.Title = doc.DocumentNode.SelectSingleNode("//h1")?.InnerText.Trim() ?? "";
            job.Company = doc.DocumentNode.SelectSingleNode("//*[contains(@class,'company-name')] | //*[contains(@class,'employer')]")?.InnerText.Trim() ?? "";

            // Tam açıklama
            var descNode = doc.DocumentNode.SelectSingleNode("//*[contains(@class,'job-description')] | //*[contains(@class,'ilan-detay')]  | //div[contains(@class,'description')]");
            job.Description = descNode?.InnerText.Trim() ?? "";

            // Ek bilgiler — tablo formatında
            var rows = doc.DocumentNode.SelectNodes("//table//tr | //ul[contains(@class,'job-info')]//li");
            if (rows != null)
            {
                foreach (var row in rows)
                {
                    var text = row.InnerText.Trim();
                    if (text.Contains("Eğitim")) job.EducationLevel = text.Split(':').LastOrDefault()?.Trim() ?? "";
                    if (text.Contains("Deneyim")) job.Experience = text.Split(':').LastOrDefault()?.Trim() ?? "";
                    if (text.Contains("Çalışma")) job.WorkType = text.Split(':').LastOrDefault()?.Trim() ?? "";
                }
            }

            return job;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "[eleman.net] Detay hatası: {Url}", url);
            return null;
        }
    }
}