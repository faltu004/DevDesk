using System.Runtime.ExceptionServices;
using System.Windows;
using DevDesk.App.Views.Dashboard;
using DevDesk.App.Views.Ports;
using DevDesk.App.Views.Projects;

namespace DevDesk.Tests;

public sealed class DevDeskSmokeTests
{
    private static readonly object AppInitLock = new();

    [Theory]
    [InlineData(typeof(DashboardView))]
    [InlineData(typeof(ProjectsView))]
    [InlineData(typeof(PortsView))]
    public void Views_InstantiateAndResolveResources_OnStaThread(Type viewType)
    {
        RunOnSta(() =>
        {
            EnsureApplicationResourcesLoaded();

            var instance = Activator.CreateInstance(viewType);
            Assert.NotNull(instance);
            Assert.IsAssignableFrom<FrameworkElement>(instance);
        });
    }

    private static void EnsureApplicationResourcesLoaded()
    {
        lock (AppInitLock)
        {
            var app = Application.Current ?? new Application();

            if (app.Resources.MergedDictionaries.Count == 0)
            {
                string[] resourcePaths =
                [
                    "pack://application:,,,/DevDesk.App;component/Resources/Themes/Colors.xaml",
                    "pack://application:,,,/DevDesk.App;component/Resources/Themes/Theme.Dark.xaml",
                    "pack://application:,,,/DevDesk.App;component/Resources/Styles/Typography.xaml",
                    "pack://application:,,,/DevDesk.App;component/Resources/Styles/Buttons.xaml",
                    "pack://application:,,,/DevDesk.App;component/Resources/Styles/Controls.xaml"
                ];

                foreach (var path in resourcePaths)
                {
                    app.Resources.MergedDictionaries.Add(new ResourceDictionary
                    {
                        Source = new Uri(path, UriKind.Absolute)
                    });
                }
            }
        }
    }

    private static void RunOnSta(Action action)
    {
        Exception? exception = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                exception = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (exception is not null)
        {
            ExceptionDispatchInfo.Capture(exception).Throw();
        }
    }
}
