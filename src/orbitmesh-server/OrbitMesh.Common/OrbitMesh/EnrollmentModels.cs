namespace OrbitMesh;

/// <summary>Body of <c>POST rest/enroll</c> - an Edge's self-reported identity, sent once on first
/// connect when the normal SignalR connect fails (not yet approved, or unreachable). Mirrors the
/// subset of <see cref="EdgeDescription"/> the server needs to show an admin a pending Edge.</summary>
public sealed record EnrollRequest(string EdgeName, string InstanceId, string MachineName, string OSVersion, string OSCaption, string Platform);

/// <summary>Response to both <c>POST rest/enroll</c> and <c>GET rest/enroll/{instanceId}/status</c>.
/// <see cref="AccessKey"/> is only populated when <see cref="Status"/> is
/// <see cref="EnrollmentStatus.Approved"/> - see <see cref="EnrollmentStatus"/> for the possible values.</summary>
public sealed record EnrollStatusResponse(string Status, string? AccessKey);

/// <summary>Possible <see cref="EnrollStatusResponse.Status"/> values.</summary>
public static class EnrollmentStatus
{
    /// <summary>Recorded, waiting on an admin to approve it from the Console.</summary>
    public const string Pending = "pending";
    /// <summary>Approved - <see cref="EnrollStatusResponse.AccessKey"/> carries the new credential.</summary>
    public const string Approved = "approved";
    /// <summary>This InstanceId has no pending (or recently-approved) enrollment on record.</summary>
    public const string Unknown = "unknown";
    /// <summary>This InstanceId already has a configured Edge - enrolling again would conflict.
    /// Something else is wrong (e.g. a stale/incorrect AccessKey on disk) and needs a human to fix
    /// appsettings.json by hand; retrying enrollment will never resolve this on its own.</summary>
    public const string Rejected = "rejected";
}
