using Microsoft.AspNetCore.Authorization;
using System.Security.Cryptography;
using System.Text;
using OrbitMesh.Server.Services;
using Microsoft.AspNetCore.Mvc;

namespace OrbitMesh.Server.Controllers;

/// <summary>
/// Unauthenticated REST enrollment for an Edge that can't (yet) connect over SignalR with a valid
/// AccessKey: <c>POST rest/enroll</c> to record the attempt, then <c>GET
/// rest/enroll/{instanceId}/status</c> every few seconds until an admin approves it from the Console's
/// pending-edges list. Deliberately its own controller (not part of ManagementController, which sits
/// behind the Management door) - it is marked <c>[AllowAnonymous]</c>.
/// <para>Being unauthenticated, everything here is bounded and validated: InstanceId must be a GUID,
/// text fields are length-capped, the registry caps total/per-IP entries, and every call carries the
/// Edge's persisted <see cref="OrbitMeshHeaderNames.EnrollSecret"/> (only its hash is stored) - so
/// knowing an InstanceId, which any <c>edges:read</c> user can see, is not enough to collect an
/// approved key or to take over someone else's pending entry.</para>
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("rest/enroll")]
public sealed class EnrollmentController(IPendingEdgeRegistry pendingEdgeRegistry, IOrbitMeshDirectory directory, ILogger<EnrollmentController> logger) : ControllerBase
{
    private const int MaxEdgeNameLength = 64;
    private const int MaxMachineNameLength = 128;
    private const int MaxPlatformLength = 32;
    private const int MinSecretLength = 32;
    private const int MaxSecretLength = 128;

    [HttpPost]
    [RequestSizeLimit(4096)]
    public ActionResult<EnrollStatusResponse> Enroll([FromBody] EnrollRequest request)
    {
        if (!TryGetInstanceId(request.InstanceId, out var instanceId) || !TryGetSecretHash(out var secretHash))
        {
            return BadRequest("A GUID InstanceId and an EnrollSecret header (32-128 characters) are required.");
        }
        if (string.IsNullOrWhiteSpace(request.EdgeName))
        {
            return BadRequest("EdgeName is required.");
        }

        if (directory.Current.Edges.Any(e => e.InstanceId != null && e.InstanceId.Equals(instanceId, StringComparison.OrdinalIgnoreCase)))
        {
            // Already configured server-side - re-enrolling would conflict (and re-approving would
            // try to mint a second credential under the same name). Whatever's failing is something
            // else (e.g. a stale AccessKey on disk) that only a human can fix.
            logger.LogWarning("Enrollment rejected: InstanceId={InstanceId} already has a configured Edge - check its AccessKey.", instanceId);
            return new EnrollStatusResponse(EnrollmentStatus.Rejected, null);
        }

        // An Edge configured by hand has no InstanceId on file (only approval from the pending list records
        // one), so the check above can't recognise it - and an Edge that merely failed to connect (the Server
        // was still starting, restarting for an update, ...) would then show up as a brand-new pending Edge
        // and never be told to retry its real connection. The name is the other identity the Server knows:
        // approval refuses an already-used name anyway, so refusing to record one loses nothing.
        var declaredName = request.EdgeName.Trim();
        if (directory.Current.Edges.Any(e => e.Name.Equals(declaredName, StringComparison.OrdinalIgnoreCase)))
        {
            logger.LogWarning(
                "Enrollment from InstanceId={InstanceId} not recorded: an Edge named '{EdgeName}' is already configured. If this is that Edge, its connection is failing (it will keep retrying); if it's a different machine, give it another Edge:EdgeName.",
                instanceId, declaredName.ForLog());
            return new EnrollStatusResponse(EnrollmentStatus.Rejected, null);
        }

        var remoteIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "?";
        var edgeName = Truncate(request.EdgeName.Trim(), MaxEdgeNameLength);
        var result = pendingEdgeRegistry.Enroll(instanceId, edgeName, Truncate(request.MachineName, MaxMachineNameLength), Truncate(request.Platform, MaxPlatformLength), remoteIp, secretHash);
        switch (result.Outcome)
        {
            case EnrollOutcome.TooMany:
                logger.LogWarning("Enrollment refused for {RemoteIp}: too many pending Edges.", remoteIp);
                return StatusCode(StatusCodes.Status429TooManyRequests, "Too many pending enrollments.");
            case EnrollOutcome.SecretMismatch:
                logger.LogWarning("Enrollment for InstanceId={InstanceId} from {RemoteIp} used a different EnrollSecret than the pending entry - ignored.", instanceId, remoteIp);
                return new EnrollStatusResponse(EnrollmentStatus.Rejected, null);
            default:
                logger.LogInformation("Enrollment request from '{EdgeName}' (InstanceId={InstanceId}, RemoteIp={RemoteIp})", edgeName.ForLog(), instanceId, remoteIp);
                return ToResponse(result);
        }
    }

    [HttpGet("{instanceId}/status")]
    public ActionResult<EnrollStatusResponse> GetStatus(string instanceId)
    {
        if (!TryGetInstanceId(instanceId, out var canonicalId) || !TryGetSecretHash(out var secretHash))
        {
            return new EnrollStatusResponse(EnrollmentStatus.Unknown, null);
        }
        Response.Headers.CacheControl = "no-store";
        return ToResponse(pendingEdgeRegistry.GetStatus(canonicalId, secretHash));
    }

    private static EnrollStatusResponse ToResponse(EnrollResult result) => result.Outcome switch
    {
        EnrollOutcome.Approved => new EnrollStatusResponse(EnrollmentStatus.Approved, result.AccessKey, result.EdgeName),
        EnrollOutcome.Pending => new EnrollStatusResponse(EnrollmentStatus.Pending, null),
        _ => new EnrollStatusResponse(EnrollmentStatus.Unknown, null)
    };

    private static bool TryGetInstanceId(string? value, out string instanceId)
    {
        if (Guid.TryParseExact(value, "D", out var guid))
        {
            instanceId = guid.ToString();
            return true;
        }
        instanceId = string.Empty;
        return false;
    }

    private bool TryGetSecretHash(out byte[] hash)
    {
        var secret = Request.Headers[OrbitMeshHeaderNames.EnrollSecret].ToString();
        if (secret.Length is < MinSecretLength or > MaxSecretLength)
        {
            hash = [];
            return false;
        }
        hash = SHA256.HashData(Encoding.UTF8.GetBytes(secret));
        return true;
    }

    private static string Truncate(string? value, int maxLength) =>
        string.IsNullOrEmpty(value) ? string.Empty : value.Length <= maxLength ? value : value[..maxLength];
}
