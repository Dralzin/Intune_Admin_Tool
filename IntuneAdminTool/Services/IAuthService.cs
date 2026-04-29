namespace IntuneAdminTool.Services;

using Microsoft.Identity.Client;

public interface IAuthService
{
    Task<AuthenticationResult> LoginAsync();
    Task LogoutAsync();
    Task<string> GetAccessTokenAsync();
    bool IsAuthenticated { get; }
    bool IsConfigured { get; }
    string? UserName { get; }
    string? ClientId { get; set; }
    string? TenantId { get; set; }
}
