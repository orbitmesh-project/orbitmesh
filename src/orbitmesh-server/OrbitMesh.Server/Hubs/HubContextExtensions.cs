using OrbitMesh.Server;
using Microsoft.AspNetCore.SignalR;

namespace OrbitMesh.Server.Hubs;

public static class HubContextExtensions
{
    /// <summary>
    /// Captures EdgeName/PackageName/AccessKey (and related metadata) from the connection's initial
    /// HTTP request (header or querystring) into <see cref="HubCallerContext.Items"/>, so later hub method
    /// calls on the same connection can read them without re-parsing the request.
    /// </summary>
    public static void CaptureConnectionMetadata(this HubCallerContext context)
    {
        var http = context.GetHttpContext();

        string? Get(string key) => http?.Request.GetHeaderOrQuery(key);

        context.Items[OrbitMeshHeaderNames.EdgeName] = Get(OrbitMeshHeaderNames.EdgeName);
        context.Items[OrbitMeshHeaderNames.PackageName] = Get(OrbitMeshHeaderNames.PackageName);
        context.Items[OrbitMeshHeaderNames.AccessKey] = Get(OrbitMeshHeaderNames.AccessKey);
        context.Items[OrbitMeshHeaderNames.OrbitMeshClientVersion] = Get(OrbitMeshHeaderNames.OrbitMeshClientVersion);
        context.Items[OrbitMeshHeaderNames.OrbitMeshClientType] = Get(OrbitMeshHeaderNames.OrbitMeshClientType);
        context.Items[OrbitMeshHeaderNames.PackageVersion] = Get(OrbitMeshHeaderNames.PackageVersion);
        context.Items[OrbitMeshHeaderNames.IsReconnection] = string.Equals(Get(OrbitMeshHeaderNames.IsReconnection), bool.TrueString, StringComparison.OrdinalIgnoreCase);
        context.Items[OrbitMeshHeaderNames.InstanceId] = Get(OrbitMeshHeaderNames.InstanceId);
    }

    private static object? GetItem(HubCallerContext context, string key) => context.Items.TryGetValue(key, out var value) ? value : null;

    public static string? GetEdgeName(this HubCallerContext context) => GetItem(context, OrbitMeshHeaderNames.EdgeName) as string;

    public static string? GetPackageName(this HubCallerContext context) => GetItem(context, OrbitMeshHeaderNames.PackageName) as string;

    public static string? GetAccessKey(this HubCallerContext context) => GetItem(context, OrbitMeshHeaderNames.AccessKey) as string;

    public static string? GetOrbitMeshClientVersion(this HubCallerContext context) => GetItem(context, OrbitMeshHeaderNames.OrbitMeshClientVersion) as string;

    public static string? GetOrbitMeshClientType(this HubCallerContext context) => GetItem(context, OrbitMeshHeaderNames.OrbitMeshClientType) as string;

    public static string? GetPackageVersion(this HubCallerContext context) => GetItem(context, OrbitMeshHeaderNames.PackageVersion) as string;

    public static bool IsReconnection(this HubCallerContext context) => GetItem(context, OrbitMeshHeaderNames.IsReconnection) as bool? ?? false;

    public static string? GetInstanceId(this HubCallerContext context) => GetItem(context, OrbitMeshHeaderNames.InstanceId) as string;

    public static string GetPackageInstanceId(this HubCallerContext context) => $"{context.GetEdgeName()}/{context.GetPackageName()}";
}
