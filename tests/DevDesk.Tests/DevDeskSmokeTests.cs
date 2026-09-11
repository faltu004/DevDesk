using System.Runtime.ExceptionServices;
using System.Windows;
using DevDesk.App.Views.Dashboard;
using DevDesk.App.Views.Ports;
using DevDesk.App.Views.Processes;
using DevDesk.App.Views.Projects;

namespace DevDesk.Tests;

[Collection("StaSmoke")]
public sealed class DevDeskSmokeTests
{
    private static readonly object AppInitLock = new();

    [Theory]
    [InlineData(typeof(DashboardView))]
    [InlineData(typeof(ProjectsView))]
    [InlineData(typeof(ProcessesView))]
    [InlineData(typeof(PortsView))]
    [InlineData(typeof(ProjectLogsView))]
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

    [Fact]
    public void ScrollBar_Styles_VerticalAndHorizontal_HaveCorrectDimensionsAndDirection()
    {
        RunOnSta(() =>
        {
            EnsureApplicationResourcesLoaded();

            var app = Application.Current;
            var style = (Style)app.TryFindResource(typeof(System.Windows.Controls.Primitives.ScrollBar));
            Assert.NotNull(style);

            // Base setter: Width = 6
            var widthSetter = style.Setters.OfType<Setter>().FirstOrDefault(s => s.Property == FrameworkElement.WidthProperty);
            Assert.NotNull(widthSetter);
            Assert.Equal(6.0, Convert.ToDouble(widthSetter.Value));

            // Triggers exist for both orientations
            var triggers = style.Triggers.OfType<Trigger>().ToList();
            var vTrigger = triggers.FirstOrDefault(t => t.Property == System.Windows.Controls.Primitives.ScrollBar.OrientationProperty && Equals(t.Value, System.Windows.Controls.Orientation.Vertical));
            var hTrigger = triggers.FirstOrDefault(t => t.Property == System.Windows.Controls.Primitives.ScrollBar.OrientationProperty && Equals(t.Value, System.Windows.Controls.Orientation.Horizontal));

            Assert.NotNull(vTrigger);
            Assert.NotNull(hTrigger);

            var vWidthSetter = vTrigger.Setters.OfType<Setter>().FirstOrDefault(s => s.Property == FrameworkElement.WidthProperty);
            Assert.NotNull(vWidthSetter);
            Assert.Equal(6.0, Convert.ToDouble(vWidthSetter.Value));

            var vHeightSetter = vTrigger.Setters.OfType<Setter>().FirstOrDefault(s => s.Property == FrameworkElement.HeightProperty);
            Assert.NotNull(vHeightSetter);
            Assert.Equal(double.NaN, (double)vHeightSetter.Value);

            var hHeightSetter = hTrigger.Setters.OfType<Setter>().FirstOrDefault(s => s.Property == FrameworkElement.HeightProperty);
            Assert.NotNull(hHeightSetter);
            Assert.Equal(6.0, Convert.ToDouble(hHeightSetter.Value));

            var hWidthSetter = hTrigger.Setters.OfType<Setter>().FirstOrDefault(s => s.Property == FrameworkElement.WidthProperty);
            Assert.NotNull(hWidthSetter);
            Assert.Equal(double.NaN, (double)hWidthSetter.Value);
        });
    }

    [Fact]
    public void AddEditProjectDialog_InstantiatesAndResolvesResources_OnStaThread()
    {
        RunOnSta(() =>
        {
            EnsureApplicationResourcesLoaded();

            var vm = new DevDesk.App.ViewModels.Projects.AddEditProjectViewModel(string.Empty);
            var dialog = new AddEditProjectDialog(vm);
            Assert.NotNull(dialog);
            Assert.Equal("Add Project", vm.DialogTitle);
        });
    }

    [Fact]
    public void ConfirmShutdownDialog_InstantiatesAndResolvesResources_OnStaThread()
    {
        RunOnSta(() =>
        {
            EnsureApplicationResourcesLoaded();

            var vm = new DevDesk.App.ViewModels.Shell.ConfirmShutdownViewModel(2);
            var dialog = new DevDesk.App.Views.Shell.ConfirmShutdownDialog(vm);
            Assert.NotNull(dialog);
            Assert.Equal("Active Projects Running - DevDesk", dialog.Title);
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

    private static readonly System.Collections.Concurrent.BlockingCollection<Action> StaQueue = new();
    private static readonly Thread StaWorkerThread;

    static DevDeskSmokeTests()
    {
        StaWorkerThread = new Thread(() =>
        {
            foreach (var item in StaQueue.GetConsumingEnumerable())
            {
                item();
            }
        })
        {
            IsBackground = true
        };
        StaWorkerThread.SetApartmentState(ApartmentState.STA);
        StaWorkerThread.Start();
    }

    private static void RunOnSta(Action action)
    {
        Exception? exception = null;
        using var done = new ManualResetEventSlim(false);
        StaQueue.Add(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                exception = ex;
            }
            finally
            {
                done.Set();
            }
        });

        done.Wait();

        if (exception is not null)
        {
            ExceptionDispatchInfo.Capture(exception).Throw();
        }
    }
}
