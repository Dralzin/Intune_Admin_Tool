namespace IntuneAdminTool.ViewModels;

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IntuneAdminTool.Services;
using Microsoft.Graph.Models;

public partial class ConfigurationViewModel : ObservableObject
{
    private readonly IGraphService _graphService;

    [ObservableProperty]
    private ObservableCollection<ConfigurationProfileItem> _configurations = [];

    [ObservableProperty]
    private ConfigurationProfileItem? _selectedConfiguration;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string? _searchText;

    [ObservableProperty]
    private string? _selectedPlatformFilter = "All";

    [ObservableProperty]
    private string? _selectedProfileTypeFilter = "All";

    public ObservableCollection<string> PlatformFilterOptions { get; } = ["All"];

    public ObservableCollection<string> ProfileTypeFilterOptions { get; } = ["All"];

    [ObservableProperty]
    private ObservableCollection<PolicySetting> _policySettings = [];

    [ObservableProperty]
    private bool _isLoadingSettings;

    [ObservableProperty]
    private string? _settingsStatusMessage;

    [ObservableProperty]
    private ObservableCollection<ProfileAssignment> _profileAssignments = [];

    [ObservableProperty]
    private string? _assignmentsStatusMessage;

    private List<ConfigurationProfileItem> _allConfigurations = [];

    public ConfigurationViewModel(IGraphService graphService)
    {
        _graphService = graphService;
    }

    [RelayCommand]
    private async Task LoadConfigurationsAsync()
    {
        try
        {
            IsLoading = true;
            ErrorMessage = null;

            var deviceConfigsTask = _graphService.GetDeviceConfigurationsAsync();
            var gpConfigsTask = _graphService.GetAdministrativeTemplatesAsync();
            var scPoliciesTask = _graphService.GetSettingsCatalogPoliciesAsync();
            var esPoliciesTask = _graphService.GetEndpointSecurityPoliciesAsync();
            await Task.WhenAll(deviceConfigsTask, gpConfigsTask, scPoliciesTask, esPoliciesTask);

            var items = new List<ConfigurationProfileItem>();

            foreach (var dc in deviceConfigsTask.Result)
                items.Add(new ConfigurationProfileItem(dc));

            foreach (var gp in gpConfigsTask.Result)
                items.Add(new ConfigurationProfileItem(gp));

            foreach (var sc in scPoliciesTask.Result)
                items.Add(new ConfigurationProfileItem(sc));

            foreach (var es in esPoliciesTask.Result)
                items.Add(new ConfigurationProfileItem(es));

            _allConfigurations = items.OrderBy(c => c.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
            UpdatePlatformFilterOptions();
            UpdateProfileTypeFilterOptions();
            ApplyFilter();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load configurations: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void Search()
    {
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var filtered = _allConfigurations.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(SelectedPlatformFilter) && SelectedPlatformFilter != "All")
        {
            filtered = filtered.Where(c =>
                MatchesPlatformFilter(c.Platform, SelectedPlatformFilter));
        }

        if (!string.IsNullOrWhiteSpace(SelectedProfileTypeFilter) && SelectedProfileTypeFilter != "All")
        {
            filtered = filtered.Where(c =>
                string.Equals(c.ProfileType, SelectedProfileTypeFilter, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            filtered = filtered.Where(c =>
                c.DisplayName?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) == true ||
                c.Description?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) == true ||
                c.ProfileType?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) == true ||
                c.Platform?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) == true);
        }

        Configurations = new ObservableCollection<ConfigurationProfileItem>(filtered);
    }

    partial void OnSelectedPlatformFilterChanged(string? value)
    {
        ApplyFilter();
    }

    partial void OnSelectedProfileTypeFilterChanged(string? value)
    {
        ApplyFilter();
    }

    private void UpdateProfileTypeFilterOptions()
    {
        var types = _allConfigurations
            .Select(c => c.ProfileType)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(t => t)
            .ToList();

        ProfileTypeFilterOptions.Clear();
        ProfileTypeFilterOptions.Add("All");
        foreach (var t in types)
            ProfileTypeFilterOptions.Add(t!);
    }

    private void UpdatePlatformFilterOptions()
    {
        var platforms = _allConfigurations
            .Select(c => NormalizePlatform(c.Platform))
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p)
            .ToList();

        PlatformFilterOptions.Clear();
        PlatformFilterOptions.Add("All");
        foreach (var p in platforms)
            PlatformFilterOptions.Add(p!);
    }

    private static string? NormalizePlatform(string? platform)
    {
        if (string.IsNullOrWhiteSpace(platform)) return null;
        // Treat "windows10", "Windows10", etc. as "Windows"
        if (platform.StartsWith("windows", StringComparison.OrdinalIgnoreCase))
            return "Windows";
        return platform;
    }

    private static bool MatchesPlatformFilter(string? platform, string filter)
    {
        if (string.IsNullOrWhiteSpace(platform)) return false;
        var normalized = NormalizePlatform(platform);
        return string.Equals(normalized, filter, StringComparison.OrdinalIgnoreCase);
    }

    partial void OnSearchTextChanged(string? value)
    {
        ApplyFilter();
    }

    partial void OnSelectedConfigurationChanged(ConfigurationProfileItem? value)
    {
        PolicySettings = [];
        SettingsStatusMessage = null;
        ProfileAssignments = [];
        AssignmentsStatusMessage = null;
        if (value != null)
        {
            _ = LoadPolicySettingsAsync(value);
            _ = LoadProfileAssignmentsAsync(value);
        }
    }

    private async Task LoadPolicySettingsAsync(ConfigurationProfileItem profile)
    {
        if (profile.Id == null) return;

        try
        {
            IsLoadingSettings = true;
            SettingsStatusMessage = "Loading policy settings...";

            List<PolicySetting> settings = profile.Source switch
            {
                ConfigProfileSource.DeviceConfiguration => await _graphService.GetDeviceConfigurationSettingsAsync(profile.Id),
                ConfigProfileSource.AdministrativeTemplate => await _graphService.GetAdministrativeTemplateSettingsAsync(profile.Id),
                ConfigProfileSource.SettingsCatalog => await _graphService.GetSettingsCatalogSettingsAsync(profile.Id),
                ConfigProfileSource.EndpointSecurity => await _graphService.GetEndpointSecuritySettingsAsync(profile.Id),
                _ => []
            };

            PolicySettings = new ObservableCollection<PolicySetting>(settings);
            SettingsStatusMessage = settings.Count == 0 ? "No policy settings found." : $"{settings.Count} setting(s) loaded.";
        }
        catch (Exception ex)
        {
            SettingsStatusMessage = $"Failed to load settings: {ex.Message}";
        }
        finally
        {
            IsLoadingSettings = false;
        }
    }

    private async Task LoadProfileAssignmentsAsync(ConfigurationProfileItem profile)
    {
        if (profile.Id == null) return;

        try
        {
            AssignmentsStatusMessage = "Loading assignments...";
            var assignments = await _graphService.GetProfileAssignmentsAsync(profile.Id, profile.Source);
            ProfileAssignments = new ObservableCollection<ProfileAssignment>(assignments);
            AssignmentsStatusMessage = assignments.Count == 0 ? "No assignments." : $"{assignments.Count} assignment(s).";
        }
        catch (Exception ex)
        {
            AssignmentsStatusMessage = $"Failed to load assignments: {ex.Message}";
        }
    }
}

public enum ConfigProfileSource
{
    DeviceConfiguration,
    AdministrativeTemplate,
    SettingsCatalog,
    EndpointSecurity
}

public class ConfigurationProfileItem
{
    public ConfigurationProfileItem(DeviceConfiguration deviceConfig)
    {
        Id = deviceConfig.Id;
        DisplayName = deviceConfig.DisplayName;
        Description = deviceConfig.Description;
        CreatedDateTime = deviceConfig.CreatedDateTime;
        LastModifiedDateTime = deviceConfig.LastModifiedDateTime;
        Version = deviceConfig.Version;
        ProfileType = FormatOdataType(deviceConfig.OdataType);
        Platform = InferPlatform(deviceConfig.OdataType);
        Source = ConfigProfileSource.DeviceConfiguration;
    }

    public ConfigurationProfileItem(AdministrativeTemplateProfile gpConfig)
    {
        Id = gpConfig.Id;
        DisplayName = gpConfig.DisplayName;
        Description = gpConfig.Description;
        CreatedDateTime = gpConfig.CreatedDateTime;
        LastModifiedDateTime = gpConfig.LastModifiedDateTime;
        ProfileType = "Administrative Templates";
        Platform = "Windows";
        Source = ConfigProfileSource.AdministrativeTemplate;
    }

    public ConfigurationProfileItem(SettingsCatalogProfile scPolicy)
    {
        Id = scPolicy.Id;
        DisplayName = scPolicy.Name;
        Description = scPolicy.Description;
        CreatedDateTime = scPolicy.CreatedDateTime;
        LastModifiedDateTime = scPolicy.LastModifiedDateTime;
        ProfileType = "Settings Catalog";
        Platform = scPolicy.Platforms ?? "Unknown";
        Source = ConfigProfileSource.SettingsCatalog;
        Technologies = scPolicy.Technologies;
    }

    public ConfigurationProfileItem(EndpointSecurityProfile esPolicy)
    {
        Id = esPolicy.Id;
        DisplayName = esPolicy.DisplayName;
        Description = esPolicy.Description;
        CreatedDateTime = esPolicy.CreatedDateTime;
        LastModifiedDateTime = esPolicy.LastModifiedDateTime;
        ProfileType = "Endpoint Security";
        Platform = "Windows";
        Source = ConfigProfileSource.EndpointSecurity;
    }

    public string? Id { get; }
    public string? DisplayName { get; }
    public string? Description { get; }
    public DateTimeOffset? CreatedDateTime { get; }
    public DateTimeOffset? LastModifiedDateTime { get; }
    public int? Version { get; }
    public string? ProfileType { get; }
    public string? Platform { get; }
    public ConfigProfileSource Source { get; }
    public string? Technologies { get; }

    private static string FormatOdataType(string? odataType)
    {
        if (string.IsNullOrEmpty(odataType)) return "Unknown";
        var name = odataType.Split('.').LastOrDefault() ?? odataType;
        var spaced = System.Text.RegularExpressions.Regex.Replace(name, "([a-z])([A-Z])", "$1 $2");
        return char.ToUpper(spaced[0]) + spaced[1..];
    }

    private static string InferPlatform(string? odataType)
    {
        if (string.IsNullOrEmpty(odataType)) return "Unknown";
        var lower = odataType.ToLowerInvariant();
        if (lower.Contains("windows10") || lower.Contains("windows81") || lower.Contains("windowsphone")) return "Windows";
        if (lower.Contains("ios") || lower.Contains("iphone")) return "iOS/iPadOS";
        if (lower.Contains("macos") || lower.Contains("mac")) return "macOS";
        if (lower.Contains("android")) return "Android";
        return "Cross-platform";
    }
}
