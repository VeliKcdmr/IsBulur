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
            var response = await _http.GetAsync(url);
            _log.LogInformation("[eleman.net] Detay HTTP {Status}: {Url}", (int)response.StatusCode, url);

            if (!response.IsSuccessStatusCode)
            {
                _log.LogWarning("[eleman.net] Detay başarısız: {Status}", response.StatusCode);
                return null;
            }

            var html = await response.Content.ReadAsStringAsync();
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var job = new JobListing { Url = url, Source = SourceName };

            // JSON-LD'den veri çek (en güvenilir yöntem)
            if (TryParseJobPosting(doc, job))
            {
                _log.LogInformation("[eleman.net] JSON-LD ile detay alındı: {Title}", job.Title);
                return job;
            }

            // Fallback: HTML selector'lar
            job.Title = HtmlEntity.DeEntitize(
                doc.DocumentNode.SelectSingleNode("//h1")?.InnerText.Trim() ?? "");
            job.Company = HtmlEntity.DeEntitize(
                doc.DocumentNode.SelectSingleNode("//*[contains(@class,'isverenAdi')]")
                ?.InnerText.Trim() ?? "");
            ParseInfoRows(doc, job);

            _log.LogInformation("[eleman.net] HTML fallback ile detay alındı: {Title}", job.Title);
            return job;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "[eleman.net] Detay hatası: {Url}", url);
            return null;
        }
    }

    private bool TryParseJobPosting(HtmlDocument doc, JobListing job)
    {
        var scripts = doc.DocumentNode.SelectNodes("//script[@type='application/ld+json']");
        if (scripts == null) return false;

        foreach (var script in scripts)
        {
            try
            {
                var json = script.InnerText.Trim();
                using var jdoc = System.Text.Json.JsonDocument.Parse(json);
                var root = jdoc.RootElement;

                if (!root.TryGetProperty("@type", out var typeEl) ||
                    typeEl.GetString() != "JobPosting") continue;

                if (root.TryGetProperty("title", out var t))
                    job.Title = HtmlEntity.DeEntitize(t.GetString() ?? "");

                if (root.TryGetProperty("description", out var d))
                    job.Description = HtmlEntity.DeEntitize(d.GetString() ?? "");

                if (root.TryGetProperty("employmentType", out var et))
                    job.WorkType = et.GetString() ?? "";

                if (root.TryGetProperty("validThrough", out var vt))
                    job.ClosingDate = vt.GetString()?.Split('T')[0] ?? "";

                if (root.TryGetProperty("hiringOrganization", out var org) &&
                    org.TryGetProperty("name", out var orgName))
                    job.Company = HtmlEntity.DeEntitize(orgName.GetString() ?? "");

                if (root.TryGetProperty("jobLocation", out var loc))
                {
                    if (loc.ValueKind == System.Text.Json.JsonValueKind.Array)
                    {
                        var cities = loc.EnumerateArray()
                            .Where(l => l.TryGetProperty("address", out _))
                            .Select(l => {
                                l.TryGetProperty("address", out var a);
                                a.TryGetProperty("addressLocality", out var al);
                                return al.GetString();
                            })
                            .Where(s => s != null);
                        job.City = string.Join(", ", cities);
                    }
                    else if (loc.TryGetProperty("address", out var addr) &&
                             addr.TryGetProperty("addressLocality", out var locality))
                        job.City = locality.GetString() ?? "";
                }

                if (root.TryGetProperty("educationRequirements", out var edu))
                {
                    if (edu.ValueKind == System.Text.Json.JsonValueKind.Array)
                        job.EducationLevel = string.Join(", ", edu.EnumerateArray()
                            .Select(e => e.TryGetProperty("credentialCategory", out var cc)
                                ? cc.GetString() : e.GetString())
                            .Where(s => !string.IsNullOrEmpty(s)));
                    else if (edu.ValueKind == System.Text.Json.JsonValueKind.String)
                        job.EducationLevel = edu.GetString() ?? "";
                }

                if (root.TryGetProperty("experienceRequirements", out var exp))
                    job.Experience = exp.ValueKind == System.Text.Json.JsonValueKind.String
                        ? exp.GetString() ?? ""
                        : exp.TryGetProperty("name", out var expN) ? expN.GetString() ?? "" : "";

                if (root.TryGetProperty("industry", out var ind))
                {
                    if (ind.ValueKind == System.Text.Json.JsonValueKind.Array)
                        job.Sector = string.Join(", ", ind.EnumerateArray()
                            .Select(i => i.GetString()).Where(s => s != null));
                    else if (ind.ValueKind == System.Text.Json.JsonValueKind.String)
                        job.Sector = ind.GetString() ?? "";
                }

                return !string.IsNullOrEmpty(job.Description);
            }
            catch { /* Bu script parse edilemiyorsa atla */ }
        }
        return false;
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
