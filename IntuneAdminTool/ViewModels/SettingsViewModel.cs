namespace IntuneAdminTool.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IntuneAdminTool.Services;

public partial class SettingsViewModel : ObservableObject
{
    private readonly IAuthService _authService;

    // Track user-entered values (null means not explicitly set)
    private string? _userClientId;
    private string? _userTenantId;

    [ObservableProperty]
    private string? _clientId;

    [ObservableProperty]
    private string? _tenantId;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private bool _isLocked;

    public SettingsViewModel(IAuthService authService)
    {
        _authService = authService;
        RefreshSettings();
    }

    public void RefreshSettings()
    {
        IsLocked = _authService.IsAuthenticated;
        if (_authService.IsAuthenticated)
        {
            // When signed in, show the active ClientId and TenantId (including defaults)
            ClientId = _authService.ClientId;
            TenantId = _authService.TenantId;
        }
        else
        {
            // When not signed in, show empty unless the user has explicitly set a value
            ClientId = _userClientId;
            TenantId = _userTenantId;
        }
    }

    [RelayCommand]
    private void SaveSettings()
    {
        if (IsLocked) return;
        _userClientId = string.IsNullOrWhiteSpace(ClientId) ? null : ClientId;
        _userTenantId = string.IsNullOrWhiteSpace(TenantId) ? null : TenantId;
        _authService.ClientId = _userClientId;
        _authService.TenantId = _userTenantId;
        StatusMessage = "Settings saved. Changes will take effect on next sign-in.";
    }

    [RelayCommand]
    private void ResetToDefaults()
    {
        if (IsLocked) return;
        _userClientId = null;
        _userTenantId = null;
        _authService.ClientId = null;
        _authService.TenantId = null;
        RefreshSettings();
        StatusMessage = "Settings reset to defaults.";
    }
}
