using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using ArduboardsManager.App.Services;
using AppMainViewModel = ArduManager.ViewModels.MainViewModel;
using BoardsMainViewModel = ArduboardsManager.App.ViewModels.MainViewModel;

namespace ArduManager.Views;

public partial class PlatformsView : UserControl
{
    private readonly BoardsMainViewModel _viewModel;
    private bool _initialized;
    private bool _isStatusActive;

    public string CurrentStatus => _viewModel.FooterText;

    public bool CurrentIsBusy => _viewModel.IsLoading;

    public bool IsStatusActive
    {
        get => _isStatusActive;
        set
        {
            _isStatusActive = value;
            if (value)
                PushStatusToHost();
        }
    }

    public PlatformsView()
    {
        InitializeComponent();
        _viewModel = new BoardsMainViewModel();
        _viewModel.StatusChanged += OnBoardsStatusChanged;
        _viewModel.BusyChanged += OnBoardsBusyChanged;
        _viewModel.LogMessage += OnBoardsLogMessage;
        DataContext = _viewModel;
        Loaded += async (_, _) => await InitializeSafelyAsync();
    }

    private AppMainViewModel? TryGetHostViewModel()
    {
        return Window.GetWindow(this)?.DataContext as AppMainViewModel;
    }

    private void PushStatusToHost()
    {
        if (!IsStatusActive)
            return;

        TryGetHostViewModel()?.SetPlatformStatus(_viewModel.FooterText, _viewModel.IsLoading);
    }

    private void OnBoardsStatusChanged(string message)
    {
        Dispatcher.Invoke(() =>
        {
            if (IsStatusActive)
                TryGetHostViewModel()?.SetPlatformStatus(message, _viewModel.IsLoading);
        });
    }

    private void OnBoardsBusyChanged(bool isBusy)
    {
        Dispatcher.Invoke(() =>
        {
            if (IsStatusActive)
                TryGetHostViewModel()?.SetPlatformStatus(_viewModel.FooterText, isBusy);
        });
    }

    private void OnBoardsLogMessage(string message)
    {
        Dispatcher.Invoke(() => TryGetHostViewModel()?.AddPlatformLog(message));
    }

    private void OpenHyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        try
        {
            if (e.Uri is not null)
            {
                Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri)
                {
                    UseShellExecute = true
                });
                e.Handled = true;
            }
        }
        catch (Exception ex)
        {
            LogService.Error("Failed to open hyperlink", ex);
            MessageBox.Show(ex.Message, "Не удалось открыть ссылку", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task InitializeSafelyAsync()
    {
        if (_initialized)
            return;

        _initialized = true;

        try
        {
            await _viewModel.InitializeAsync();
            PushStatusToHost();
        }
        catch (Exception ex)
        {
            LogService.Error("Boards initialization failed", ex);
            MessageBox.Show(
                $"Ошибка при запуске вкладки платформ. Подробности записаны в лог:\n{AppPaths.CurrentLogFile}\n\n{ex.Message}",
                "ArduManager",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
}
