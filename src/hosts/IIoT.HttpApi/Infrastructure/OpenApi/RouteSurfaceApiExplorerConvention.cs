using Microsoft.AspNetCore.Mvc.ApplicationModels;

namespace IIoT.HttpApi.Infrastructure.OpenApi;

internal sealed class RouteSurfaceApiExplorerConvention : IApplicationModelConvention
{
    public void Apply(ApplicationModel application)
    {
        foreach (var controller in application.Controllers)
        {
            foreach (var action in controller.Actions)
            {
                action.ApiExplorer.GroupName = ResolveGroupName(
                    controllerRoute: controller.Selectors
                        .Select(selector => selector.AttributeRouteModel?.Template)
                        .FirstOrDefault(template => !string.IsNullOrWhiteSpace(template))
                        ?? string.Empty,
                    controllerName: controller.ControllerName,
                    actionName: action.ActionMethod.Name);
            }
        }
    }

    internal static string ResolveGroupName(
        string controllerRoute,
        string controllerName,
        string actionName)
    {
        if (string.Equals(controllerName, "HumanIdentity", StringComparison.Ordinal)
            && string.Equals(actionName, "EdgeLogin", StringComparison.Ordinal))
        {
            return "bootstrap";
        }

        if (MatchesSurface(controllerRoute, "api/v1/edge/bootstrap"))
            return "bootstrap";

        if (MatchesSurface(controllerRoute, "api/v1/ai/read"))
            return "ai-read";

        if (MatchesSurface(controllerRoute, "api/v1/machine"))
            return "machine";

        if (MatchesSurface(controllerRoute, "api/v1/edge"))
            return "edge";

        return "human";
    }

    private static bool MatchesSurface(string controllerRoute, string surfacePrefix) =>
        string.Equals(controllerRoute, surfacePrefix, StringComparison.OrdinalIgnoreCase)
        || controllerRoute.StartsWith($"{surfacePrefix}/", StringComparison.OrdinalIgnoreCase);
}
