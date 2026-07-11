using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace GameShelf;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : Application
{
    public static MainWindow? StartupWindow { get; private set; }
    
    /// <summary>
    /// Initializes the singleton application object.  This is the first line of authored code
    /// executed, and as such is the logical equivalent of main() or WinMain().
    /// </summary>
    public App()
    {
        try
        {
            InitializeComponent();
            UnhandledException += App_UnhandledException;
        }
        catch (Exception ex)
        {
            try
            {
                var dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Aether");
                if (!System.IO.Directory.Exists(dir)) System.IO.Directory.CreateDirectory(dir);
                System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "crash_app_constructor.txt"), ex.ToString());
            }
            catch { }
            throw;
        }
    }

    private void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        try
        {
            var dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Aether");
            if (!System.IO.Directory.Exists(dir)) System.IO.Directory.CreateDirectory(dir);
            
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("--- UNHANDLED EXCEPTION ---");
            sb.AppendLine($"Time: {DateTime.Now}");
            sb.AppendLine($"Message: {e.Message}");
            sb.AppendLine($"Handled: {e.Handled}");
            
            var ex = e.Exception;
            int depth = 0;
            while (ex != null)
            {
                sb.AppendLine($"\n[Exception Level {depth}]");
                sb.AppendLine($"Type: {ex.GetType().FullName}");
                sb.AppendLine($"Message: {ex.Message}");
                sb.AppendLine($"HResult: 0x{ex.HResult:X8}");
                sb.AppendLine($"StackTrace:\n{ex.StackTrace}");
                ex = ex.InnerException;
                depth++;
            }
            
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "crash_unhandled.txt"), sb.ToString());
        }
        catch { }
    }

    /// <summary>
    /// Invoked when the application is launched.
    /// </summary>
    /// <param name="args">Details about the launch request and process.</param>
    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        try
        {
            StartupWindow = new MainWindow();
            StartupWindow.Activate();
        }
        catch (Exception ex)
        {
            try
            {
                var dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Aether");
                if (!System.IO.Directory.Exists(dir)) System.IO.Directory.CreateDirectory(dir);
                System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "crash.txt"), ex.ToString());
            }
            catch { }
            throw;
        }
    }
}
