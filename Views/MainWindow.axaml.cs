using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using SH2EESetup.ViewModels;

namespace SH2EESetup.Views
{
    public partial class MainWindow : Window
    {
        private MainViewModel Vm => (MainViewModel)DataContext!;
        private bool _configBuilt;

        public MainWindow()
        {
            InitializeComponent();
            DataContextChanged += (_, _) => HookViewModel();
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        private void HookViewModel()
        {
            if (DataContext is not MainViewModel vm)
                return;

            // Header buttons.
            this.FindControl<Button>("BrowseButton")!.Click += OnBrowse;
            this.FindControl<Button>("AutoDetectButton")!.Click += (_, _) => vm.RunAutoDetect();

            var detectedBox = this.FindControl<ComboBox>("DetectedBox")!;
            detectedBox.SelectionChanged += (_, _) =>
            {
                if (detectedBox.SelectedItem is string dir)
                    vm.SetGameDirectory(dir);
            };

            // Install / maintenance.
            vm.Components.CollectionChanged += (_, _) => BuildInstallPanel();
            BuildInstallPanel();

            // Config buttons.
            this.FindControl<Button>("LoadConfigButton")!.Click += (_, _) =>
            {
                vm.LoadConfigValues();
                BuildConfigEditor();
            };
            this.FindControl<Button>("SaveConfigButton")!.Click += (_, _) => vm.SaveConfig();
            this.FindControl<Button>("SaveLaunchButton")!.Click += async (_, _) =>
            {
                string msg = vm.SaveAndLaunch();
                if (msg.Length > 0)
                    await ShowMessage("Save & Launch", msg);
            };
            this.FindControl<Button>("ResetConfigButton")!.Click += (_, _) =>
            {
                vm.ResetConfigDefaults();
                RefreshConfigSelections();
            };

            // Linux / Proton.
            this.FindControl<Button>("ApplyOverridesButton")!.Click += async (_, _) =>
            {
                string msg = vm.ApplyDllOverrides();
                await ShowMessage("DLL overrides", msg);
            };
            this.FindControl<Button>("CopyLaunchOptionButton")!.Click += async (_, _) =>
            {
                if (Clipboard != null)
                    await Clipboard.SetTextAsync(vm.DllOverrideLaunchOption);
            };
            this.FindControl<Button>("AddToSteamButton")!.Click += async (_, _) =>
                await ShowMessage("Add to Steam", vm.AddToSteam());
            this.FindControl<Button>("LaunchSteamButton")!.Click += async (_, _) =>
            {
                string msg = vm.LaunchViaSteam();
                if (msg.Length > 0)
                    await ShowMessage("Launch via Steam", msg);
            };

            // Resolve checksum mismatches interactively during install.
            vm.ChecksumMismatchPrompt = PromptChecksumAsync;
        }

        // ---- Game directory dialog ---------------------------------------------------

        private async void OnBrowse(object? sender, RoutedEventArgs e)
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Select your Silent Hill 2 (PC) install folder",
                AllowMultiple = false,
            });

            if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } path)
                Vm.SetGameDirectory(path);
        }

        private async void OnOfflineInstall(object? sender, RoutedEventArgs e)
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Select the folder containing local_sh2ee.dat and the component archives",
                AllowMultiple = false,
            });
            if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } path)
                await Vm.InstallOfflineAsync(path);
        }

        /// <summary>Asks the user how to handle a checksum mismatch (Retry / Skip / Abort).</summary>
        private async Task<SH2EESetup.Services.ChecksumAction> PromptChecksumAsync(
            SH2EESetup.Models.WebComponent comp)
        {
            var result = SH2EESetup.Services.ChecksumAction.Abort;
            var dialog = new Window
            {
                Title = "Checksum mismatch",
                Width = 480,
                SizeToContent = SizeToContent.Height,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
            };

            var retry = new Button { Content = "Retry download" };
            var skip = new Button { Content = "Skip this component" };
            var abort = new Button { Content = "Abort" };
            retry.Click += (_, _) => { result = SH2EESetup.Services.ChecksumAction.Retry; dialog.Close(); };
            skip.Click += (_, _) => { result = SH2EESetup.Services.ChecksumAction.Skip; dialog.Close(); };
            abort.Click += (_, _) => { result = SH2EESetup.Services.ChecksumAction.Abort; dialog.Close(); };

            dialog.Content = new StackPanel
            {
                Margin = new(16),
                Spacing = 14,
                Children =
                {
                    new TextBlock
                    {
                        Text = $"\"{comp.Name}\" failed verification — the download for " +
                               $"{comp.FileName} may be corrupt. What would you like to do?",
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

        // ---- Install / maintenance list ----------------------------------------------

        private void BuildInstallPanel()
        {
            var panel = this.FindControl<StackPanel>("InstallPanel")!;
            panel.Children.Clear();

            // Action buttons.
            var refreshBtn = new Button { Content = "Fetch / refresh component list" };
            refreshBtn.Click += async (_, _) => await Vm.RefreshManifestAsync();

            var installBtn = new Button { Content = "Install / Update selected", Margin = new(8, 0, 0, 0) };
            installBtn.Click += async (_, _) => await Vm.InstallSelectedAsync();

            var uninstallBtn = new Button { Content = "Uninstall", Margin = new(8, 0, 0, 0) };
            uninstallBtn.Click += async (_, _) =>
            {
                if (await Confirm("Uninstall SH2: Enhanced Edition?",
                        "This removes all enhancement files and restores the original sh2pc.exe."))
                    Vm.Uninstall();
            };

            var offlineBtn = new Button { Content = "Offline install…", Margin = new(8, 0, 0, 0) };
            offlineBtn.Click += OnOfflineInstall;

            panel.Children.Add(new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children = { refreshBtn, installBtn, uninstallBtn, offlineBtn },
            });

            if (Vm.Components.Count == 0)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = "Click \"Fetch / refresh\" to load the component list from the SH2:EE servers.",
                    Opacity = 0.7,
                    Margin = new(0, 8, 0, 0),
                });
                return;
            }

            foreach (var comp in Vm.Components)
            {
                var check = new CheckBox
                {
                    IsChecked = comp.IsSelected,
                    IsEnabled = comp.IsEnabled,
                    Content = new StackPanel
                    {
                        Children =
                        {
                            new StackPanel
                            {
                                Orientation = Orientation.Horizontal,
                                Spacing = 8,
                                Children =
                                {
                                    new TextBlock { Text = comp.Name, FontWeight = FontWeight.Bold },
                                    new TextBlock
                                    {
                                        Text = comp.StatusLabel,
                                        Foreground = comp.UpdateAvailable ? Brushes.Goldenrod
                                            : comp.IsInstalled ? Brushes.MediumSeaGreen
                                            : Brushes.Gray,
                                        FontSize = 11,
                                        VerticalAlignment = VerticalAlignment.Center,
                                    },
                                },
                            },
                            new TextBlock
                            {
                                Text = comp.Description,
                                TextWrapping = TextWrapping.Wrap,
                                Opacity = 0.7,
                                FontSize = 12,
                                MaxWidth = 680,
                            },
                        },
                    },
                };
                var captured = comp;
                check.IsCheckedChanged += (_, _) => captured.IsSelected = check.IsChecked ?? false;

                panel.Children.Add(new Border
                {
                    Padding = new(10),
                    CornerRadius = new(4),
                    Background = new SolidColorBrush(Color.FromArgb(0x18, 0, 0, 0)),
                    Child = check,
                });
            }
        }

        // ---- Config editor -----------------------------------------------------------

        private void BuildConfigEditor()
        {
            var tabs = this.FindControl<TabControl>("ConfigTabs")!;
            if (_configBuilt)
            {
                RefreshConfigSelections();
                return;
            }

            var items = new List<TabItem>();
            foreach (var tab in Vm.ConfigTabs)
            {
                var sectionStack = new StackPanel { Spacing = 16, Margin = new(8) };

                foreach (var section in tab.Sections)
                {
                    var secStack = new StackPanel { Spacing = 10 };
                    secStack.Children.Add(new TextBlock
                    {
                        Text = section.Name,
                        FontWeight = FontWeight.Bold,
                        FontSize = 15,
                    });

                    foreach (var feature in section.Features)
                    {
                        if (!Vm.FeatureVms.TryGetValue(feature.Name, out var fvm))
                            continue;
                        secStack.Children.Add(BuildFeatureControl(fvm));
                    }

                    sectionStack.Children.Add(new Border
                    {
                        Padding = new(12),
                        CornerRadius = new(6),
                        Background = new SolidColorBrush(Color.FromArgb(0x18, 0, 0, 0)),
                        Child = secStack,
                    });
                }

                items.Add(new TabItem { Header = tab.Name, Content = new ScrollViewer { Content = sectionStack } });
            }

            tabs.ItemsSource = items;
            _configBuilt = true;
        }

        private Control BuildFeatureControl(FeatureViewModel fvm)
        {
            var stack = new StackPanel { Spacing = 4 };

            var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };

            bool isSpeedrunMode = fvm.Feature.Name == SH2EESetup.Models.SpeedrunMode.FeatureName;

            if (fvm.IsCheckbox)
            {
                var cb = new CheckBox
                {
                    Content = fvm.Title,
                    IsChecked = fvm.IsChecked,
                    IsEnabled = !fvm.IsLocked,
                    Tag = fvm,
                };
                cb.IsCheckedChanged += (_, _) =>
                {
                    fvm.IsChecked = cb.IsChecked ?? false;
                };
                header.Children.Add(cb);
            }
            else
            {
                header.Children.Add(new TextBlock
                {
                    Text = fvm.Title,
                    VerticalAlignment = VerticalAlignment.Center,
                    MinWidth = 320,
                });
                var combo = new ComboBox
                {
                    ItemsSource = fvm.ChoiceLabels,
                    SelectedIndex = fvm.SelectedIndex,
                    IsEnabled = !fvm.IsLocked,
                    MinWidth = 180,
                    Tag = fvm,
                };
                combo.SelectionChanged += (_, _) =>
                {
                    int previousMode = isSpeedrunMode ? CurrentSpeedrunModeValue() : 0;
                    fvm.SelectedIndex = combo.SelectedIndex;

                    if (isSpeedrunMode)
                    {
                        string msg = Vm.OnSpeedrunModeChanged(previousMode);
                        RefreshConfigSelections();
                        if (msg.Length > 0)
                            Vm.Status = msg;
                    }
                };
                header.Children.Add(combo);
            }

            stack.Children.Add(header);

            if (!string.IsNullOrWhiteSpace(fvm.Description))
            {
                stack.Children.Add(new TextBlock
                {
                    Text = fvm.Description,
                    TextWrapping = TextWrapping.Wrap,
                    Opacity = 0.65,
                    FontSize = 12,
                    MaxWidth = 720,
                });
            }

            return stack;
        }

        /// <summary>Re-syncs the editor controls to the view-model after a reset/reload.</summary>
        private void RefreshConfigSelections()
        {
            var tabs = this.FindControl<TabControl>("ConfigTabs")!;
            if (tabs.ItemsSource is not IEnumerable<TabItem> items)
                return;

            foreach (var control in items
                         .Select(i => i.Content)
                         .OfType<ScrollViewer>()
                         .Select(sv => sv.Content)
                         .OfType<StackPanel>()
                         .SelectMany(Descendants))
            {
                switch (control)
                {
                    case CheckBox { Tag: FeatureViewModel fvm } cb:
                        cb.IsChecked = fvm.IsChecked;
                        cb.IsEnabled = !fvm.IsLocked;
                        break;
                    case ComboBox { Tag: FeatureViewModel fvm } combo:
                        combo.SelectedIndex = fvm.SelectedIndex;
                        // The SpeedrunMode selector itself always stays enabled.
                        combo.IsEnabled = !fvm.IsLocked ||
                            fvm.Feature.Name == SH2EESetup.Models.SpeedrunMode.FeatureName;
                        break;
                }
            }
        }

        private int CurrentSpeedrunModeValue()
        {
            if (Vm.FeatureVms.TryGetValue(SH2EESetup.Models.SpeedrunMode.FeatureName, out var vm) &&
                int.TryParse(vm.CurrentValue, out var v))
                return v;
            return 0;
        }

        private static IEnumerable<Control> Descendants(Control root)
        {
            if (root is Panel panel)
            {
                foreach (var child in panel.Children)
                {
                    yield return child;
                    foreach (var d in Descendants(child))
                        yield return d;
                }
            }
            else if (root is Border { Child: Control bc })
            {
                yield return bc;
                foreach (var d in Descendants(bc))
                    yield return d;
            }
        }

        // ---- Small dialogs -----------------------------------------------------------

        private async Task ShowMessage(string title, string message)
        {
            var dialog = new Window
            {
                Title = title,
                Width = 460,
                SizeToContent = SizeToContent.Height,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Content = new StackPanel
                {
                    Margin = new(16),
                    Spacing = 14,
                    Children =
                    {
                        new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                        new Button { Content = "OK", HorizontalAlignment = HorizontalAlignment.Right },
                    },
                },
            };
            ((Button)((StackPanel)dialog.Content!).Children[1]).Click += (_, _) => dialog.Close();
            await dialog.ShowDialog(this);
        }

        private async Task<bool> Confirm(string title, string message)
        {
            var result = false;
            var dialog = new Window
            {
                Title = title,
                Width = 460,
                SizeToContent = SizeToContent.Height,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
            };

            var yes = new Button { Content = "Yes" };
            var no = new Button { Content = "Cancel" };
            yes.Click += (_, _) => { result = true; dialog.Close(); };
            no.Click += (_, _) => dialog.Close();

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
                        Children = { no, yes },
                    },
                },
            };

            await dialog.ShowDialog(this);
            return result;
        }
    }
}
