namespace IntuneAdminTool.ViewModels;

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IntuneAdminTool.Services;
using Microsoft.Graph.Models;

public partial class DevicesViewModel : ObservableObject
{
    private readonly IGraphService _graphService;

    [ObservableProperty]
    private ObservableCollection<DeviceDisplayItem> _devices = [];

    [ObservableProperty]
    private DeviceDisplayItem? _selectedDevice;

    [ObservableProperty]
    private string? _searchText;

    [ObservableProperty]
    private string? _selectedOsFilter = "All";

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _isEditing;

    [ObservableProperty]
    private bool _isSaving;

    [ObservableProperty]
    private string? _editStatusMessage;

    [ObservableProperty]
    private string? _assignUserUpn;

    [ObservableProperty]
    private string? _userAssignmentStatus;

    [ObservableProperty]
    private bool _isAssigning;

    [ObservableProperty]
    private ObservableCollection<string> _currentOwners = [];

    [ObservableProperty]
    private ObservableCollection<string> _currentRegisteredUsers = [];

    [ObservableProperty]
    private string? _selectedOwner;

    [ObservableProperty]
    private string? _selectedRegisteredUser;

    [ObservableProperty]
    private bool _isUserAssignmentVisible;

    public ObservableCollection<string> OsFilterOptions { get; } = ["All"];

    public ObservableCollection<ManagedDeviceOwnerType> OwnershipOptions { get; } =
    [
        Microsoft.Graph.Models.ManagedDeviceOwnerType.Company,
        Microsoft.Graph.Models.ManagedDeviceOwnerType.Personal,
        Microsoft.Graph.Models.ManagedDeviceOwnerType.Unknown
    ];

    [ObservableProperty]
    private ObservableCollection<DeviceCategory> _deviceCategories = [];

    private List<DeviceDisplayItem> _allDevices = [];

    public DevicesViewModel(IGraphService graphService)
    {
        _graphService = graphService;
    }

    [RelayCommand]
    private async Task LoadDevicesAsync()
    {
        try
        {
            IsLoading = true;
            ErrorMessage = null;

            // Fetch devices and users concurrently
            var devicesTask = _graphService.GetManagedDevicesAsync();
            var usersTask = _graphService.GetUsersAsync();
            await Task.WhenAll(devicesTask, usersTask);

            var managedDevices = devicesTask.Result;
            var users = usersTask.Result;

            // Process mapping on a background thread
            _allDevices = await Task.Run(() =>
            {
                var userLookup = users.ToDictionary(
                    u => u.UserPrincipalName ?? string.Empty, u => u, StringComparer.OrdinalIgnoreCase);

                return managedDevices.Select(d =>
                {
                    string? employeeId = null;
                    if (!string.IsNullOrEmpty(d.UserPrincipalName) && userLookup.TryGetValue(d.UserPrincipalName, out var user))
                    {
                        employeeId = user.EmployeeId;
                    }
                    return new DeviceDisplayItem(d, employeeId);
                }).ToList();
            });

            UpdateOsFilterOptions();
            ApplyFilter();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load devices: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void UpdateOsFilterOptions()
    {
        var osValues = _allDevices
            .Select(d => d.OperatingSystem)
            .Where(os => !string.IsNullOrWhiteSpace(os))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(os => os)
            .ToList();

        OsFilterOptions.Clear();
        OsFilterOptions.Add("All");
        foreach (var os in osValues)
            OsFilterOptions.Add(os!);
    }

    [RelayCommand]
    private void Search()
    {
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var filtered = _allDevices.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            filtered = filtered.Where(d =>
                d.DeviceName?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) == true ||
                d.UserDisplayName?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) == true ||
                d.UserPrincipalName?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) == true ||
                d.EmployeeId?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) == true);
        }

        if (!string.IsNullOrWhiteSpace(SelectedOsFilter) && SelectedOsFilter != "All")
        {
            filtered = filtered.Where(d =>
                string.Equals(d.OperatingSystem, SelectedOsFilter, StringComparison.OrdinalIgnoreCase));
        }

        Devices = new ObservableCollection<DeviceDisplayItem>(filtered);
    }

    partial void OnSearchTextChanged(string? value)
    {
        ApplyFilter();
    }

    partial void OnSelectedOsFilterChanged(string? value)
    {
        ApplyFilter();
    }

    [RelayCommand]
    private async Task EditDeviceAsync()
    {
        if (SelectedDevice == null) return;
        SelectedDevice.ResetEdits();
        IsEditing = true;
        EditStatusMessage = null;

        if (DeviceCategories.Count == 0)
        {
            try
            {
                var categories = await _graphService.GetDeviceCategoriesAsync();
                DeviceCategories = new ObservableCollection<DeviceCategory>(categories);
            }
            catch { /* Categories will be empty if fetch fails */ }
        }
    }

    [RelayCommand]
    private void CancelEdit()
    {
        if (SelectedDevice != null)
            SelectedDevice.ResetEdits();
        IsEditing = false;
        EditStatusMessage = null;
    }

    [RelayCommand]
    private async Task SaveDeviceAsync()
    {
        if (SelectedDevice?.Id == null) return;

        var patch = SelectedDevice.BuildPatchDevice();
        var categoryChanged = SelectedDevice.EditDeviceCategory?.Id != null &&
            !string.Equals(SelectedDevice.EditDeviceCategory.DisplayName, SelectedDevice.DeviceCategoryDisplayName, StringComparison.OrdinalIgnoreCase);

        if (patch == null && !categoryChanged)
        {
            EditStatusMessage = "No changes to save.";
            return;
        }

        try
        {
            IsSaving = true;
            EditStatusMessage = "Saving...";

            if (patch != null)
                await _graphService.UpdateManagedDeviceAsync(SelectedDevice.Id, patch);

            if (categoryChanged)
                await _graphService.SetDeviceCategoryAsync(SelectedDevice.Id, SelectedDevice.EditDeviceCategory!.Id!);

            SelectedDevice.ApplyEdits();
            IsEditing = false;
            EditStatusMessage = "Device updated successfully.";
        }
        catch (Exception ex)
        {
            EditStatusMessage = $"Failed to save: {ex.Message}";
        }
        finally
        {
            IsSaving = false;
        }
    }

    [RelayCommand]
    private async Task ShowUserAssignmentAsync()
    {
        if (SelectedDevice?.Id == null) return;
        IsUserAssignmentVisible = true;
        UserAssignmentStatus = null;
        AssignUserUpn = null;
        await LoadDeviceAssignmentsAsync();
    }

    [RelayCommand]
    private void HideUserAssignment()
    {
        IsUserAssignmentVisible = false;
        UserAssignmentStatus = null;
    }

    private async Task LoadDeviceAssignmentsAsync()
    {
        if (SelectedDevice?.Id == null) return;

        try
        {
            var azureAdDeviceId = await _graphService.GetAzureAdDeviceIdAsync(SelectedDevice.Id);
            if (azureAdDeviceId != null)
            {
                var owners = await _graphService.GetAzureAdDeviceOwnersAsync(azureAdDeviceId);
                CurrentOwners = new ObservableCollection<string>(
                    owners.OfType<User>().Select(u => $"{u.DisplayName} ({u.UserPrincipalName}) [{u.Id}]"));

                var users = await _graphService.GetAzureAdDeviceUsersAsync(azureAdDeviceId);
                CurrentRegisteredUsers = new ObservableCollection<string>(
                    users.OfType<User>().Select(u => $"{u.DisplayName} ({u.UserPrincipalName}) [{u.Id}]"));
            }
            else
            {
                CurrentOwners = [];
                CurrentRegisteredUsers = [];
                UserAssignmentStatus = "Azure AD device not found. Cannot manage owners/users.";
            }
        }
        catch (Exception ex)
        {
            UserAssignmentStatus = $"Failed to load assignments: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task SetPrimaryUserAsync()
    {
        if (SelectedDevice?.Id == null || string.IsNullOrWhiteSpace(AssignUserUpn)) return;

        try
        {
            IsAssigning = true;
            UserAssignmentStatus = "Setting Intune primary user...";
            var user = await _graphService.GetUserByUpnAsync(AssignUserUpn.Trim());
            if (user?.Id == null)
            {
                UserAssignmentStatus = "User not found.";
                return;
            }

            await _graphService.SetIntunePrimaryUserAsync(SelectedDevice.Id, user.Id);
            UserAssignmentStatus = $"Primary user set to {user.DisplayName}.";
        }
        catch (Exception ex)
        {
            UserAssignmentStatus = $"Failed: {ex.Message}";
        }
        finally
        {
            IsAssigning = false;
        }
    }

    [RelayCommand]
    private async Task RemovePrimaryUserAsync()
    {
        if (SelectedDevice?.Id == null) return;

        try
        {
            IsAssigning = true;
            UserAssignmentStatus = "Removing Intune primary user...";
            await _graphService.RemoveIntunePrimaryUserAsync(SelectedDevice.Id);
            UserAssignmentStatus = "Primary user removed.";
        }
        catch (Exception ex)
        {
            UserAssignmentStatus = $"Failed: {ex.Message}";
        }
        finally
        {
            IsAssigning = false;
        }
    }

    [RelayCommand]
    private async Task AddDeviceOwnerAsync()
    {
        if (SelectedDevice?.Id == null || string.IsNullOrWhiteSpace(AssignUserUpn)) return;

        try
        {
            IsAssigning = true;
            UserAssignmentStatus = "Adding Azure AD device owner...";
            var azureAdDeviceId = await _graphService.GetAzureAdDeviceIdAsync(SelectedDevice.Id);
            if (azureAdDeviceId == null) { UserAssignmentStatus = "Azure AD device not found."; return; }

            var user = await _graphService.GetUserByUpnAsync(AssignUserUpn.Trim());
            if (user?.Id == null) { UserAssignmentStatus = "User not found."; return; }

            await _graphService.AddAzureAdDeviceOwnerAsync(azureAdDeviceId, user.Id);
            UserAssignmentStatus = $"Owner {user.DisplayName} added.";
            await LoadDeviceAssignmentsAsync();
        }
        catch (Exception ex)
        {
            UserAssignmentStatus = $"Failed: {ex.Message}";
        }
        finally
        {
            IsAssigning = false;
        }
    }

    [RelayCommand]
    private async Task RemoveDeviceOwnerAsync()
    {
        if (SelectedDevice?.Id == null || string.IsNullOrEmpty(SelectedOwner)) return;

        try
        {
            IsAssigning = true;
            var userId = ExtractUserIdFromDisplay(SelectedOwner);
            if (userId == null) { UserAssignmentStatus = "Could not determine user ID."; return; }

            var azureAdDeviceId = await _graphService.GetAzureAdDeviceIdAsync(SelectedDevice.Id);
            if (azureAdDeviceId == null) { UserAssignmentStatus = "Azure AD device not found."; return; }

            await _graphService.RemoveAzureAdDeviceOwnerAsync(azureAdDeviceId, userId);
            UserAssignmentStatus = "Owner removed.";
            await LoadDeviceAssignmentsAsync();
        }
        catch (Exception ex)
        {
            UserAssignmentStatus = $"Failed: {ex.Message}";
        }
        finally
        {
            IsAssigning = false;
        }
    }

    [RelayCommand]
    private async Task AddRegisteredUserAsync()
    {
        if (SelectedDevice?.Id == null || string.IsNullOrWhiteSpace(AssignUserUpn)) return;

        try
        {
            IsAssigning = true;
            UserAssignmentStatus = "Adding Azure AD registered user...";
            var azureAdDeviceId = await _graphService.GetAzureAdDeviceIdAsync(SelectedDevice.Id);
            if (azureAdDeviceId == null) { UserAssignmentStatus = "Azure AD device not found."; return; }

            var user = await _graphService.GetUserByUpnAsync(AssignUserUpn.Trim());
            if (user?.Id == null) { UserAssignmentStatus = "User not found."; return; }

            await _graphService.AddAzureAdDeviceUserAsync(azureAdDeviceId, user.Id);
            UserAssignmentStatus = $"Registered user {user.DisplayName} added.";
            await LoadDeviceAssignmentsAsync();
        }
        catch (Exception ex)
        {
            UserAssignmentStatus = $"Failed: {ex.Message}";
        }
        finally
        {
            IsAssigning = false;
        }
    }

    [RelayCommand]
    private async Task RemoveRegisteredUserAsync()
    {
        if (SelectedDevice?.Id == null || string.IsNullOrEmpty(SelectedRegisteredUser)) return;

        try
        {
            IsAssigning = true;
            var userId = ExtractUserIdFromDisplay(SelectedRegisteredUser);
            if (userId == null) { UserAssignmentStatus = "Could not determine user ID."; return; }

            var azureAdDeviceId = await _graphService.GetAzureAdDeviceIdAsync(SelectedDevice.Id);
            if (azureAdDeviceId == null) { UserAssignmentStatus = "Azure AD device not found."; return; }

            await _graphService.RemoveAzureAdDeviceUserAsync(azureAdDeviceId, userId);
            UserAssignmentStatus = "Registered user removed.";
            await LoadDeviceAssignmentsAsync();
        }
        catch (Exception ex)
        {
            UserAssignmentStatus = $"Failed: {ex.Message}";
        }
        finally
        {
            IsAssigning = false;
        }
    }

    private static string? ExtractUserIdFromDisplay(string display)
    {
        // Format: "DisplayName (UPN) [userId]"
        var start = display.LastIndexOf('[');
        var end = display.LastIndexOf(']');
        if (start >= 0 && end > start)
            return display[(start + 1)..end];
        return null;
    }
}

public class DeviceDisplayItem : ObservableObject
{
    private readonly ManagedDevice _device;

    public DeviceDisplayItem(ManagedDevice device, string? employeeId)
    {
        _device = device;
        EmployeeId = employeeId;
        // Initialize editable fields
        _editDeviceName = device.DeviceName;
        _editNotes = device.Notes;
        _editManagedDeviceOwnerType = device.ManagedDeviceOwnerType;
    }

    public string? Id => _device.Id;
    public string? DeviceName => _device.DeviceName;
    public string? UserDisplayName => _device.UserDisplayName;
    public string? UserPrincipalName => _device.UserPrincipalName;
    public string? EmployeeId { get; }
    public string? OperatingSystem => _device.OperatingSystem;
    public string? OsVersion => _device.OsVersion;
    public ComplianceState? ComplianceState => _device.ComplianceState;
    public DateTimeOffset? LastSyncDateTime => _device.LastSyncDateTime;
    public ManagedDeviceOwnerType? ManagedDeviceOwnerType => _device.ManagedDeviceOwnerType;
    public string? EmailAddress => _device.EmailAddress;
    public string? SerialNumber => _device.SerialNumber;
    public string? Model => _device.Model;
    public string? Manufacturer => _device.Manufacturer;
    public string? Notes => _device.Notes;
    public ManagementAgentType? ManagementAgent => _device.ManagementAgent;
    public string? DeviceCategoryDisplayName => _device.DeviceCategoryDisplayName;

    // Editable properties
    private string? _editDeviceName;
    public string? EditDeviceName
    {
        get => _editDeviceName;
        set => SetProperty(ref _editDeviceName, value);
    }

    private string? _editNotes;
    public string? EditNotes
    {
        get => _editNotes;
        set => SetProperty(ref _editNotes, value);
    }

    private ManagedDeviceOwnerType? _editManagedDeviceOwnerType;
    public ManagedDeviceOwnerType? EditManagedDeviceOwnerType
    {
        get => _editManagedDeviceOwnerType;
        set => SetProperty(ref _editManagedDeviceOwnerType, value);
    }

    private DeviceCategory? _editDeviceCategory;
    public DeviceCategory? EditDeviceCategory
    {
        get => _editDeviceCategory;
        set => SetProperty(ref _editDeviceCategory, value);
    }

    /// <summary>
    /// Builds a ManagedDevice with only the changed properties for PATCH.
    /// Returns null if nothing changed.
    /// </summary>
    public ManagedDevice? BuildPatchDevice()
    {
        var patch = new ManagedDevice();
        bool hasChanges = false;

        if (!string.Equals(EditDeviceName, DeviceName, StringComparison.Ordinal))
        {
            patch.DeviceName = EditDeviceName;
            hasChanges = true;
        }
        if (!string.Equals(EditNotes, Notes, StringComparison.Ordinal))
        {
            patch.Notes = EditNotes;
            hasChanges = true;
        }
        if (EditManagedDeviceOwnerType != ManagedDeviceOwnerType)
        {
            patch.ManagedDeviceOwnerType = EditManagedDeviceOwnerType;
            hasChanges = true;
        }

        return hasChanges ? patch : null;
    }

    /// <summary>
    /// Applies the saved values back to the underlying device after a successful update.
    /// </summary>
    public void ApplyEdits()
    {
        _device.DeviceName = EditDeviceName;
        _device.Notes = EditNotes;
        _device.ManagedDeviceOwnerType = EditManagedDeviceOwnerType;
        if (EditDeviceCategory != null)
            _device.DeviceCategoryDisplayName = EditDeviceCategory.DisplayName;
        OnPropertyChanged(nameof(DeviceName));
        OnPropertyChanged(nameof(Notes));
        OnPropertyChanged(nameof(ManagedDeviceOwnerType));
        OnPropertyChanged(nameof(DeviceCategoryDisplayName));
    }

    /// <summary>
    /// Resets editable fields to the current device values.
    /// </summary>
    public void ResetEdits()
    {
        EditDeviceName = DeviceName;
        EditNotes = Notes;
        EditManagedDeviceOwnerType = ManagedDeviceOwnerType;
        EditDeviceCategory = null;
    }
}
