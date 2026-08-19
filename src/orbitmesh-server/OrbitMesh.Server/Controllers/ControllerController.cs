using Microsoft.AspNetCore.Mvc;

namespace OrbitMesh.Server.Controllers;

[ApiController]
[Route("rest/controller")]
public sealed class ControllerController : ControllerBase
{
    [HttpGet("CheckAccess")]
    public IActionResult CheckAccess() => Ok();
}
