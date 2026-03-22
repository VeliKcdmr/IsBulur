using HtmlAgilityPack;
using IsBulur.Shared.Models;

namespace IsBulur.API.Services.Scrapers;

public class KariyerNetScraper : IJobScraper
{
    private readonly HttpClient _http;
    private readonly ILogger<KariyerNetScraper> _log;
    private const string Base = "https://www.kariyer.net";

    public string SourceName => "kariyer.net";

    private static readonly Dictionary<string, int> CityCodeMap = new(StringComparer.OrdinalIgnoreCase)
    {
        {"adana",1},{"adıyaman",2},{"afyon",3},{"ağrı",4},{"amasya",5},
        {"ankara",6},{"antalya",7},{"artvin",8},{"aydın",9},{"balıkesir",10},
        {"bilecik",11},{"bingöl",12},{"bitlis",13},{"bolu",14},{"burdur",15},
        {"bursa",16},{"çanakkale",17},{"çankırı",18},{"çorum",19},{"denizli",20},
        {"diyarbakır",21},{"edirne",22},{"elazığ",23},{"erzincan",24},{"erzurum",25},
        {"eskişehir",26},{"gaziantep",27},{"giresun",28},{"gümüşhane",29},{"hakkari",30},
        {"hatay",31},{"ısparta",32},{"mersin",33},{"istanbul",34},{"izmir",35},
        {"kars",36},{"kastamonu",37},{"kayseri",38},{"kırklareli",39},{"kırşehir",40},
        {"kocaeli",41},{"konya",42},{"kütahya",43},{"malatya",44},{"manisa",45},
        {"kahramanmaraş",46},{"mardin",47},{"muğla",48},{"muş",49},{"nevşehir",50},
        {"niğde",51},{"ordu",52},{"rize",53},{"sakarya",54},{"samsun",55},
        {"siirt",56},{"sinop",57},{"sivas",58},{"tekirdağ",59},{"tokat",60},
        {"trabzon",61},{"tunceli",62},{"şanlıurfa",63},{"uşak",64},{"van",65},
        {"yozgat",66},{"zonguldak",67},{"aksaray",68},{"bayburt",69},{"karaman",70},
        {"kırıkkale",71},{"batman",72},{"şırnak",73},{"bartın",74},{"ardahan",75},
        {"ığdır",76},{"yalova",77},{"karabük",78},{"kilis",79},{"osmaniye",80},
        {"düzce",81}
    };

    public KariyerNetScraper(HttpClient http, ILogger<KariyerNetScraper> log)
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
            var cityCode = "";
            if (!string.IsNullOrWhiteSpace(request.Location))
            {
                if (CityCodeMap.TryGetValue(request.Location.Trim(), out int code))
                    cityCode = $"&ct={code}";
            }

            var citySlug = string.IsNullOrWhiteSpace(request.Location)
                ? ""
                : request.Location.Trim().ToLower()
                    .Replace("ı", "i").Replace("ğ", "g")
                    .Replace("ü", "u").Replace("ş", "s")
                    .Replace("ö", "o").Replace("ç", "c")
                    .Replace(" ", "-");

            var kw = Uri.EscapeDataString(request.Keyword.Trim());
            var pageParam = request.Page > 1 ? $"&sayfa={request.Page}" : "";

            var url = string.IsNullOrWhiteSpace(request.Keyword)
                ? $"{Base}/is-ilanlari/{citySlug}?{cityCode.TrimStart('&')}{pageParam}"
                : $"{Base}/is-ilanlari/{citySlug}?{cityCode.TrimStart('&')}&kw={kw}{pageParam}";

            _log.LogInformation("[kariyer.net] Scraping: {Url}", url);

            await Task.Delay(Random.Shared.Next(500, 1000));
            var html = await _http.GetStringAsync(url);

            return ParseJobs(html);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "[kariyer.net] Hata");
            return new List<JobListing>();
        }
    }

    private List<JobListing> ParseJobs(string html)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var cards = doc.DocumentNode.SelectNodes("//div[@data-test='ad-card']");
        if (cards == null) return new List<JobListing>();

        var jobs = new List<JobListing>();
        foreach (var card in cards)
        {
            try
            {
                var job = new JobListing
                {
                    Title = card.GetAttributeValue("positionname", ""),
                    Company = card.SelectSingleNode(".//span[@data-test='subtitle']")?.InnerText.Trim() ?? "",
                    City = card.GetAttributeValue("cityname", ""),
                    Sector = card.GetAttributeValue("sectorname", ""),
                    WorkType = card.GetAttributeValue("worktypetext", ""),
                    WorkModel = card.GetAttributeValue("workmodeltext", ""),
                    TimeAgo = card.GetAttributeValue("time", ""),
                    Url = Base + (card.SelectSingleNode(".//a[@data-test='ad-card-item']")?.GetAttributeValue("href", "") ?? ""),
                    CompanyLogoUrl = card.SelectSingleNode(".//img[@data-test='company-image']")?.GetAttributeValue("src", "") ?? "",
                    Source = SourceName,
                    ScrapedAt = DateTime.UtcNow
                };

                if (!string.IsNullOrEmpty(job.Title))
                    jobs.Add(job);
            }
            catch (Exception ex)
            {
                _log.LogWarning("[kariyer.net] Kart parse hatası: {Msg}", ex.Message);
            }
        }

        return jobs;
    }
}