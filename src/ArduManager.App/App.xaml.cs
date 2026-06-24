using System.Text;
using System.Windows;
using ArduManager.Views;
using ArduboardsManager.App.Services;

namespace ArduManager;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        try
        {
            base.OnStartup(e);

            try
            {
                LogService.Info("Application startup");
                new StartupCleanupService(new ArduinoPathService()).CleanupLeftovers();
            }
            catch
            {
                // Cleanup/logging must not prevent the main app from starting.
            }

            var window = new MainWindow();
            MainWindow = window;
            window.Show();
        }
        catch (Exception ex)
        {
            WriteStartupError(ex);
            MessageBox.Show(
                "Приложение не смогло запуститься. Подробности записаны в startup-error.log рядом с приложением." + Environment.NewLine + Environment.NewLine + ex.Message,
                "ArduManager",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private static void WriteStartupError(Exception ex)
    {
        try
        {
            var logPath = Path.Combine(AppContext.BaseDirectory, "startup-error.log");
            var sb = new StringBuilder();
            sb.AppendLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine(ex.ToString());
            File.WriteAllText(logPath, sb.ToString());
        }
        catch
        {
            // ignore logging errors
        }
    }
}
