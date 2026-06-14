using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using SH2EESetup.Models;
using SH2EESetup.Services;
using SH2EESetup.Setup.ViewModels;

namespace SH2EESetup.Setup.Views
{
    public partial class MainWindow : Window
    {
        private WizardViewModel Vm => (WizardViewModel)DataContext!;

        public MainWindow()
        {
            InitializeComponent();
            DataContextChanged += (_, _) => Hook();
            Opened += (_, _) =>
            {
                Vm.ChecksumMismatchPrompt = PromptChecksumAsync;
                Vm.AutoDetect();
                UpdateStepVisibility();
            };
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        private void Hook()
        {
            if (DataContext is not WizardViewModel vm)
                return;

            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(WizardViewModel.Step))
                    UpdateStepVisibility();
            };

            this.FindControl<Button>("BrowseButton")!.Click += async (_, _) =>
            {
                var path = await PickFolder("Select your Silent Hill 2 (PC) install folder");
                if (path != null) vm.GameDirectory = path;
            };

            var detectedBox = this.FindControl<ComboBox>("DetectedBox")!;
            detectedBox.SelectionChanged += (_, _) =>
            {
                if (detectedBox.SelectedItem is string dir) vm.GameDirectory = dir;
            };

            this.FindControl<Button>("LocalBrowseButton")!.Click += async (_, _) =>
            {
                var path = await PickFolder("Select the folder containing local_sh2ee.dat");
                if (path != null) vm.LocalSourceDir = path;
            };

            this.FindControl<Button>("BackButton")!.Click += (_, _) => vm.GoBack();
            this.FindControl<Button>("NextButton")!.Click += OnNext;
        }

        private void UpdateStepVisibility()
        {
            var vm = Vm;
            this.FindControl<StackPanel>("LocatePanel")!.IsVisible = vm.Step == WizardStep.Locate;
            this.FindControl<StackPanel>("SourcePanel")!.IsVisible = vm.Step == WizardStep.Source;
            this.FindControl<Grid>("InstallTypePanel")!.IsVisible = vm.Step == WizardStep.InstallType;
            this.FindControl<Panel>("ProgressPanel")!.IsVisible = vm.Step == WizardStep.Progress;
            this.FindControl<StackPanel>("SteamPanel")!.IsVisible = vm.Step == WizardStep.Steam;
        }

        private bool _navigating;

        private async void OnNext(object? sender, RoutedEventArgs e)
        {
            // Guard against repeated clicks while an async step (fetch/install) is running.
            // Without this, extra clicks queue duplicate work (double component loads, etc.).
            if (_navigating)
                return;
            _navigating = true;
            try
            {
                var vm = Vm;
                switch (vm.Step)
                {
                    case WizardStep.Locate:
                        vm.SetStep(WizardStep.Source);
                        break;

                    case WizardStep.Source:
                        vm.SetStep(WizardStep.InstallType);
                        // Load the component list for the chosen source.
                        vm.IsBusy = true;
                        string? err = await vm.LoadComponentsAsync();
                        vm.IsBusy = false;
                        if (err != null)
                            await ShowMessage("Couldn't load the component list", err);
                        break;

                    case WizardStep.InstallType:
                        // Move to progress and run the install immediately.
                        vm.SetStep(WizardStep.Progress);
                        await vm.RunInstallAsync();
                        break;

                    case WizardStep.Progress:
                        vm.SetStep(WizardStep.Steam);
                        break;

                    case WizardStep.Steam:
                        await Finish();
                        break;
                }
            }
            finally
            {
                _navigating = false;
            }
        }

        private async System.Threading.Tasks.Task Finish()
        {
            string msg = Vm.FinishSteam();
            if (msg.Length > 0)
                await ShowMessage("Add to Steam", msg);

            Vm.LaunchConfigApp();

            if (Avalonia.Application.Current?.ApplicationLifetime
                is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.Shutdown();
            }
            else
            {
                Close();
            }
        }

        private async System.Threading.Tasks.Task<string?> PickFolder(string title)
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = title,
                AllowMultiple = false,
            });
            return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
        }

        private async System.Threading.Tasks.Task<ChecksumAction> PromptChecksumAsync(WebComponent comp)
        {
            var result = ChecksumAction.Abort;
            var dialog = new Window
            {
                Title = "Checksum mismatch",
                Width = 460,
                SizeToContent = SizeToContent.Height,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
            };
            var retry = new Button { Content = "Retry" };
            var skip = new Button { Content = "Skip" };
            var abort = new Button { Content = "Abort" };
            retry.Click += (_, _) => { result = ChecksumAction.Retry; dialog.Close(); };
            skip.Click += (_, _) => { result = ChecksumAction.Skip; dialog.Close(); };
            abort.Click += (_, _) => { result = ChecksumAction.Abort; dialog.Close(); };
            dialog.Content = new StackPanel
            {
                Margin = new(16),
                Spacing = 14,
                Children =
                {
                    new TextBlock
                    {
                        Text = $"\"{comp.Name}\" failed verification ({comp.FileName}). " +
                               "The download may be corrupt.",
                        TextWrapping = TextWrapping.Wrap,
                    },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 8,
                        Children = { abort, skip, retry },
                    },
                },
            };
            await dialog.ShowDialog(this);
            return result;
        }

        private async System.Threading.Tasks.Task ShowMessage(string title, string message)
        {
            var dialog = new Window
            {
                Title = title,
                Width = 460,
                SizeToContent = SizeToContent.Height,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
            };
            var ok = new Button { Content = "OK", HorizontalAlignment = HorizontalAlignment.Right };
            ok.Click += (_, _) => dialog.Close();
            dialog.Content = new StackPanel
            {
                Margin = new(16),
                Spacing = 14,
                Children =
                {
                    new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                    ok,
                },
            };
            await dialog.ShowDialog(this);
        }
    }
}
