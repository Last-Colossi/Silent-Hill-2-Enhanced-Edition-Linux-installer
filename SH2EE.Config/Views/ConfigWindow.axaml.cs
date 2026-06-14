using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using SH2EESetup.Config.ViewModels;
using SH2EESetup.Models;
using SH2EESetup.ViewModels;

namespace SH2EESetup.Config.Views
{
    public partial class ConfigWindow : Window
    {
        private ConfigViewModel Vm => (ConfigViewModel)DataContext!;
        private bool _built;

        public ConfigWindow()
        {
            InitializeComponent();
            Opened += (_, _) =>
            {
                Vm.Load();
                BuildEditor();
                Hook();
            };
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        private void Hook()
        {
            this.FindControl<Button>("BrowseButton")!.Click += async (_, _) =>
            {
                var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                {
                    Title = "Select your Silent Hill 2 (PC) install folder",
                    AllowMultiple = false,
                });
                if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } path)
                {
                    Vm.GameDirectory = path;
                    Vm.Load();
                    RefreshSelections();
                }
            };

            this.FindControl<Button>("SaveButton")!.Click += (_, _) => Vm.Save();
            this.FindControl<Button>("ResetButton")!.Click += (_, _) =>
            {
                Vm.ResetDefaults();
                RefreshSelections();
            };
            this.FindControl<Button>("SaveLaunchButton")!.Click += async (_, _) =>
            {
                string msg = Vm.SaveAndLaunch();
                if (msg.Length > 0)
                    await ShowMessage("Save & Launch", msg);
            };
        }

        private void BuildEditor()
        {
            var tabs = this.FindControl<TabControl>("ConfigTabs")!;
            if (_built) { RefreshSelections(); return; }

            var items = new List<TabItem>();
            foreach (var tab in Vm.ConfigTabs)
            {
                var sectionStack = new StackPanel { Spacing = 16, Margin = new(8) };
                foreach (var section in tab.Sections)
                {
                    var secStack = new StackPanel { Spacing = 10 };
                    secStack.Children.Add(new TextBlock
                    {
                        Text = section.Name, FontWeight = FontWeight.Bold, FontSize = 15,
                    });
                    foreach (var feature in section.Features)
                    {
                        if (Vm.FeatureVms.TryGetValue(feature.Name, out var fvm))
                            secStack.Children.Add(BuildFeature(fvm));
                    }
                    sectionStack.Children.Add(new Border
                    {
                        Padding = new(12), CornerRadius = new(6),
                        Background = new SolidColorBrush(Color.FromArgb(0x18, 0, 0, 0)),
                        Child = secStack,
                    });
                }
                items.Add(new TabItem { Header = tab.Name, Content = new ScrollViewer { Content = sectionStack } });
            }
            tabs.ItemsSource = items;
            _built = true;
        }

        private Control BuildFeature(FeatureViewModel fvm)
        {
            bool isSpeedrunMode = fvm.Feature.Name == SpeedrunMode.FeatureName;
            var stack = new StackPanel { Spacing = 4 };
            var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };

            if (fvm.IsCheckbox)
            {
                var cb = new CheckBox
                {
                    Content = fvm.Title, IsChecked = fvm.IsChecked, IsEnabled = !fvm.IsLocked, Tag = fvm,
                };
                cb.IsCheckedChanged += (_, _) => fvm.IsChecked = cb.IsChecked ?? false;
                header.Children.Add(cb);
            }
            else
            {
                header.Children.Add(new TextBlock
                {
                    Text = fvm.Title, VerticalAlignment = VerticalAlignment.Center, MinWidth = 320,
                });
                var combo = new ComboBox
                {
                    ItemsSource = fvm.ChoiceLabels, SelectedIndex = fvm.SelectedIndex,
                    IsEnabled = !fvm.IsLocked, MinWidth = 180, Tag = fvm,
                };
                combo.SelectionChanged += (_, _) =>
                {
                    int previousMode = isSpeedrunMode ? Vm.CurrentSpeedrunMode : 0;
                    fvm.SelectedIndex = combo.SelectedIndex;
                    if (isSpeedrunMode)
                    {
                        string msg = Vm.OnSpeedrunModeChanged(previousMode);
                        RefreshSelections();
                        if (msg.Length > 0) Vm.Status = msg;
                    }
                };
                header.Children.Add(combo);
            }

            stack.Children.Add(header);
            if (!string.IsNullOrWhiteSpace(fvm.Description))
            {
                stack.Children.Add(new TextBlock
                {
                    Text = fvm.Description, TextWrapping = TextWrapping.Wrap,
                    Opacity = 0.65, FontSize = 12, MaxWidth = 720,
                });
            }
            return stack;
        }

        private void RefreshSelections()
        {
            var tabs = this.FindControl<TabControl>("ConfigTabs")!;
            if (tabs.ItemsSource is not IEnumerable<TabItem> items) return;

            foreach (var control in items
                         .Select(i => i.Content).OfType<ScrollViewer>()
                         .Select(sv => sv.Content).OfType<StackPanel>()
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
                        combo.IsEnabled = !fvm.IsLocked || fvm.Feature.Name == SpeedrunMode.FeatureName;
                        break;
                }
            }
        }

        private static IEnumerable<Control> Descendants(Control root)
        {
            if (root is Panel panel)
            {
                foreach (var child in panel.Children)
                {
                    yield return child;
                    foreach (var d in Descendants(child)) yield return d;
                }
            }
            else if (root is Border { Child: Control bc })
            {
                yield return bc;
                foreach (var d in Descendants(bc)) yield return d;
            }
        }

        private async System.Threading.Tasks.Task ShowMessage(string title, string message)
        {
            var dialog = new Window
            {
                Title = title, Width = 460, SizeToContent = SizeToContent.Height,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
            };
            var ok = new Button { Content = "OK", HorizontalAlignment = HorizontalAlignment.Right };
            ok.Click += (_, _) => dialog.Close();
            dialog.Content = new StackPanel
            {
                Margin = new(16), Spacing = 14,
                Children = { new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap }, ok },
            };
            await dialog.ShowDialog(this);
        }
    }
}
