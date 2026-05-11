namespace IIoT.HttpApi.Infrastructure.Oidc;

public static class CloudOidcDefaults
{
    public const string SessionScheme = "CloudOidcSession";
    public const string LoginAuditOperation = "CloudOidcLoginSession";
    public const string AuthorizeAuditOperation = "CloudOidcAuthorize";
    public const string TokenAuditOperation = "CloudOidcToken";
    public const string UserInfoAuditOperation = "CloudOidcUserInfo";
    public const string LogoutAuditOperation = "CloudOidcLogout";
}
