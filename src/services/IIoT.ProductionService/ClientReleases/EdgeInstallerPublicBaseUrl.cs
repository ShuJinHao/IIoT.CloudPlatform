namespace IIoT.ProductionService.ClientReleases;

internal static class EdgeInstallerPublicBaseUrl
{
    public const string ValidationMessage =
        "云端地址必须填写为 Gateway 公开访问 origin，例如 http://cloud-host:81。不要包含 /api/v1、路径、查询或片段。";

    public static bool TryNormalize(string? value, out string normalized, out string error)
    {
        normalized = string.Empty;
        error = string.Empty;

        var candidate = value?.Trim();
        if (string.IsNullOrWhiteSpace(candidate))
        {
            error = ValidationMessage;
            return false;
        }

        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            error = ValidationMessage;
            return false;
        }

        if (!string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            error = ValidationMessage;
            return false;
        }

        if (!string.IsNullOrEmpty(uri.AbsolutePath) && uri.AbsolutePath != "/")
        {
            error = ValidationMessage;
            return false;
        }

        normalized = uri.GetLeftPart(UriPartial.Authority).TrimEnd('/');
        return true;
    }
}
