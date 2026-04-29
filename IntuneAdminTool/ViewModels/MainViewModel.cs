namespace IntuneAdminTool.ViewModels;

using System.Runtime.InteropServices;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IntuneAdminTool.Services;

public partial class MainViewModel : ObservableObject
{
    private readonly IAuthService _authService;
    private readonly IGraphService _graphService;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    private const int SW_RESTORE = 9;

    [ObservableProperty]
    private ObservableObject? _currentView;

    [ObservableProperty]
    private bool _isAuthenticated;

    [ObservableProperty]
    private string? _userName;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private bool _isSettingsVisible;

    public DevicesViewModel DevicesViewModel { get; }
    public ComplianceViewModel ComplianceViewModel { get; }
    public ConfigurationViewModel ConfigurationViewModel { get; }
    public AppsViewModel AppsViewModel { get; }
    public AutopilotViewModel AutopilotViewModel { get; }
    public WindowsUpdatesViewModel WindowsUpdatesViewModel { get; }
    public ReportsViewModel ReportsViewModel { get; }
    public SettingsViewModel SettingsViewModel { get; }

    public MainViewModel(IAuthService authService, IGraphService graphService)
    {
        _authService = authService;
        _graphService = graphService;

        DevicesViewModel = new DevicesViewModel(graphService);
        ComplianceViewModel = new ComplianceViewModel(graphService);
        ConfigurationViewModel = new ConfigurationViewModel(graphService);
        AppsViewModel = new AppsViewModel(graphService);
        AutopilotViewModel = new AutopilotViewModel(graphService);
        WindowsUpdatesViewModel = new WindowsUpdatesViewModel(graphService);
        ReportsViewModel = new ReportsViewModel(graphService);
        SettingsViewModel = new SettingsViewModel(authService);

        StatusMessage = "Please sign in to continue.";
    }

    [RelayCommand]
    private async Task LoginAsync()
    {
        try
        {
            StatusMessage = "Signing in...";
            await _authService.LoginAsync();
            IsAuthenticated = true;
            UserName = _authService.UserName;
            StatusMessage = $"Signed in as {UserName}";
            BringAppToForeground();
            NavigateToDevices();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Sign-in failed: {ex.Message}";
        }
    }

    private static void BringAppToForeground()
    {
        var mainWindow = System.Windows.Application.Current.MainWindow;
        if (mainWindow == null) return;

        var handle = new System.Windows.Interop.WindowInteropHelper(mainWindow).Handle;
        ShowWindow(handle, SW_RESTORE);
        SetForegroundWindow(handle);
        mainWindow.Activate();
    }

    [RelayCommand]
    private async Task LogoutAsync()
    {
        await _authService.LogoutAsync();
        IsAuthenticated = false;
        UserName = null;
        CurrentView = null;
        StatusMessage = "Signed out. Please sign in to continue.";
    }

    [RelayCommand]
    private void NavigateToDevices()
    {
        CurrentView = DevicesViewModel;
        _ = DevicesViewModel.LoadDevicesCommand.ExecuteAsync(null);
    }

    [RelayCommand]
    private void NavigateToCompliance()
    {
        CurrentView = ComplianceViewModel;
        _ = ComplianceViewModel.LoadComplianceCommand.ExecuteAsync(null);
    }

    [RelayCommand]
    private void NavigateToConfiguration()
    {
        CurrentView = ConfigurationViewModel;
        _ = ConfigurationViewModel.LoadConfigurationsCommand.ExecuteAsync(null);
    }

    [RelayCommand]
    private void NavigateToApps()
    {
        CurrentView = AppsViewModel;
        _ = AppsViewModel.LoadAppsCommand.ExecuteAsync(null);
    }

    [RelayCommand]
    private void NavigateToAutopilot()
    {
        CurrentView = AutopilotViewModel;
        _ = AutopilotViewModel.LoadAutopilotCommand.ExecuteAsync(null);
    }

    [RelayCommand]
    private void NavigateToWindowsUpdates()
    {
        CurrentView = WindowsUpdatesViewModel;
        _ = WindowsUpdatesViewModel.LoadWindowsUpdatesCommand.ExecuteAsync(null);
    }

    [RelayCommand]
    private void NavigateToReports()
    {
        CurrentView = ReportsViewModel;
    }

    [RelayCommand]
    private void NavigateToSettings()
    {
        SettingsViewModel.RefreshSettings();
        CurrentView = SettingsViewModel;
    }

    [RelayCommand]
    private void ShowSettings()
    {
        SettingsViewModel.RefreshSettings();
        IsSettingsVisible = true;
    }

    [RelayCommand]
    private void HideSettings()
    {
        IsSettingsVisible = false;
    }
}
