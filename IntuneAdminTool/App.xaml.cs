using System.Windows;
using IntuneAdminTool.Services;
using IntuneAdminTool.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace IntuneAdminTool;

public partial class App : Application
{
    private readonly ServiceProvider _serviceProvider;

    public App()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IAuthService, AuthService>();
        services.AddSingleton<IGraphService, GraphService>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();
        _serviceProvider = services.BuildServiceProvider();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        mainWindow.DataContext = _serviceProvider.GetRequiredService<MainViewModel>();
        mainWindow.Show();
    }
}

