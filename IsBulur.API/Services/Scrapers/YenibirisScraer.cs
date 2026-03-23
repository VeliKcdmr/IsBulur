using HtmlAgilityPack;
using IsBulur.Shared.Models;

namespace IsBulur.API.Services.Scrapers;

public class YenibirisScraper : IJobScraper
{
    private readonly HttpClient _http;
    private readonly ILogger<YenibirisScraper> _log;
    private const string Base = "https://www.yenibiris.com";

    public string SourceName => "yenibiris.com";

    public YenibirisScraper(HttpClient http, ILogger<YenibirisScraper> log)
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
            // URL formatı: /is-ilanlari/keyword-sehir
            var kw = request.Keyword.Trim().ToLower()
                .Replace("ı", "i").Replace("ğ", "g")
                .Replace("ü", "u").Replace("ş", "s")
                .Replace("ö", "o").Replace("ç", "c")
                .Replace(" ", "-");

            var loc = string.IsNullOrWhiteSpace(request.Location)
                ? ""
                : "-" + request.Location.Trim().ToLower()
                    .Replace("ı", "i").Replace("ğ", "g")
                    .Replace("ü", "u").Replace("ş", "s")
                    .Replace("ö", "o").Replace("ç", "c")
                    .Replace(" ", "-");

            var page = request.Page > 1 ? $"?sayfa={request.Page}" : "";
            var url = string.IsNullOrWhiteSpace(request.Keyword)
                ? $"{Base}/is-ilanlari{page}"
                : $"{Base}/is-ilanlari/{kw}{loc}{page}";

            _log.LogInformation("[yenibiris.com] Scraping: {Url}", url);

            await Task.Delay(Random.Shared.Next(500, 1000));
            var html = await _http.GetStringAsync(url);

            return ParseJobs(html);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "[yenibiris.com] Hata");
            return new List<JobListing>();
        }
    }

    private List<JobListing> ParseJobs(string html)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var cards = doc.DocumentNode.SelectNodes("//div[contains(@class,'listViewRows')]");
        if (cards == null) return new List<JobListing>();

        var jobs = new List<JobListing>();

        foreach (var card in cards)
        {
            try
            {
                // Başlık
                var titleNode = card.SelectSingleNode(".//a[contains(@class,'gtmTitle')]");
                var title = titleNode?.InnerText.Trim() ?? "";
                if (string.IsNullOrEmpty(title)) continue;

                // Link
                var href = titleNode?.GetAttributeValue("href", "") ?? "";
                var url = href.StartsWith("http") ? href : Base + href;

                // Şirket
                var company = card.SelectSingleNode(".//*[contains(@class,'jobCompanyLnk')]")
                    ?.InnerText.Trim() ?? "";

                // Konum
                var city = card.SelectSingleNode(".//*[contains(@class,'gtmLocation')]")
                    ?.InnerText.Trim() ?? "";

                // Logo
                var logo = card.SelectSingleNode(".//img")
                    ?.GetAttributeValue("src", "") ?? "";

                // Çalışma tipi ve modeli
                var tags = card.SelectNodes(".//*[contains(@class,'listJobTag')]");
                var workType = tags?.Count > 0 ? tags[0].InnerText.Trim() : "";
                var workModel = tags?.Count > 1 ? tags[1].InnerText.Trim() : "";

                // Tarih
                var timeAgo = card.SelectSingleNode(".//*[contains(@class,'orderDateTxt')]//div")
                    ?.GetAttributeValue("title", "") ?? "";

                jobs.Add(new JobListing
                {
                    Title = title,
                    Company = company,
                    City = city,
                    WorkType = workType,
                    WorkModel = workModel,
                    TimeAgo = timeAgo,
                    Url = url,
                    CompanyLogoUrl = logo.StartsWith("http") ? logo : Base + logo,
                    Source = SourceName,
                    ScrapedAt = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _log.LogWarning("[yenibiris.com] Kart parse hatası: {Msg}", ex.Message);
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
            job.Company = doc.DocumentNode.SelectSingleNode("//*[contains(@class,'company-name')]")?.InnerText.Trim() ?? "";

            // İş tanımı
            var descNode = doc.DocumentNode.SelectSingleNode("//*[contains(@class,'job-description')] | //*[contains(@class,'detailContent')]");
            job.Description = descNode?.InnerText.Trim() ?? "";

            // Ek bilgiler
            var labels = doc.DocumentNode.SelectNodes("//label[contains(@class,'col-lg-3')]");
            var values = doc.DocumentNode.SelectNodes("//span[contains(@class,'col-lg-8')]");

            if (labels != null && values != null)
            {
                for (int i = 0; i < Math.Min(labels.Count, values.Count); i++)
                {
                    var label = labels[i].InnerText.Trim();
                    var value = values[i].InnerText.Trim();

                    if (label.Contains("Deneyim")) job.Experience = value;
                    if (label.Contains("Eğitim")) job.EducationLevel = value;
                    if (label.Contains("Son Başvuru")) job.ClosingDate = value;
                    if (label.Contains("Çalışma Şekli")) job.WorkType = value;
                    if (label.Contains("Çalışma Türü")) job.WorkModel = value;
                }
            }

            return job;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "[yenibiris.com] Detay hatası: {Url}", url);
            return null;
        }
    }
}