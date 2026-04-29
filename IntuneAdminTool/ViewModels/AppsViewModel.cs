namespace IntuneAdminTool.ViewModels;

using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IntuneAdminTool.Services;
using Microsoft.Graph.Models;

public partial class AppsViewModel : ObservableObject
{
    private readonly IGraphService _graphService;

    [ObservableProperty]
    private ObservableCollection<AppDisplayItem> _apps = [];

    [ObservableProperty]
    private AppDisplayItem? _selectedApp;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string? _searchText;

    [ObservableProperty]
    private string? _selectedTypeFilter = "All";

    [ObservableProperty]
    private string? _selectedOsFilter = "All";

    public ObservableCollection<string> TypeFilterOptions { get; } = ["All"];

    public ObservableCollection<string> OsFilterOptions { get; } = ["All"];

    [ObservableProperty]
    private BitmapImage? _appLogo;

    [ObservableProperty]
    private ObservableCollection<ProfileAssignment> _appAssignments = [];

    [ObservableProperty]
    private string? _assignmentsStatusMessage;

    private List<AppDisplayItem> _allApps = [];

    public AppsViewModel(IGraphService graphService)
    {
        _graphService = graphService;
    }

    [RelayCommand]
    private async Task LoadAppsAsync()
    {
        try
        {
            IsLoading = true;
            ErrorMessage = null;

            var appsTask = _graphService.GetMobileAppsAsync();
            var assignmentTask = _graphService.GetMobileAppAssignmentInfoAsync();
            await Task.WhenAll(appsTask, assignmentTask);

            var assignmentLookup = assignmentTask.Result
                .Where(a => a.Id != null)
                .ToDictionary(a => a.Id!, a => a);

            _allApps = appsTask.Result.Select(app =>
            {
                AppAssignmentInfo? info = null;
                var hasInfo = app.Id != null && assignmentLookup.TryGetValue(app.Id, out info);
                var isAssigned = hasInfo && info!.IsAssigned;
                var betaType = hasInfo ? info!.OdataType : null;
                var packageId = hasInfo ? info!.PackageIdentifier : null;
                return new AppDisplayItem(app, isAssigned, betaType, packageId);
            }).ToList();

            UpdateTypeFilterOptions();
            UpdateOsFilterOptions();
            ApplyFilter();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load applications: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void ApplyFilter()
    {
        var filtered = _allApps.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(SelectedOsFilter) && SelectedOsFilter != "All")
        {
            filtered = filtered.Where(a =>
                string.Equals(a.AppOs, SelectedOsFilter, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(SelectedTypeFilter) && SelectedTypeFilter != "All")
        {
            filtered = filtered.Where(a =>
                string.Equals(a.AppType, SelectedTypeFilter, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            filtered = filtered.Where(a =>
                a.DisplayName?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) == true);
        }

        Apps = new ObservableCollection<AppDisplayItem>(filtered);
    }

    partial void OnSearchTextChanged(string? value)
    {
        ApplyFilter();
    }

    partial void OnSelectedTypeFilterChanged(string? value)
    {
        ApplyFilter();
    }

    partial void OnSelectedOsFilterChanged(string? value)
    {
        ApplyFilter();
    }

    private void UpdateOsFilterOptions()
    {
        var platforms = _allApps
            .Select(a => a.AppOs)
            .Where(o => !string.IsNullOrWhiteSpace(o))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(o => o)
            .ToList();

        OsFilterOptions.Clear();
        OsFilterOptions.Add("All");
        foreach (var o in platforms)
            OsFilterOptions.Add(o);
    }

    private void UpdateTypeFilterOptions()
    {
        var types = _allApps
            .Select(a => a.AppType)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(t => t)
            .ToList();

        TypeFilterOptions.Clear();
        TypeFilterOptions.Add("All");
        foreach (var t in types)
            TypeFilterOptions.Add(t);
    }

    partial void OnSelectedAppChanged(AppDisplayItem? value)
    {
        AppLogo = null;
        AppAssignments = [];
        AssignmentsStatusMessage = null;
        if (value?.Id != null)
            _ = LoadAppDetailsAsync(value.Id);
    }

    private async Task LoadAppDetailsAsync(string appId)
    {
        // Load logo and assignments in parallel
        var logoTask = LoadAppLogoAsync(appId);
        var assignmentsTask = LoadAppAssignmentsAsync(appId);
        await Task.WhenAll(logoTask, assignmentsTask);
    }

    private async Task LoadAppLogoAsync(string appId)
    {
        try
        {
            var logoBytes = await _graphService.GetMobileAppLogoAsync(appId);
            if (logoBytes != null && logoBytes.Length > 0)
            {
                var bitmap = new BitmapImage();
                using var ms = new MemoryStream(logoBytes);
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = ms;
                bitmap.EndInit();
                bitmap.Freeze();
                AppLogo = bitmap;
            }
        }
        catch { /* Logo not available */ }
    }

    private async Task LoadAppAssignmentsAsync(string appId)
    {
        try
        {
            AssignmentsStatusMessage = "Loading assignments...";
            var assignments = await _graphService.GetMobileAppAssignmentsAsync(appId);
            AppAssignments = new ObservableCollection<ProfileAssignment>(assignments);
            AssignmentsStatusMessage = assignments.Count == 0 ? "No assignments." : $"{assignments.Count} assignment(s).";
        }
        catch (Exception ex)
        {
            AssignmentsStatusMessage = $"Failed to load assignments: {ex.Message}";
        }
    }
}

public class AppDisplayItem
{
    private readonly MobileApp _app;

    public AppDisplayItem(MobileApp app, bool isAssigned, string? betaOdataType = null, string? packageIdentifier = null)
    {
        _app = app;
        IsAssigned = isAssigned ? "Yes" : "No";
        // If the app has a packageIdentifier, it's a Microsoft Store (New) app
        // regardless of whether the API types it as win32LobApp or winGetApp
        var effectiveType = betaOdataType ?? app.OdataType;
        if (!string.IsNullOrEmpty(packageIdentifier) && effectiveType?.Contains("win32LobApp") == true)
            effectiveType = "#microsoft.graph.winGetApp";
        AppType = FormatAppType(effectiveType);
        AppOs = InferOs(effectiveType);
    }

    public string? Id => _app.Id;
    public string? DisplayName => _app.DisplayName;
    public string? Description => _app.Description;
    public string? Publisher => _app.Publisher;
    public DateTimeOffset? CreatedDateTime => _app.CreatedDateTime;
    public DateTimeOffset? LastModifiedDateTime => _app.LastModifiedDateTime;
    public MobileAppPublishingState? PublishingState => _app.PublishingState;
    public string IsAssigned { get; }
    public string AppType { get; }
    public string AppOs { get; }

    private static string FormatAppType(string? odataType)
    {
        if (string.IsNullOrEmpty(odataType)) return "Unknown";
        var name = odataType.Split('.').LastOrDefault() ?? odataType;
        return name switch
        {
            "win32LobApp" => "Windows app (Win32)",
            "windowsMobileMSI" => "Line-of-business app",
            "microsoftStoreForBusinessApp" => "Microsoft Store app (legacy)",
            "winGetApp" => "New Microsoft Store app",
            "officeSuiteApp" => "Microsoft 365 Apps for Windows 10 and later",
            "webApp" => "Web link",
            "iosStoreApp" => "iOS store app",
            "iosVppApp" => "iOS/iPadOS app (VPP)",
            "iosLobApp" => "iOS/iPadOS line-of-business app",
            "androidStoreApp" => "Android store app",
            "androidManagedStoreApp" => "Managed Google Play app",
            "androidForWorkApp" => "Managed Google Play app",
            "managedIOSStoreApp" => "Managed iOS store app",
            "managedAndroidStoreApp" => "Managed Android store app",
            "managedAndroidLobApp" => "Android line-of-business app",
            "windowsUniversalAppX" => "Universal Windows Platform app",
            "windowsAppX" => "Windows app (AppX)",
            "windowsMicrosoftEdgeApp" => "Microsoft Edge for Windows 10 and later",
            "windowsPhone81AppX" => "Windows Phone 8.1 app",
            "windowsStoreApp" => "Microsoft Store app",
            "macOSLobApp" => "macOS line-of-business app",
            "macOSDmgApp" => "macOS app (DMG)",
            "macOSPkgApp" => "macOS app (PKG)",
            "macOSMicrosoftEdgeApp" => "Microsoft Edge for macOS",
            "macOSMicrosoftDefenderApp" => "Microsoft Defender for macOS",
            "macOSOfficeSuiteApp" => "Microsoft 365 Apps for macOS",
            "macOSVppApp" => "macOS app (VPP)",
            "androidLobApp" => "Android line-of-business app",
            "windowsWebApp" => "Windows web app",
            "windowsMicrosoftDefenderApp" => "Microsoft Defender for Windows",
            _ => System.Text.RegularExpressions.Regex.Replace(name, "([a-z])([A-Z])", "$1 $2")
        };
    }

    private static string InferOs(string? odataType)
    {
        if (string.IsNullOrEmpty(odataType)) return "Other";
        var name = odataType.Split('.').LastOrDefault() ?? odataType;
        var lower = name.ToLowerInvariant();
        if (lower.Contains("ios") || lower.Contains("ipad")) return "iOS/iPadOS";
        if (lower.Contains("macos")) return "macOS";
        if (lower.Contains("android")) return "Android";
        if (lower.Contains("windows") || lower.Contains("win32") || lower.Contains("winget") || lower.Contains("office")) return "Windows";
        if (lower.Contains("webapp") || lower == "webapp") return "Cross-platform";
        return "Other";
    }
}
