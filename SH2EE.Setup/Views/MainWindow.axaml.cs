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
                if (path != null) vm.ConfirmGameDirectory(path);
            };

            var detectedBox = this.FindControl<ComboBox>("DetectedBox")!;
            detectedBox.SelectionChanged += (_, _) =>
            {
                if (detectedBox.SelectedItem is string dir) vm.ConfirmGameDirectory(dir);
            };

            this.FindControl<Button>("LocalBrowseButton")!.Click += async (_, _) =>
            {
                var path = await PickFolder("Select the folder containing local_sh2ee.dat");
                if (path != null) vm.LocalSourceDir = path;
            };

            this.FindControl<Button>("BackupBrowseButton")!.Click += async (_, _) =>
            {
                var path = await PickFolder("Select a folder to keep the downloaded files in");
                if (path != null) vm.OfflineBackupDir = path;
            };

            this.FindControl<Button>("BackupTargetBrowseButton")!.Click += async (_, _) =>
            {
                var path = await PickFolder("Select a folder to save the offline files in");
                if (path != null) vm.BackupTargetDir = path;
            };

            this.FindControl<Button>("CancelInstallButton")!.Click += (_, _) => vm.CancelOperation();
            this.FindControl<Button>("CancelBackupButton")!.Click += (_, _) => vm.CancelOperation();

            this.FindControl<Button>("HomeOptionsButton")!.Click += (_, _) => vm.GoToHome();

            this.FindControl<Button>("HomeBackupButton")!.Click += async (_, _) =>
            {
                // Leaving Home hides its buttons, so this can't be re-entered mid-fetch.
                vm.StartBackup();
                vm.IsBusy = true;
                string? err = await vm.LoadComponentsAsync();
                vm.IsBusy = false;
                if (err != null)
                    await ShowMessage("Couldn't load the component list", err);
            };

            this.FindControl<Button>("HomeModifyButton")!.Click += (_, _) => vm.GoToInstallFlow();
            this.FindControl<Button>("HomeUninstallButton")!.Click += (_, _) => vm.StartUninstall();
            this.FindControl<Button>("HomeDifferentFolderButton")!.Click +=
                (_, _) => vm.ChooseDifferentFolder();
            this.FindControl<Button>("HomeConfigButton")!.Click += async (_, _) =>
            {
                // The config app replaces this one: keeping both open invites two writers on
                // the same d3d8.ini. Only close once it has actually started.
                if (vm.LaunchConfigApp())
                    CloseApp();
                else
                    await ShowMessage("Configuration tool",
                        "We couldn't find the configuration app next to this one. It ships " +
                        "alongside the setup tool in the AppImage and the portable download.");
            };

            this.FindControl<Button>("BackButton")!.Click += (_, _) => vm.GoBack();
            this.FindControl<Button>("NextButton")!.Click += OnNext;
        }

        private void UpdateStepVisibility()
        {
            var vm = Vm;
            this.FindControl<StackPanel>("LocatePanel")!.IsVisible = vm.Step == WizardStep.Locate;
            this.FindControl<StackPanel>("SourcePanel")!.IsVisible = vm.Step == WizardStep.Source;
            this.FindControl<Panel>("InstallTypePanel")!.IsVisible = vm.Step == WizardStep.InstallType;
            this.FindControl<Panel>("ProgressPanel")!.IsVisible = vm.Step == WizardStep.Progress;
            this.FindControl<StackPanel>("SteamPanel")!.IsVisible = vm.Step == WizardStep.Steam;
            this.FindControl<Panel>("UninstallPanel")!.IsVisible = vm.Step == WizardStep.Uninstall;
            this.FindControl<StackPanel>("HomePanel")!.IsVisible = vm.Step == WizardStep.Home;
            this.FindControl<Panel>("BackupPanel")!.IsVisible = vm.Step == WizardStep.Backup;
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
                        vm.GoToInstallFlow();
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

                    case WizardStep.Backup:
                        if (vm.BackupComplete)
                            vm.GoToHome();
                        else
                            await vm.RunBackupAsync();
                        break;

                    case WizardStep.Uninstall:
                        if (vm.UninstallComplete)
                        {
                            CloseApp();
                            break;
                        }
                        // Last stop before anything is deleted.
                        if (await ConfirmAsync(
                                "Remove the Enhanced Edition?",
                                vm.UninstallPlan + "\n\nThis can't be undone.",
                                confirmLabel: "Uninstall"))
                        {
                            await vm.RunUninstallAsync();
                        }
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
            CloseApp();
        }

        private void CloseApp()
        {
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

        /// <summary>
        /// Yes/no dialog for destructive actions. Defaults to "no": closing the window with
        /// the title bar leaves <c>confirmed</c> false, so only the explicit button proceeds.
        /// </summary>
        private async System.Threading.Tasks.Task<bool> ConfirmAsync(
            string title, string message, string confirmLabel)
        {
            bool confirmed = false;
            var dialog = new Window
            {
                Title = title,
                Width = 480,
                SizeToContent = SizeToContent.Height,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
            };
            var cancel = new Button { Content = "Cancel", MinWidth = 90 };
            var confirm = new Button { Content = confirmLabel, MinWidth = 110 };
            cancel.Click += (_, _) => dialog.Close();
            confirm.Click += (_, _) => { confirmed = true; dialog.Close(); };
            dialog.Content = new StackPanel
            {
                Margin = new(16),
                Spacing = 14,
                Children =
                {
                    new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 8,
                        Children = { cancel, confirm },
                    },
                },
            };
            await dialog.ShowDialog(this);
            return confirmed;
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
