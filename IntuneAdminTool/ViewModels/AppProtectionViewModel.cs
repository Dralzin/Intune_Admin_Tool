namespace IntuneAdminTool.ViewModels;

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IntuneAdminTool.Services;

public partial class AppProtectionViewModel : ObservableObject
{
    private readonly IGraphService _graphService;

    [ObservableProperty]
    private ObservableCollection<AppProtectionPolicyItem> _policies = [];

    [ObservableProperty]
    private AppProtectionPolicyItem? _selectedPolicy;

    [ObservableProperty]
    private ObservableCollection<ProfileAssignment> _policyAssignments = [];

    [ObservableProperty]
    private string? _assignmentsStatus;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string? _searchText;

    [ObservableProperty]
    private string? _selectedPlatformFilter = "All";

    public ObservableCollection<string> PlatformFilterOptions { get; } = ["All"];

    private List<AppProtectionPolicyItem> _allPolicies = [];

    public AppProtectionViewModel(IGraphService graphService)
    {
        _graphService = graphService;
    }

    [RelayCommand]
    private async Task LoadPoliciesAsync()
    {
        try
        {
            IsLoading = true;
            ErrorMessage = null;

            _allPolicies = await _graphService.GetAppProtectionPoliciesAsync();

            var platforms = _allPolicies
                .Select(p => p.Platform)
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(p => p)
                .ToList();

            PlatformFilterOptions.Clear();
            PlatformFilterOptions.Add("All");
            foreach (var p in platforms)
                PlatformFilterOptions.Add(p!);

            ApplyFilter();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load App Protection Policies: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    partial void OnSearchTextChanged(string? value) => ApplyFilter();
    partial void OnSelectedPlatformFilterChanged(string? value) => ApplyFilter();

    private void ApplyFilter()
    {
        var filtered = _allPolicies.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(SelectedPlatformFilter) && SelectedPlatformFilter != "All")
        {
            filtered = filtered.Where(p =>
                string.Equals(p.Platform, SelectedPlatformFilter, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            filtered = filtered.Where(p =>
                p.DisplayName?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) == true);
        }

        Policies = new ObservableCollection<AppProtectionPolicyItem>(filtered);
    }

    partial void OnSelectedPolicyChanged(AppProtectionPolicyItem? value)
    {
        PolicyAssignments = [];
        AssignmentsStatus = null;
        if (value?.Id != null && value.Platform != null)
            _ = LoadAssignmentsAsync(value.Id, value.Platform);
    }

    private async Task LoadAssignmentsAsync(string policyId, string platform)
    {
        try
        {
            AssignmentsStatus = "Loading assignments...";
            var assignments = await _graphService.GetAppProtectionPolicyAssignmentsAsync(policyId, platform);
            PolicyAssignments = new ObservableCollection<ProfileAssignment>(assignments);
            AssignmentsStatus = assignments.Count == 0 ? "No assignments." : $"{assignments.Count} assignment(s).";
        }
        catch (Exception ex)
        {
            AssignmentsStatus = $"Failed to load assignments: {ex.Message}";
        }
    }
}
