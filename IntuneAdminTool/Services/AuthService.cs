namespace IntuneAdminTool.Services;

using System.Diagnostics;
using IntuneAdminTool.Properties;
using Microsoft.Identity.Client;

public class AuthService : IAuthService
{
    /// <summary>
    /// Well-known client ID for Microsoft Graph Command Line Tools.
    /// Used as the default when no custom ClientId is provided.
    /// </summary>
    private const string DefaultGraphClientId = "14d82eec-204b-4c2f-b7e8-296a70dab67e";
    private const string DefaultTenantId = "organizations";

    private const string SuccessHtml =
        "<html><head><title>Authentication Complete</title></head>" +
        "<body style=\"font-family:Segoe UI,sans-serif;display:flex;justify-content:center;align-items:center;height:100vh;margin:0;background:#f5f5f5\">" +
        "<div style=\"text-align:center\">" +
        "<h2 style=\"color:#0078D4\">&#x2713; Authentication successful</h2>" +
        "<p>You can close this tab and return to the application.</p>" +
        "</div>" +
        "</body></html>";

    private Process? _browserProcess;

    private string? _clientId;
    private string? _tenantId;

    public string? ClientId
    {
        get => !string.IsNullOrWhiteSpace(_clientId) ? _clientId :
               !string.IsNullOrWhiteSpace(Resources.ClientId) ? Resources.ClientId : DefaultGraphClientId;
        set { _clientId = value; _msalClient = null; }
    }

    public string? TenantId
    {
        get => !string.IsNullOrWhiteSpace(_tenantId) ? _tenantId :
               !string.IsNullOrWhiteSpace(Resources.TenantId) ? Resources.TenantId : DefaultTenantId;
        set { _tenantId = value; _msalClient = null; }
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(TenantId);

    private string Authority => string.Format(Resources.Authority, TenantId);

    private static readonly string[] Scopes = BuildScopes();

    private static string[] BuildScopes()
    {
        var scopes = new List<string>();

        if (!string.IsNullOrWhiteSpace(Resources.ScopeDevices))
            scopes.Add(Resources.ScopeDevices);

        if (!string.IsNullOrWhiteSpace(Resources.ScopeConfiguration))
            scopes.Add(Resources.ScopeConfiguration);

        if (!string.IsNullOrWhiteSpace(Resources.ScopeApps))
            scopes.Add(Resources.ScopeApps);

        return scopes.ToArray();
    }

    private IPublicClientApplication? _msalClient;
    private AuthenticationResult? _authResult;

    public bool IsAuthenticated => _authResult != null;
    public string? UserName => _authResult?.Account?.Username;

    private IPublicClientApplication MsalClient => _msalClient ??= PublicClientApplicationBuilder
        .Create(ClientId)
        .WithAuthority(Authority)
        .WithRedirectUri(Resources.RedirectUri)
        .Build();

    public AuthService()
    {
    }

    public async Task<AuthenticationResult> LoginAsync()
    {
        try
        {
            var accounts = await MsalClient.GetAccountsAsync();
            _authResult = await MsalClient.AcquireTokenSilent(Scopes, accounts.FirstOrDefault())
                .ExecuteAsync();
        }
        catch (MsalUiRequiredException)
        {
            _authResult = await AcquireTokenInteractiveWithBrowserCloseAsync();
        }

        return _authResult;
    }

    public async Task LogoutAsync()
    {
        var accounts = await MsalClient.GetAccountsAsync();
        foreach (var account in accounts)
        {
            await MsalClient.RemoveAsync(account);
        }
        _authResult = null;
    }

    public async Task<string> GetAccessTokenAsync()
    {
        if (_authResult == null)
            throw new InvalidOperationException(Resources.NotAuthenticatedError);

        try
        {
            var accounts = await MsalClient.GetAccountsAsync();
            var result = await MsalClient.AcquireTokenSilent(Scopes, accounts.FirstOrDefault())
                .ExecuteAsync();
            _authResult = result;
            return result.AccessToken;
        }
        catch (MsalUiRequiredException)
        {
            _authResult = await AcquireTokenInteractiveWithBrowserCloseAsync();
            return _authResult.AccessToken;
        }
    }

    private async Task<AuthenticationResult> AcquireTokenInteractiveWithBrowserCloseAsync()
    {
        _browserProcess = null;

        var result = await MsalClient.AcquireTokenInteractive(Scopes)
            .WithUseEmbeddedWebView(false)
            .WithSystemWebViewOptions(new SystemWebViewOptions
            {
                HtmlMessageSuccess = SuccessHtml,
                OpenBrowserAsync = OpenBrowserAndTrackAsync
            })
            .ExecuteAsync();

        // Give the success page a moment to display, then close the browser process
        _ = Task.Delay(1500).ContinueWith(_ => CloseBrowserProcess());

        return result;
    }

    private Task OpenBrowserAndTrackAsync(Uri uri)
    {
        var psi = new ProcessStartInfo
        {
            FileName = uri.AbsoluteUri,
            UseShellExecute = true
        };
        _browserProcess = Process.Start(psi);
        return Task.CompletedTask;
    }

    private void CloseBrowserProcess()
    {
        try
        {
            if (_browserProcess != null && !_browserProcess.HasExited)
            {
                _browserProcess.CloseMainWindow();
                if (!_browserProcess.WaitForExit(2000))
                    _browserProcess.Kill();
            }
        }
        catch { /* Best effort — browser may already be closed */ }
        finally
        {
            _browserProcess = null;
        }
    }
}
