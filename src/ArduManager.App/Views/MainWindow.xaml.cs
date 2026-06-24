using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using ArduManager.Models;
using ArduManager.ViewModels;

namespace ArduManager.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.InitializeCommand.Execute(null);
    }

    private void MainTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, MainTabControl))
            return;

        if (PlatformsHost is not null && DataContext is MainViewModel vm)
        {
            PlatformsHost.IsStatusActive = PlatformsTab?.IsSelected == true;

            if (PlatformsHost.IsStatusActive)
                vm.SetPlatformStatus(PlatformsHost.CurrentStatus, PlatformsHost.CurrentIsBusy);
            else
                vm.RestoreLibraryStatus();
        }
    }

    private void VersionComboBox_DropDownOpened(object sender, EventArgs e)
    {
        if (DataContext is MainViewModel vm && sender is ComboBox comboBox && comboBox.DataContext is SearchResult result)
            vm.LoadVersionsCommand.Execute(result);
    }

    private void OpenHyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        if (e.Uri is null)
            return;

        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }
}
