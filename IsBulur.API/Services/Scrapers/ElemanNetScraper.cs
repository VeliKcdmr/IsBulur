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
                var linkNode = card.SelectSingleNode(".//a");
                var url = linkNode?.GetAttributeValue("href", "") ?? "";
                var title = card.SelectSingleNode(".//h3")?.InnerText.Trim() ?? "";

                title = System.Text.RegularExpressions.Regex
                    .Replace(title, @"\s+", " ").Trim();

                if (string.IsNullOrEmpty(title)) continue;

                var subtitle = card.SelectSingleNode(".//*[contains(@class,'c-showcase-box__subtitle')]")
                    ?.InnerText.Trim() ?? "";

                var parts = subtitle.Split('-');
                var company = parts.Length > 0 ? parts[0].Trim() : "";
                var city = parts.Length > 1 ? parts[1].Trim() : "";

                var description = card.SelectSingleNode(".//*[contains(@class,'c-showcase-box__text')]")
                    ?.InnerText.Trim() ?? "";

                jobs.Add(new JobListing
                {
                    Title = HtmlEntity.DeEntitize(title),
                    Company = HtmlEntity.DeEntitize(company),
                    City = HtmlEntity.DeEntitize(city),
                    Description = HtmlEntity.DeEntitize(description),
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
            await Task.Delay(200);
            var html = await _http.GetStringAsync(url);
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var job = new JobListing { Url = url, Source = SourceName };

            // Başlık
            job.Title = HtmlEntity.DeEntitize(
                doc.DocumentNode.SelectSingleNode("//h1")?.InnerText.Trim() ?? "");

            // Şirket — birden fazla yerde olabilir
            job.Company = HtmlEntity.DeEntitize(
                doc.DocumentNode.SelectSingleNode(
                    "//*[contains(@class,'isverenAdi')] | " +
                    "//*[contains(@class,'company-name')] | " +
                    "//*[contains(@class,'c-company-name')] | " +
                    "//*[contains(@class,'employer-name')]")
                ?.InnerText.Trim() ?? "");

            // Konum
            job.City = HtmlEntity.DeEntitize(
                doc.DocumentNode.SelectSingleNode(
                    "//*[contains(@class,'sehir')] | " +
                    "//*[contains(@class,'city')] | " +
                    "//*[contains(@class,'location')]")
                ?.InnerText.Trim() ?? "");

            // İş tanımı — eleman.net'te genellikle .ilan-aciklama veya .c-job-description
            string[] descSelectors =
            [
                "//div[contains(@class,'ilan-aciklama')]",
                "//div[contains(@class,'job-description')]",
                "//div[contains(@class,'c-job-description')]",
                "//div[contains(@class,'ilan-detay')]",
                "//div[contains(@class,'jobDesc')]",
                "//div[@id='ilanMetni']",
                "//div[@id='jobDescription']",
                "//section[contains(@class,'description')]",
            ];

            foreach (var selector in descSelectors)
            {
                var node = doc.DocumentNode.SelectSingleNode(selector);
                if (node == null) continue;

                var text = HtmlEntity.DeEntitize(node.InnerText.Trim());
                if (text.Length > 20)
                {
                    job.Description = CleanWhitespace(text);
                    break;
                }
            }

            // Ek bilgiler — tablo veya liste satırları
            ParseInfoRows(doc, job);

            return job;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "[eleman.net] Detay hatası: {Url}", url);
            return null;
        }
    }

    private static void ParseInfoRows(HtmlDocument doc, JobListing job)
    {
        // dt/dd çiftleri veya label/value yapısı
        var dtNodes = doc.DocumentNode.SelectNodes("//dl//dt");
        var ddNodes = doc.DocumentNode.SelectNodes("//dl//dd");

        if (dtNodes != null && ddNodes != null)
        {
            for (int i = 0; i < Math.Min(dtNodes.Count, ddNodes.Count); i++)
            {
                var label = dtNodes[i].InnerText.Trim();
                var value = HtmlEntity.DeEntitize(ddNodes[i].InnerText.Trim());

                if (label.Contains("Deneyim", StringComparison.OrdinalIgnoreCase)) job.Experience = value;
                else if (label.Contains("Eğitim", StringComparison.OrdinalIgnoreCase)) job.EducationLevel = value;
                else if (label.Contains("Son Başvuru", StringComparison.OrdinalIgnoreCase)) job.ClosingDate = value;
                else if (label.Contains("Çalışma Şekli", StringComparison.OrdinalIgnoreCase)) job.WorkType = value;
                else if (label.Contains("Çalışma Türü", StringComparison.OrdinalIgnoreCase)) job.WorkModel = value;
                else if (label.Contains("Sektör", StringComparison.OrdinalIgnoreCase)) job.Sector = value;
            }
        }

        // Tablo formatı
        var rows = doc.DocumentNode.SelectNodes("//table//tr");
        if (rows == null) return;

        foreach (var row in rows)
        {
            var cells = row.SelectNodes(".//td | .//th");
            if (cells == null || cells.Count < 2) continue;

            var label = cells[0].InnerText.Trim();
            var value = HtmlEntity.DeEntitize(cells[1].InnerText.Trim());

            if (label.Contains("Deneyim", StringComparison.OrdinalIgnoreCase)) job.Experience = value;
            else if (label.Contains("Eğitim", StringComparison.OrdinalIgnoreCase)) job.EducationLevel = value;
            else if (label.Contains("Son Başvuru", StringComparison.OrdinalIgnoreCase)) job.ClosingDate = value;
            else if (label.Contains("Çalışma", StringComparison.OrdinalIgnoreCase)) job.WorkType = value;
        }
    }

    private static string CleanWhitespace(string text) =>
        System.Text.RegularExpressions.Regex.Replace(text, @"\s{3,}", "\n\n").Trim();
}
