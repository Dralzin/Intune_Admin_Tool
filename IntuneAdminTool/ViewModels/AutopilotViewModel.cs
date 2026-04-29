namespace IntuneAdminTool.ViewModels;

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IntuneAdminTool.Services;

public partial class AutopilotViewModel : ObservableObject
{
    private readonly IGraphService _graphService;

    [ObservableProperty]
    private ObservableCollection<AutopilotDeviceItem> _autopilotDevices = [];

    [ObservableProperty]
    private ObservableCollection<AutopilotProfileItem> _deploymentProfiles = [];

    [ObservableProperty]
    private ObservableCollection<AutopilotEspItem> _enrollmentStatusPages = [];

    [ObservableProperty]
    private ObservableCollection<AutopilotPrepPolicyItem> _devicePrepPolicies = [];

    [ObservableProperty]
    private AutopilotDeviceItem? _selectedDevice;

    [ObservableProperty]
    private AutopilotProfileItem? _selectedProfile;

    [ObservableProperty]
    private AutopilotEspItem? _selectedEsp;

    [ObservableProperty]
    private AutopilotPrepPolicyItem? _selectedPrepPolicy;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private int _selectedTabIndex;

    [ObservableProperty]
    private string? _deviceSearchText;

    [ObservableProperty]
    private ObservableCollection<ProfileAssignment> _profileAssignments = [];

    [ObservableProperty]
    private string? _profileAssignmentsStatus;

    [ObservableProperty]
    private ObservableCollection<ProfileAssignment> _espAssignments = [];

    [ObservableProperty]
    private string? _espAssignmentsStatus;

    private List<AutopilotDeviceItem> _allAutopilotDevices = [];

    public AutopilotViewModel(IGraphService graphService)
    {
        _graphService = graphService;
    }

    partial void OnDeviceSearchTextChanged(string? value)
    {
        ApplyDeviceFilter();
    }

    private void ApplyDeviceFilter()
    {
        var filtered = _allAutopilotDevices.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(DeviceSearchText))
        {
            filtered = filtered.Where(d =>
                d.SerialNumber?.Contains(DeviceSearchText, StringComparison.OrdinalIgnoreCase) == true ||
                d.DisplayName?.Contains(DeviceSearchText, StringComparison.OrdinalIgnoreCase) == true);
        }

        AutopilotDevices = new ObservableCollection<AutopilotDeviceItem>(filtered);
    }

    partial void OnSelectedProfileChanged(AutopilotProfileItem? value)
    {
        ProfileAssignments = [];
        ProfileAssignmentsStatus = null;
        if (value?.Id != null)
            _ = LoadProfileAssignmentsAsync(value.Id);
    }

    private async Task LoadProfileAssignmentsAsync(string profileId)
    {
        try
        {
            ProfileAssignmentsStatus = "Loading assignments...";
            var assignments = await _graphService.GetAutopilotProfileAssignmentsAsync(profileId);
            ProfileAssignments = new ObservableCollection<ProfileAssignment>(assignments);
            ProfileAssignmentsStatus = assignments.Count == 0 ? "No assignments." : $"{assignments.Count} assignment(s).";
        }
        catch (Exception ex)
        {
            ProfileAssignmentsStatus = $"Failed to load assignments: {ex.Message}";
        }
    }

    partial void OnSelectedEspChanged(AutopilotEspItem? value)
    {
        EspAssignments = [];
        EspAssignmentsStatus = null;
        if (value?.Id != null)
            _ = LoadEspAssignmentsAsync(value.Id);
    }

    private async Task LoadEspAssignmentsAsync(string espId)
    {
        try
        {
            EspAssignmentsStatus = "Loading assignments...";
            var assignments = await _graphService.GetEspAssignmentsAsync(espId);
            EspAssignments = new ObservableCollection<ProfileAssignment>(assignments);
            EspAssignmentsStatus = assignments.Count == 0 ? "No assignments." : $"{assignments.Count} assignment(s).";
        }
        catch (Exception ex)
        {
            EspAssignmentsStatus = $"Failed to load assignments: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task LoadAutopilotAsync()
    {
        try
        {
            IsLoading = true;
            ErrorMessage = null;

            var devicesTask = SafeLoadAsync(_graphService.GetAutopilotDevicesAsync);
            var profilesTask = SafeLoadAsync(_graphService.GetAutopilotDeploymentProfilesAsync);
            var espTask = SafeLoadAsync(_graphService.GetEnrollmentStatusPagesAsync);
            var prepTask = SafeLoadAsync(_graphService.GetDevicePreparationPoliciesAsync);

            await Task.WhenAll(devicesTask, profilesTask, espTask, prepTask);

            _allAutopilotDevices = devicesTask.Result;
            ApplyDeviceFilter();
            DeploymentProfiles = new ObservableCollection<AutopilotProfileItem>(profilesTask.Result);
            EnrollmentStatusPages = new ObservableCollection<AutopilotEspItem>(espTask.Result);
            DevicePrepPolicies = new ObservableCollection<AutopilotPrepPolicyItem>(prepTask.Result);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load Autopilot data: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private static async Task<List<T>> SafeLoadAsync<T>(Func<Task<List<T>>> loader)
    {
        try
        {
            return await loader();
        }
        catch
        {
            return [];
        }
    }
}
