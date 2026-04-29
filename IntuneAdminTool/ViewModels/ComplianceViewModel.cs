namespace IntuneAdminTool.ViewModels;

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IntuneAdminTool.Services;
using Microsoft.Graph.Models;

public partial class ComplianceViewModel : ObservableObject
{
    private readonly IGraphService _graphService;

    [ObservableProperty]
    private int _compliantCount;

    [ObservableProperty]
    private int _nonCompliantCount;

    [ObservableProperty]
    private int _unknownCount;

    [ObservableProperty]
    private ObservableCollection<ManagedDevice> _devices = [];

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorMessage;

    public ComplianceViewModel(IGraphService graphService)
    {
        _graphService = graphService;
    }

    [RelayCommand]
    private async Task LoadComplianceAsync()
    {
        try
        {
            IsLoading = true;
            ErrorMessage = null;
            var allDevices = await _graphService.GetManagedDevicesAsync();

            CompliantCount = allDevices.Count(d => d.ComplianceState == ComplianceState.Compliant);
            NonCompliantCount = allDevices.Count(d => d.ComplianceState == ComplianceState.Noncompliant);
            UnknownCount = allDevices.Count(d => d.ComplianceState == ComplianceState.Unknown
                || d.ComplianceState == null);

            Devices = new ObservableCollection<ManagedDevice>(allDevices);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load compliance data: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }
}
