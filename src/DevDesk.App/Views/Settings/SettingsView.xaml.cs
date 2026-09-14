using System.Windows;
using System.Windows.Controls;
using DevDesk.App.ViewModels.Settings;

namespace DevDesk.App.Views.Settings;

/// <summary>
/// Interaction logic for SettingsView.xaml
/// </summary>
public partial class SettingsView : UserControl
{
    private bool _isProgrammaticScroll;
    private SettingsViewModel? _subscribedViewModel;

    public SettingsView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (SettingsScrollViewer != null)
        {
            SettingsScrollViewer.ScrollChanged += OnScrollChanged;
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (SettingsScrollViewer != null)
        {
            SettingsScrollViewer.ScrollChanged -= OnScrollChanged;
        }
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_subscribedViewModel != null)
        {
            _subscribedViewModel.RequestScrollToCategory -= OnRequestScrollToCategory;
            _subscribedViewModel = null;
        }

        if (e.NewValue is SettingsViewModel vm)
        {
            _subscribedViewModel = vm;
            _subscribedViewModel.RequestScrollToCategory += OnRequestScrollToCategory;
        }
    }

    private void OnRequestScrollToCategory(object? sender, string categoryId)
    {
        if (SettingsScrollViewer == null) return;

        FrameworkElement? target = categoryId switch
        {
            "General" => SectionGeneral,
            "Editors" => SectionEditors,
            "Monitoring" => SectionMonitoring,
            "Data" => SectionData,
            _ => null
        };

        if (target != null)
        {
            try
            {
                _isProgrammaticScroll = true;
                var transform = target.TransformToAncestor(SettingsScrollViewer);
                var pos = transform.Transform(new Point(0, 0));
                var targetOffset = SettingsScrollViewer.VerticalOffset + pos.Y;
                SettingsScrollViewer.ScrollToVerticalOffset(Math.Max(0, targetOffset));
            }
            catch
            {
                // Best effort if element not yet arranged
                target.BringIntoView();
            }
            finally
            {
                // Reset after dispatcher cycle to allow scroll event to fire safely
                Dispatcher.InvokeAsync(() => _isProgrammaticScroll = false, System.Windows.Threading.DispatcherPriority.Loaded);
            }
        }
    }

    private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (_isProgrammaticScroll || DataContext is not SettingsViewModel vm || SettingsScrollViewer == null)
        {
            return;
        }

        // Only process vertical scroll changes
        if (Math.Abs(e.VerticalChange) < 0.5 && Math.Abs(e.VerticalOffset) > 0.1)
        {
            return;
        }

        var sections = new (string Id, FrameworkElement Elem)[]
        {
            ("General", SectionGeneral),
            ("Editors", SectionEditors),
            ("Monitoring", SectionMonitoring),
            ("Data", SectionData)
        };

        string? activeId = null;

        foreach (var (id, elem) in sections)
        {
            try
            {
                var transform = elem.TransformToAncestor(SettingsScrollViewer);
                var pos = transform.Transform(new Point(0, 0));
                // If section top is near or past the top of the viewport (within 80px)
                if (pos.Y <= 80)
                {
                    activeId = id;
                }
            }
            catch
            {
                // In case visual tree is not connected
            }
        }

        if (!string.IsNullOrEmpty(activeId))
        {
            vm.UpdateSelectedCategoryFromScroll(activeId);
        }
    }
}
