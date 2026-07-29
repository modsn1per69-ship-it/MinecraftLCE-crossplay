using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace LegacyCrossplayPatcher;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(
                args.Exception.Message,
                "Legacy Crossplay Patcher",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            args.Handled = true;
        };

        if (e.Args.Length == 3 && e.Args[0].Equals("--capture-view", StringComparison.OrdinalIgnoreCase))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var captureWindow = new MainWindow(loadUserSettings: false)
            {
                Width = 1180,
                Height = 780,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = 40,
                Top = 40
            };
            captureWindow.Show();
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            captureWindow.PrepareScreenshot(e.Args[1]);
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
            await Task.Delay(250);
            captureWindow.SaveScreenshot(Path.GetFullPath(e.Args[2]));
            captureWindow.Close();
            Shutdown();
            return;
        }

        if (e.Args.Length == 2 && e.Args[0].Equals("--capture-readme", StringComparison.OrdinalIgnoreCase))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var outputDirectory = Path.GetFullPath(e.Args[1]);
            Directory.CreateDirectory(outputDirectory);

            foreach (var view in new[] { "patch", "guide", "log" })
            {
                var captureWindow = new MainWindow(loadUserSettings: false)
                {
                    Width = 1180,
                    Height = 780,
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    Left = 40,
                    Top = 40
                };
                captureWindow.Show();
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                captureWindow.PrepareScreenshot(view);
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
                await Task.Delay(250);
                captureWindow.SaveScreenshot(Path.Combine(outputDirectory, $"patcher-{view}.png"));
                captureWindow.Close();
            }

            Shutdown();
            return;
        }

        MainWindow = new MainWindow();
        MainWindow.Show();
    }
}
