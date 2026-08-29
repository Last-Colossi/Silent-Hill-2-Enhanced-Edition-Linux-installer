using System.Collections.ObjectModel;
using Avalonia.Media;
using SH2EESetup.Models;
using SH2EESetup.Platform;
using SH2EESetup.Services;
using SH2EESetup.ViewModels;

namespace SH2EESetup.Config.ViewModels
{
    /// <summary>
    /// Standalone configuration editor: loads the d3d8.ini settings schema, applies a
    /// game's current values, edits them (including Speedrun Mode lock-down), and saves
    /// back in the upstream format. Reused across the embedded settings schema.
    /// </summary>
    public class ConfigViewModel : BaseViewModel
    {
        private readonly ConfigService _config = new();
        private ConfigDocument? _schema;
        private List<KeyValuePair<string, string>> _extra = new();

        private readonly Dictionary<string, FeatureViewModel> _featureVms = new(StringComparer.Ordinal);
        public IReadOnlyDictionary<string, FeatureViewModel> FeatureVms => _featureVms;
        public ObservableCollection<ConfigTab> ConfigTabs { get; } = new();

        public ConfigViewModel(string? gameDirectory)
        {
            GameDirectory = ResolveGameDir(gameDirectory);
        }

        /// <summary>
        /// Most specific source wins: the directory the wizard handed us, then the one the
        /// user last worked with, and only then a scan. The middle case is what makes a
        /// standalone launch open on the right game immediately — and it covers installs
        /// nested too deeply for the depth-limited scan to reach.
        /// </summary>
        private static string ResolveGameDir(string? supplied)
        {
            if (!string.IsNullOrWhiteSpace(supplied) && GameEnvironment.IsValidGameDir(supplied))
                return supplied!;

            return AppStateService.GetRememberedGameDirectory()
                ?? GameEnvironment.CandidateGameDirectories().FirstOrDefault()
                ?? "";
        }

        private string _gameDirectory = "";
        public string GameDirectory
        {
            get => _gameDirectory;
            set
            {
                if (SetProperty(ref _gameDirectory, value))
                {
                    OnPropertyChanged(nameof(IsGameValid));
                    OnPropertyChanged(nameof(HasGame));
                    // Browsing to a different install here should carry over to the wizard.
                    // The state service ignores anything that isn't a real game folder.
                    AppStateService.RememberGameDirectory(value);
                    UpdateHeader();
                }
            }
        }

        public bool IsGameValid => GameEnvironment.IsValidGameDir(GameDirectory);
        public bool HasGame => IsGameValid;

        private string _header = "";
        public string Header
        {
            get => _header;
            set => SetProperty(ref _header, value);
        }

        private string _status = "";
        public string Status
        {
            get => _status;
            set => SetProperty(ref _status, value);
        }

        private void UpdateHeader() =>
            Header = IsGameValid
                ? $"Editing: {Path.Combine(GameDirectory, "d3d8.ini")}"
                : "No Silent Hill 2 install selected.";

        // ---- Schema + values ---------------------------------------------------------

        public void Load()
        {
            if (_schema == null)
            {
                _schema = _config.LoadSchema();
                foreach (var tab in _schema.Tabs)
                {
                    ConfigTabs.Add(tab);
                    foreach (var feat in tab.Sections.SelectMany(s => s.Features))
                        _featureVms[feat.Name] = new FeatureViewModel(feat);
                }
            }

            if (!IsGameValid)
            {
                Status = "Pick your Silent Hill 2 folder to edit its settings.";
                return;
            }

            var current = ConfigService.ReadCurrentValues(GameDirectory);
            _extra = ConfigService.ReadExtraOptions(GameDirectory, _schema);
            foreach (var (name, vm) in _featureVms)
            {
                if (current != null && current.TryGetValue(name, out var val))
                    vm.SetFromIniValue(val);
                else
                    vm.ResetToDefault();
            }
            ApplySpeedrunState();
            Status = current != null ? "Loaded settings from d3d8.ini." : "No d3d8.ini yet — showing defaults.";
        }

        // ---- Speedrun mode -----------------------------------------------------------

        public int CurrentSpeedrunMode =>
            _featureVms.TryGetValue(SpeedrunMode.FeatureName, out var vm) &&
            int.TryParse(vm.CurrentValue, out var v) ? v : 0;

        public string OnSpeedrunModeChanged(int previousMode)
        {
            int mode = CurrentSpeedrunMode;
            if (mode == previousMode) return "";

            bool disabling = mode == SpeedrunMode.Disabled && previousMode != SpeedrunMode.Disabled;
            bool switching = mode != SpeedrunMode.Disabled && previousMode != SpeedrunMode.Disabled;

            foreach (var vm in _featureVms.Values)
            {
                if (!vm.IsSpeedrunToggleable) continue;
                if (disabling) { vm.ResetToDefault(); vm.IsLocked = false; }
                else { vm.ApplySpeedrunDefault(mode, switching); vm.IsLocked = true; }
            }

            return disabling
                ? "Speedrun Mode disabled — settings unlocked and reset to defaults."
                : "Speedrun Mode enabled — affected settings locked.";
        }

        public void ApplySpeedrunState()
        {
            bool active = CurrentSpeedrunMode != SpeedrunMode.Disabled;
            foreach (var vm in _featureVms.Values)
                if (vm.IsSpeedrunToggleable)
                    vm.IsLocked = active;
        }

        // ---- Save / launch -----------------------------------------------------------

        public bool Save()
        {
            if (_schema == null) return false;
            if (!IsGameValid)
            {
                Status = "No valid game folder selected.";
                return false;
            }
            var values = _featureVms.ToDictionary(kv => kv.Key, kv => kv.Value.CurrentValue);
            ConfigService.Save(GameDirectory, _schema, values, _extra);
            Status = "Saved d3d8.ini.";
            return true;
        }

        public string SaveAndLaunch()
        {
            if (!Save()) return Status;

            string exe = Path.Combine(GameDirectory, GameEnvironment.GameExe);
            ulong gameId = SteamShortcuts.RunGameId(SteamShortcuts.ComputeAppId(exe, "Silent Hill 2: Enhanced Edition"));
            if (UrlLauncher.Open(SteamShortcuts.RunGameUrl(gameId)))
            {
                Status = "Launching via Steam…";
                return "";
            }
            Status = "Could not launch via Steam.";
            return "Could not launch through Steam. Make sure the game has been added to Steam " +
                   "(via the Setup app) and that Steam is running.";
        }

        public void ResetDefaults()
        {
            foreach (var vm in _featureVms.Values)
                vm.ResetToDefault();
            ApplySpeedrunState();
            Status = "Settings reset to defaults (not yet saved).";
        }
    }
}
