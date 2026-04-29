namespace IntuneAdminTool.Views;

using System.Collections.ObjectModel;
using System.Windows.Controls;
using System.Windows.Data;
using IntuneAdminTool.ViewModels;

public partial class ReportsView : UserControl
{
    public ReportsView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, System.Windows.DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is ReportsViewModel vm)
        {
            vm.PropertyChanged += (s, args) =>
            {
                if (args.PropertyName == nameof(ReportsViewModel.ReportColumns))
                    RebuildColumns(vm.ReportColumns);
            };
        }
    }

    private void RebuildColumns(ObservableCollection<string> columns)
    {
        ReportGrid.Columns.Clear();
        foreach (var col in columns)
        {
            ReportGrid.Columns.Add(new DataGridTextColumn
            {
                Header = col,
                Binding = new Binding($"[{col}]"),
                Width = new DataGridLength(1, DataGridLengthUnitType.Star)
            });
        }
    }
}
