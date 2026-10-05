using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;

namespace MdbConverter.App;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        UnhandledException += OnUnhandledException;
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            _window = new MainWindow();
            _window.Activate();
        }
        catch (Exception ex)
        {
            StartupLog.Fail(ex);
            Environment.Exit(1);
        }
    }

    private static void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        StartupLog.Fail(e.Exception);
        e.Handled = true;
        Environment.Exit(1);
    }
}

internal static class StartupLog
{
    public static void Fail(Exception ex)
    {
        try
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MdbConverter");
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "startup-error.txt"), ex.ToString());
            MessageBox(IntPtr.Zero, ex.Message, "MDB Converter", 0x00000010);
        }
        catch (Exception)
        {
            // Last-chance report. The process is already exiting.
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "MessageBoxW")]
    private static extern int MessageBox(IntPtr owner, string text, string caption, uint type);
}
