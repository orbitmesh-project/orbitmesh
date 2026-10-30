using Microsoft.AspNetCore.Authorization;
using OrbitMesh.Server.Security;
using Microsoft.AspNetCore.Mvc;

namespace OrbitMesh.Server.Controllers;

[ApiController]
[Authorize(Policy = OrbitMeshPolicies.ControllerDoor)]
[Route("rest/controller")]
public sealed class ControllerController : ControllerBase
{
    [HttpGet("CheckAccess")]
    public IActionResult CheckAccess() => Ok();
}
