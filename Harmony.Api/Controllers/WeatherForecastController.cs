using Cortex.Mediator;
using Harmony.Application.Contract.Requests;
using Microsoft.AspNetCore.Mvc;

namespace Harmony.Api.Controllers;

[ApiController]
[Route("[controller]")]
public class WeatherForecastController : ControllerBase
{
    private readonly IMediator _mediator;
    public WeatherForecastController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    [Route("test")]
    public async Task<IActionResult> Test(AddPostDto dto) => Ok(await _mediator.SendCommandAsync(dto));
}
