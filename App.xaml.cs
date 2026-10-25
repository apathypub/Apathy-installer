using System.Windows;
using System.Windows.Threading;
using Apathy.Services;
using Apathy.Themes;

namespace Apathy;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandledException;

        // Built here instead of StartupUri: if the menu can't be created, say why and exit
        // instead of leaving an invisible process behind.
        try
        {
            ThemeManager.InstallPattern();
            var menu = new MainWindow();
            MainWindow = menu;
            menu.Show();
        }
        catch (Exception ex)
        {
            ShowError("apathy could not start", ex);
            Shutdown(1);
        }
    }

    private static void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        ShowError("Something went wrong", e.Exception);
        e.Handled = true;
    }

    private static void ShowError(string title, Exception ex)
    {
        Log.Write("ERROR: " + ex);
        MessageBox.Show(title + ":\n\n" + ex.Message + "\n\nDetails are in " + Log.FilePath,
            "apathy", MessageBoxButton.OK, MessageBoxImage.Error);
    }
}
