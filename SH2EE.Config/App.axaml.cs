using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using SH2EESetup.Config.ViewModels;
using SH2EESetup.Config.Views;

namespace SH2EESetup.Config
{
    public class App : Application
    {
        public override void Initialize() => AvaloniaXamlLoader.Load(this);

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow = new ConfigWindow
                {
                    DataContext = new ConfigViewModel(Program.GameDirectoryArg),
                };
            }
            base.OnFrameworkInitializationCompleted();
        }
    }
}
