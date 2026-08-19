using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace OrbitMesh.Server.Services;

/// <summary>
/// Per-endpoint authorization for ManagementController actions - the credential resolved from the
/// request's AccessKey must hold at least one of the listed scopes (see OrbitMeshScope). This is the
/// fine-grained layer beneath AccessKeyAuthenticationMiddleware's coarse "is this credential
/// Management-capable at all" gate (IOrbitMeshDirectory.CanAccess): the middleware decides whether the
/// request reaches this controller at all, this attribute decides whether it reaches this specific
/// action. Resolves services from HttpContext.RequestServices rather than constructor injection so it
/// can be used as a plain attribute (`[RequiresScope(...)]`) instead of `[TypeFilter]`.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class RequiresScopeAttribute(params string[] anyOf) : Attribute, IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var directory = context.HttpContext.RequestServices.GetRequiredService<IOrbitMeshDirectory>();
        var accessKey = context.HttpContext.Request.GetHeaderOrQuery(OrbitMeshHeaderNames.AccessKey);
        var credential = directory.FindCredential(accessKey);
        if (credential == null || !anyOf.Any(credential.Scopes.Contains))
        {
            context.Result = new ObjectResult($"Forbidden: this action requires the '{string.Join("' or '", anyOf)}' scope.")
            {
                StatusCode = StatusCodes.Status403Forbidden
            };
            return;
        }
        await next();
    }
}
