using System.Windows;

namespace MarkdownViewer
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            string? startupFilePath = e.Args.Length > 0 ? e.Args[0] : null;
            var window = new MainWindow(startupFilePath);
            MainWindow = window;
            window.Show();
        }
    }
}
