namespace IntuneAdminTool.ViewModels;

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IntuneAdminTool.Services;

public partial class WindowsUpdatesViewModel : ObservableObject
{
    private readonly IGraphService _graphService;

    [ObservableProperty]
    private ObservableCollection<WindowsUpdateRing> _updateRings = [];

    [ObservableProperty]
    private ObservableCollection<WindowsFeatureUpdate> _featureUpdates = [];

    [ObservableProperty]
    private ObservableCollection<WindowsDriverUpdate> _driverUpdates = [];

    [ObservableProperty]
    private WindowsUpdateRing? _selectedRing;

    [ObservableProperty]
    private WindowsFeatureUpdate? _selectedFeatureUpdate;

    [ObservableProperty]
    private WindowsDriverUpdate? _selectedDriverUpdate;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private int _selectedTabIndex;

    public WindowsUpdatesViewModel(IGraphService graphService)
    {
        _graphService = graphService;
    }

    [RelayCommand]
    private async Task LoadWindowsUpdatesAsync()
    {
        try
        {
            IsLoading = true;
            ErrorMessage = null;

            var ringsTask = SafeLoadAsync(_graphService.GetWindowsUpdateRingsAsync);
            var featureTask = SafeLoadAsync(_graphService.GetWindowsFeatureUpdatesAsync);
            var driverTask = SafeLoadAsync(_graphService.GetWindowsDriverUpdatesAsync);

            await Task.WhenAll(ringsTask, featureTask, driverTask);

            UpdateRings = new ObservableCollection<WindowsUpdateRing>(ringsTask.Result);
            FeatureUpdates = new ObservableCollection<WindowsFeatureUpdate>(featureTask.Result);
            DriverUpdates = new ObservableCollection<WindowsDriverUpdate>(driverTask.Result);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load Windows Updates data: {ex.Message}";
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
