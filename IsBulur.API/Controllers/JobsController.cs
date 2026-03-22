using IsBulur.Shared.Models;
using IsBulur.API.Services;
using Microsoft.AspNetCore.Mvc;

namespace IsBulur.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class JobsController : ControllerBase
{
    private readonly JobAggregatorService _aggregator;

    public JobsController(JobAggregatorService aggregator)
    {
        _aggregator = aggregator;
    }

    // GET /api/jobs/search?keyword=yazilim&location=istanbul
    [HttpGet("search")]
    public async Task<ActionResult<SearchResponse>> Search([FromQuery] SearchRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Keyword) && string.IsNullOrWhiteSpace(request.Location))
            return BadRequest("Keyword veya location giriniz.");

        var result = await _aggregator.SearchAsync(request);
        return Ok(result);
    }

    // GET /api/jobs/sources
    [HttpGet("sources")]
    public ActionResult<List<string>> GetSources([FromServices] IEnumerable<IJobScraper> scrapers)
    {
        return Ok(scrapers.Select(s => s.SourceName).ToList());
    }
}