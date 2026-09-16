using OrbitMesh.Server.Services;
using Microsoft.AspNetCore.Mvc;

namespace OrbitMesh.Server.Controllers;

/// <summary>
/// Unauthenticated REST enrollment for an Edge that can't (yet) connect over SignalR with a valid
/// AccessKey: <c>POST rest/enroll</c> once to record the attempt, then <c>GET
/// rest/enroll/{instanceId}/status</c> every few seconds until an admin approves it from the Console's
/// pending-edges list. Deliberately its own controller (not part of ManagementController, which sits
/// behind AccessKeyAuthenticationMiddleware's Management gate) - see the matching bypass in that
/// middleware for the <c>rest/enroll</c> prefix. No secrets are accepted here; the only thing handed
/// out is a freshly-approved AccessKey, and only to whoever polls with the matching InstanceId.
/// </summary>
[ApiController]
[Route("rest/enroll")]
public sealed class EnrollmentController(IPendingEdgeRegistry pendingEdgeRegistry, IOrbitMeshDirectory directory, ILogger<EnrollmentController> logger) : ControllerBase
{
    [HttpPost]
    public ActionResult<EnrollStatusResponse> Enroll([FromBody] EnrollRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.InstanceId))
        {
            return BadRequest("InstanceId is required.");
        }

        if (directory.Current.Edges.Any(e => e.InstanceId == request.InstanceId))
        {
            // Already configured server-side - re-enrolling would conflict (and re-approving would
            // try to mint a second credential under the same name). Whatever's failing is something
            // else (e.g. a stale AccessKey on disk) that only a human can fix.
            logger.LogWarning("Enrollment rejected: InstanceId={InstanceId} (EdgeName={EdgeName}) already has a configured Edge - check its AccessKey.", request.InstanceId, request.EdgeName.ForLog());
            return new EnrollStatusResponse(EnrollmentStatus.Rejected, null);
        }

        var remoteIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "?";
        pendingEdgeRegistry.RecordAttempt(request.InstanceId, request.EdgeName, remoteIp);
        logger.LogInformation("Enrollment request from '{EdgeName}' (InstanceId={InstanceId}, RemoteIp={RemoteIp})", request.EdgeName.ForLog(), request.InstanceId, remoteIp);
        return BuildStatusResponse(request.InstanceId);
    }

    [HttpGet("{instanceId}/status")]
    public ActionResult<EnrollStatusResponse> GetStatus(string instanceId)
    {
        pendingEdgeRegistry.Touch(instanceId);
        return BuildStatusResponse(instanceId);
    }

    private ActionResult<EnrollStatusResponse> BuildStatusResponse(string instanceId)
    {
        var entry = pendingEdgeRegistry.Get(instanceId);
        if (entry == null)
        {
            return new EnrollStatusResponse(EnrollmentStatus.Unknown, null);
        }
        return entry.ApprovedAccessKey != null
            ? new EnrollStatusResponse(EnrollmentStatus.Approved, entry.ApprovedAccessKey)
            : new EnrollStatusResponse(EnrollmentStatus.Pending, null);
    }
}
