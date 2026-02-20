using Cortex.Mediator;
using Harmony.Application.Contract.Requests;
using Microsoft.AspNetCore.Authorization;
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
}
