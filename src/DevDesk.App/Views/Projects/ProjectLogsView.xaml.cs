using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DevDesk.App.ViewModels.Projects;

namespace DevDesk.App.Views.Projects;

/// <summary>
/// Interaction logic for ProjectLogsView.xaml.
/// Coordinates virtualized scroll tracking and auto-scroll execution.
/// </summary>
public partial class ProjectLogsView : UserControl
{
    private ScrollViewer? _scrollViewer;
    private ProjectLogsViewModel? _subscribedVm;

    public ProjectLogsView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        AttachScrollViewer();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        DetachScrollViewer();
        DetachViewModel();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        DetachViewModel();

        if (e.NewValue is ProjectLogsViewModel vm)
        {
            _subscribedVm = vm;
            _subscribedVm.ScrollToBottomRequested += OnScrollToBottomRequested;
        }
    }

    private void DetachViewModel()
    {
        if (_subscribedVm != null)
        {
            _subscribedVm.ScrollToBottomRequested -= OnScrollToBottomRequested;
            _subscribedVm = null;
        }
    }

    private void AttachScrollViewer()
    {
        if (_scrollViewer != null)
        {
            return;
        }

        _scrollViewer = FindVisualChild<ScrollViewer>(LogsListBox);
        if (_scrollViewer != null)
        {
            _scrollViewer.ScrollChanged += OnScrollViewerScrollChanged;
        }
    }

    private void DetachScrollViewer()
    {
        if (_scrollViewer != null)
        {
            _scrollViewer.ScrollChanged -= OnScrollViewerScrollChanged;
            _scrollViewer = null;
        }
    }

    private void OnScrollViewerScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.VerticalChange != 0 && sender is ScrollViewer sv && DataContext is ProjectLogsViewModel vm)
        {
            bool isNearBottom = sv.VerticalOffset >= (sv.ScrollableHeight - 5);
            vm.NotifyUserScrolled(isNearBottom);
        }
    }

    private void OnScrollToBottomRequested(object? sender, EventArgs e)
    {
        Dispatcher.InvokeAsync(() =>
        {
            if (_scrollViewer == null)
            {
                AttachScrollViewer();
            }

            if (_scrollViewer != null)
            {
                _scrollViewer.ScrollToEnd();
            }
            else if (LogsListBox.Items.Count > 0)
            {
                LogsListBox.ScrollIntoView(LogsListBox.Items[^1]);
            }
        });
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typedChild)
            {
                return typedChild;
            }

            var result = FindVisualChild<T>(child);
            if (result != null)
            {
                return result;
            }
        }

        return null;
    }
}
