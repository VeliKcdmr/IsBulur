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
                var titleNode = card.SelectSingleNode(".//a[contains(@class,'title')]");
                var title = titleNode?.InnerText.Trim() ?? "";
                var url = titleNode?.GetAttributeValue("href", "") ?? "";

                if (string.IsNullOrEmpty(title)) continue;

                var company = card.SelectSingleNode(".//a[contains(@class,'company')]")
                    ?.InnerText.Trim() ?? "";

                var cityNode = card.SelectSingleNode(".//span[contains(@class,'city')]//span[1]");
                var city = cityNode?.InnerText.Trim() ?? "";

                var timeAgo = card.SelectSingleNode(".//small[contains(@class,'text-muted')]")
                    ?.InnerText.Replace("İlan Tarihi:", "").Trim() ?? "";

                var logo = card.SelectSingleNode(".//img[contains(@class,'img-brand')]")
                    ?.GetAttributeValue("src", "") ?? "";

                jobs.Add(new JobListing
                {
                    Title = HtmlEntity.DeEntitize(title),
                    Company = HtmlEntity.DeEntitize(company),
                    City = HtmlEntity.DeEntitize(city),
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
            await Task.Delay(200);
            var html = await _http.GetStringAsync(url);
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var job = new JobListing { Url = url, Source = SourceName };

            // Başlık
            job.Title = HtmlEntity.DeEntitize(
                doc.DocumentNode.SelectSingleNode(
                    "//h1 | //h2[contains(@class,'cj-title')] | //*[contains(@class,'job-title')]")
                ?.InnerText.Trim() ?? "");

            // Şirket — sayfadan veya URL'den
            var companyNode = doc.DocumentNode.SelectSingleNode(
                "//*[contains(@class,'company-name')] | " +
                "//*[contains(@class,'employer')] | " +
                "//*[contains(@class,'cj-company')] | " +
                "//a[contains(@href,'/sirket/')]");
            job.Company = HtmlEntity.DeEntitize(companyNode?.InnerText.Trim() ?? "");

            // Konum
            job.City = HtmlEntity.DeEntitize(
                doc.DocumentNode.SelectSingleNode(
                    "//*[contains(@class,'city')] | " +
                    "//*[contains(@class,'location')] | " +
                    "//*[contains(@class,'cj-city')]")
                ?.InnerText.Trim() ?? "");

            // İş tanımı — önce yapısal selector'lar, sonra fallback
            string[] descSelectors =
            [
                "//div[contains(@class,'content-job')]",
                "//div[contains(@class,'cv-card-body')]",
                "//div[contains(@class,'job-detail')]",
                "//div[contains(@class,'cj-description')]",
                "//div[contains(@class,'ilan-detay')]",
                "//div[@id='jobDescription']",
                "//div[@id='ilanMetni']",
                "//article",
                "//main",
            ];

            foreach (var selector in descSelectors)
            {
                var node = doc.DocumentNode.SelectSingleNode(selector);
                if (node == null) continue;

                // Gereksiz navigasyon/buton nodlarını atla
                var paraNodes = node.SelectNodes(".//p | .//li");
                if (paraNodes != null && paraNodes.Count > 0)
                {
                    var lines = paraNodes
                        .Select(n => HtmlEntity.DeEntitize(n.InnerText.Trim()))
                        .Where(t => t.Length > 5
                            && !t.Contains("İşe Başvur")
                            && !t.Contains("Cv Oluştur")
                            && !t.Contains("İlanı Şikayet")
                            && !t.Contains("Giriş Yap")
                            && !t.Contains("Üye Ol"))
                        .ToList();

                    if (lines.Count > 0)
                    {
                        job.Description = string.Join("\n", lines);
                        break;
                    }
                }

                // Paragraf yoksa düz metin dene
                var rawText = HtmlEntity.DeEntitize(node.InnerText.Trim());
                if (rawText.Length > 50)
                {
                    job.Description = CleanWhitespace(rawText);
                    break;
                }
            }

            // Ek bilgiler — tablo veya dl/dt/dd yapısı
            ParseInfoRows(doc, job);

            _log.LogInformation("[secretcv.com] Detay alındı: {Title}", job.Title);
            return job;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "[secretcv.com] Detay hatası: {Url}", url);
            return null;
        }
    }

    private static void ParseInfoRows(HtmlDocument doc, JobListing job)
    {
        // dt/dd çiftleri
        var dtNodes = doc.DocumentNode.SelectNodes("//dl//dt");
        var ddNodes = doc.DocumentNode.SelectNodes("//dl//dd");

        if (dtNodes != null && ddNodes != null)
        {
            for (int i = 0; i < Math.Min(dtNodes.Count, ddNodes.Count); i++)
            {
                var label = dtNodes[i].InnerText.Trim();
                var value = HtmlEntity.DeEntitize(ddNodes[i].InnerText.Trim());
                ApplyInfoField(label, value, job);
            }
        }

        // label/span çiftleri (Bootstrap col yapısı)
        var labels = doc.DocumentNode.SelectNodes("//label[contains(@class,'col-')]");
        var values = doc.DocumentNode.SelectNodes("//span[contains(@class,'col-')]");

        if (labels != null && values != null)
        {
            for (int i = 0; i < Math.Min(labels.Count, values.Count); i++)
            {
                var label = labels[i].InnerText.Trim();
                var value = HtmlEntity.DeEntitize(values[i].InnerText.Trim());
                ApplyInfoField(label, value, job);
            }
        }

        // Tablo satırları
        var rows = doc.DocumentNode.SelectNodes("//table//tr");
        if (rows == null) return;

        foreach (var row in rows)
        {
            var cells = row.SelectNodes(".//td | .//th");
            if (cells == null || cells.Count < 2) continue;
            var label = cells[0].InnerText.Trim();
            var value = HtmlEntity.DeEntitize(cells[1].InnerText.Trim());
            ApplyInfoField(label, value, job);
        }
    }

    private static void ApplyInfoField(string label, string value, JobListing job)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        if (label.Contains("Deneyim", StringComparison.OrdinalIgnoreCase)) job.Experience = value;
        else if (label.Contains("Eğitim", StringComparison.OrdinalIgnoreCase)) job.EducationLevel = value;
        else if (label.Contains("Son Başvuru", StringComparison.OrdinalIgnoreCase)) job.ClosingDate = value;
        else if (label.Contains("Çalışma Şekli", StringComparison.OrdinalIgnoreCase)) job.WorkType = value;
        else if (label.Contains("Çalışma Türü", StringComparison.OrdinalIgnoreCase)) job.WorkModel = value;
        else if (label.Contains("Sektör", StringComparison.OrdinalIgnoreCase)) job.Sector = value;
    }

    private static string CleanWhitespace(string text) =>
        System.Text.RegularExpressions.Regex.Replace(text, @"\s{3,}", "\n\n").Trim();
}
