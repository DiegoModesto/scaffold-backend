using System.Security.Claims;
using Yarp.ReverseProxy.Transforms;
using Yarp.ReverseProxy.Transforms.Builder;

namespace Gateway.Authentication;

internal sealed class ForwardedIdentityTransform : ITransformProvider
{
    public void ValidateRoute(TransformRouteValidationContext context) { }
    public void ValidateCluster(TransformClusterValidationContext context) { }

    public void Apply(TransformBuilderContext context)
    {
        context.AddRequestTransform(transformContext =>
        {
            var user = transformContext.HttpContext.User;
            if (user.Identity?.IsAuthenticated != true)
            {
                return ValueTask.CompletedTask;
            }

            var sub = user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
            var tenantId = user.FindFirstValue("tenant_id");

            if (!string.IsNullOrEmpty(sub))
            {
                transformContext.ProxyRequest.Headers.Remove("X-Forwarded-User");
                transformContext.ProxyRequest.Headers.Add("X-Forwarded-User", sub);
            }
            if (!string.IsNullOrEmpty(tenantId))
            {
                transformContext.ProxyRequest.Headers.Remove("X-Forwarded-TenantId");
                transformContext.ProxyRequest.Headers.Add("X-Forwarded-TenantId", tenantId);
            }

            return ValueTask.CompletedTask;
        });
    }
}
