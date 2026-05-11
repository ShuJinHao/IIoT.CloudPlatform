namespace IIoT.HttpApi.Infrastructure.Oidc;

public interface ICloudOidcSessionService
{
    Task SignInAsync(
        HttpContext httpContext,
        string employeeNo,
        CancellationToken cancellationToken = default);

    Task SignOutAsync(HttpContext httpContext);
}
