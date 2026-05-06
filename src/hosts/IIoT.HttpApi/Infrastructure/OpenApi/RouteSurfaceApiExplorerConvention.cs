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
                action.ApiExplorer.GroupName = ResolveGroupName(controller, action);
            }
        }
    }

    private static string ResolveGroupName(ControllerModel controller, ActionModel action)
    {
        var controllerRoute = controller.Selectors
            .Select(selector => selector.AttributeRouteModel?.Template)
            .FirstOrDefault(template => !string.IsNullOrWhiteSpace(template))
            ?? string.Empty;

        if (string.Equals(controller.ControllerName, "HumanIdentity", StringComparison.Ordinal)
            && string.Equals(action.ActionMethod.Name, "EdgeLogin", StringComparison.Ordinal))
        {
            return "bootstrap";
        }

        if (controllerRoute.StartsWith("api/v1/edge/bootstrap", StringComparison.OrdinalIgnoreCase))
            return "bootstrap";

        if (controllerRoute.StartsWith("api/v1/ai/read/", StringComparison.OrdinalIgnoreCase))
            return "ai-read";

        if (controllerRoute.StartsWith("api/v1/edge/", StringComparison.OrdinalIgnoreCase))
            return "edge";

        return "human";
    }
}
