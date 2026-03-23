using HtmlAgilityPack;
using IsBulur.Shared.Models;

namespace IsBulur.API.Services.Scrapers;

public class SecretCvScraper : IJobScraper
{
    private readonly HttpClient _http;
    private readonly ILogger<SecretCvScraper> _log;
    private const string Base = "https://www.secretcv.com";

    public string SourceName => "secretcv.com";

    public SecretCvScraper(HttpClient http, ILogger<SecretCvScraper> log)
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

            var page = request.Page > 1 ? $"?page={request.Page}" : "";

            var url = string.IsNullOrWhiteSpace(request.Keyword)
                ? $"{Base}/is-ilanlari{page}"
                : $"{Base}/is-ilanlari/{kw}{loc}{page}";

            _log.LogInformation("[secretcv.com] Scraping: {Url}", url);

            await Task.Delay(Random.Shared.Next(500, 1000));
            var html = await _http.GetStringAsync(url);

            return ParseJobs(html);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "[secretcv.com] Hata");
            return new List<JobListing>();
        }
    }

    private List<JobListing> ParseJobs(string html)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var cards = doc.DocumentNode.SelectNodes("//div[contains(@class,'cv-job-box')]");
        if (cards == null) return new List<JobListing>();

        var jobs = new List<JobListing>();

        foreach (var card in cards)
        {
            try
            {
                // Başlık ve link
                var titleNode = card.SelectSingleNode(".//a[contains(@class,'title')]");
                var title = titleNode?.InnerText.Trim() ?? "";
                var url = titleNode?.GetAttributeValue("href", "") ?? "";

                if (string.IsNullOrEmpty(title)) continue;

                // Şirket
                var company = card.SelectSingleNode(".//a[contains(@class,'company')]")
                    ?.InnerText.Trim() ?? "";

                // Konum — ilk span içindeki metin
                var cityNode = card.SelectSingleNode(".//span[contains(@class,'city')]//span[1]");
                var city = cityNode?.InnerText.Trim() ?? "";

                // Tarih
                var timeAgo = card.SelectSingleNode(".//small[contains(@class,'text-muted')]")
                    ?.InnerText.Replace("İlan Tarihi:", "").Trim() ?? "";

                // Logo
                var logo = card.SelectSingleNode(".//img[contains(@class,'img-brand')]")
                    ?.GetAttributeValue("src", "") ?? "";

                jobs.Add(new JobListing
                {
                    Title = title,
                    Company = company,
                    City = city,
                    TimeAgo = timeAgo,
                    Url = url.StartsWith("http") ? url : Base + url,
                    CompanyLogoUrl = logo,
                    Source = SourceName,
                    ScrapedAt = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _log.LogWarning("[secretcv.com] Kart parse hatası: {Msg}", ex.Message);
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

            // Başlık
            job.Title = HtmlEntity.DeEntitize(
                doc.DocumentNode.SelectSingleNode("//h1 | //h2[contains(@class,'cj-title')]")
                ?.InnerText.Trim() ?? "");

            // Şirket — URL'den
            try
            {
                var uri = new Uri(url);
                var segment = uri.Segments.Length > 1 ? uri.Segments[1].Trim('/') : "";
                job.Company = HtmlEntity.DeEntitize(
                    string.Join(" ", segment.Split('-')
                        .Select(w => w.Length > 0 ? char.ToUpper(w[0]) + w.Substring(1) : w)));
            }
            catch { }

            // İş tanımı — tüm olası selector'ları dene
            string[] selectors = {
            "//div[contains(@class,'content-job')]",
            "//div[contains(@class,'cv-card')]",
            "//div[contains(@class,'job-detail')]",
            "//div[contains(@class,'ilan-detay')]",
            "//main",
            "//article"
        };

            foreach (var selector in selectors)
            {
                var node = doc.DocumentNode.SelectSingleNode(selector);
                if (node == null) continue;

                var nodes = node.SelectNodes(".//p | .//li");
                if (nodes == null || nodes.Count == 0) continue;

                var lines = nodes
                    .Select(n => HtmlEntity.DeEntitize(n.InnerText.Trim()))
                    .Where(t => t.Length > 3
                        && !t.Contains("İşe Başvur")
                        && !t.Contains("Cv Oluştur")
                        && !t.Contains("İlanı Şikayet")
                        && !t.Contains("veya"))
                    .ToList();

                if (lines.Count > 0)
                {
                    job.Description = string.Join("\n", lines);
                    break;
                }
            }

            // Hala boşsa tüm sayfanın text'ini al
            if (string.IsNullOrEmpty(job.Description))
            {
                _log.LogWarning("[secretcv.com] Selector çalışmadı, ham text alınıyor: {Url}", url);
                job.Description = HtmlEntity.DeEntitize(
                    doc.DocumentNode.InnerText
                        .Replace("\t", " ")
                        .Replace("\r", "")
                        .Split('\n')
                        .Select(l => l.Trim())
                        .Where(l => l.Length > 5)
                        .FirstOrDefault() ?? "");
            }

            return job;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "[secretcv.com] Detay hatası: {Url}", url);
            return null;
        }
    }
}


