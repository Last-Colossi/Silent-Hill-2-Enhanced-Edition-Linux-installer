using System.Collections.ObjectModel;
using System.Reflection;
using Avalonia.Media;
using Avalonia.Threading;
using SH2EESetup.Models;
using SH2EESetup.Platform;
using SH2EESetup.Services;

namespace SH2EESetup.ViewModels
{
    public class MainViewModel : BaseViewModel
    {
        private readonly HttpClient _http;
        private readonly ManifestService _manifest;
        private readonly DownloadService _download;
        private readonly ExtractionService _extraction;
        private readonly InstallerService _installer;
        private readonly ConfigService _config = new();

        private List<WebComponent> _webComponents = new();
        private ConfigDocument? _schema;
        private List<KeyValuePair<string, string>> _extraConfigOptions = new();

        public MainViewModel()
        {
            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("SH2EE-setup-linux/1.0");
            _manifest = new ManifestService(_http);
            _download = new DownloadService(_http);
            _extraction = new ExtractionService();
            _installer = new InstallerService(_download, _extraction, AppVersion);

            DllOverrideLaunchOption = DllOverrideService.SteamLaunchOption;
        }

        public static string AppVersion =>
            Assembly.GetExecutingAssembly().GetName().Version is { } v
                ? $"{v.Major}.{v.Minor}.{v.Build}"
                : "1.1.5";

        // ---- Shared game-directory state ---------------------------------------------

        private string _gameDirectory = "";
        public string GameDirectory
        {
            get => _gameDirectory;
            set
            {
                if (SetProperty(ref _gameDirectory, value))
                {
                    OnPropertyChanged(nameof(IsGameValid));
                    RefreshGameStatus();
                }
            }
        }

        public bool IsGameValid => GameEnvironment.IsValidGameDir(GameDirectory);

        private string _gameStatus = "Select your Silent Hill 2 (PC) install folder to begin.";
        public string GameStatus
        {
            get => _gameStatus;
            set => SetProperty(ref _gameStatus, value);
        }

        private IBrush _gameStatusBrush = Brushes.Goldenrod;
        public IBrush GameStatusBrush
        {
            get => _gameStatusBrush;
            set => SetProperty(ref _gameStatusBrush, value);
        }

        public ObservableCollection<string> DetectedDirectories { get; } = new();

        // ---- Installer / maintenance state -------------------------------------------

        public ObservableCollection<ComponentViewModel> Components { get; } = new();

        private bool _isMaintenanceMode;
        public bool IsMaintenanceMode
        {
            get => _isMaintenanceMode;
            set => SetProperty(ref _isMaintenanceMode, value);
        }

        private string _status = "Ready.";
        public string Status
        {
            get => _status;
            set => SetProperty(ref _status, value);
        }

        private double _progress;
        public double Progress
        {
            get => _progress;
            set => SetProperty(ref _progress, value);
        }

        private bool _isBusy;
        public bool IsBusy
        {
            get => _isBusy;
            set => SetProperty(ref _isBusy, value);
        }

        // ---- Config editor state -----------------------------------------------------

        public ObservableCollection<ConfigTab> ConfigTabs { get; } = new();
        private readonly Dictionary<string, FeatureViewModel> _featureVms = new(StringComparer.Ordinal);
        public IReadOnlyDictionary<string, FeatureViewModel> FeatureVms => _featureVms;
        public ConfigDocument? Schema => _schema;

        // ---- DLL override / Proton state ---------------------------------------------

        public string DllOverrideLaunchOption { get; }

        private string _prefixStatus = "";
        public string PrefixStatus
        {
            get => _prefixStatus;
            set => SetProperty(ref _prefixStatus, value);
        }

        /// <summary>
        /// Set by the view to resolve checksum mismatches interactively (Retry/Skip/Abort).
        /// </summary>
        public ChecksumMismatchHandler? ChecksumMismatchPrompt { get; set; }

        // ---- Operations --------------------------------------------------------------

        /// <summary>Validates a manually chosen folder and adopts it as the game directory.</summary>
        public void SetGameDirectory(string dir) => GameDirectory = dir;

        public void RunAutoDetect()
        {
            DetectedDirectories.Clear();
            Status = "Scanning common Wine/Proton prefixes…";
            var candidates = GameEnvironment.CandidateGameDirectories();
            foreach (var c in candidates)
                DetectedDirectories.Add(c);

            if (candidates.Count == 1 && !IsGameValid)
                GameDirectory = candidates[0];

            Status = candidates.Count switch
            {
                0 => "No installs found automatically — pick the folder manually.",
                1 => "Found one candidate install.",
                _ => $"Found {candidates.Count} candidate installs — choose one above.",
            };
        }

        private void RefreshGameStatus()
        {
            if (!IsGameValid)
            {
                GameStatus = "Not a valid SH2 install (no sh2pc.exe found here).";
                GameStatusBrush = Brushes.OrangeRed;
                IsMaintenanceMode = false;
                Components.Clear();
                return;
            }

            IsMaintenanceMode = ManifestService.IsMaintenanceInstall(GameDirectory);
            bool hasData = GameEnvironment.HasDataMarker(GameDirectory);

            GameStatus = IsMaintenanceMode
                ? "SH2: Enhanced Edition is installed here — maintenance mode."
                : hasData
                    ? "Valid SH2 install detected — ready for a fresh install."
                    : "sh2pc.exe found, but game data files look incomplete.";
            GameStatusBrush = IsMaintenanceMode ? Brushes.MediumSeaGreen : Brushes.CornflowerBlue;

            // Prefix / DLL-override status.
            string? prefix = GameEnvironment.TryFindPrefixRoot(GameDirectory);
            PrefixStatus = prefix != null
                ? $"Wine prefix detected: {prefix}"
                : "Game is not inside a detectable Wine prefix — use the launch-option method below.";
        }

        public async Task RefreshManifestAsync()
        {
            IsBusy = true;
            Status = "Fetching component manifest…";
            try
            {
                _webComponents = await _manifest.FetchComponentsAsync();
                var installed = ManifestService.ReadInstalled(GameDirectory)
                    .ToDictionary(c => c.Id, c => c, StringComparer.OrdinalIgnoreCase);

                Components.Clear();
                foreach (var comp in _webComponents)
                {
                    if (comp.Id == ComponentIds.SetupTool)
                        continue;

                    installed.TryGetValue(comp.Id, out var local);
                    bool mandatory = ComponentIds.Mandatory.Contains(comp.Id);

                    var vm = new ComponentViewModel
                    {
                        Component = comp,
                        Description = ComponentDescriptions.Get(comp.Id),
                        IsInstalled = local?.IsInstalled ?? false,
                        InstalledVersion = local?.IsInstalled == true ? local.Version : null,
                        IsMandatory = mandatory && !IsMaintenanceMode,
                    };

                    // Maintenance: pre-check only components with an update available.
                    // Fresh install defaults are applied by ApplyDefaultSelections() below.
                    if (IsMaintenanceMode)
                        vm.IsSelected = vm.UpdateAvailable;

                    Components.Add(vm);
                }

                ApplyDefaultSelections();
                Status = $"Loaded {Components.Count} components.";
            }
            catch (Exception ex)
            {
                Status = $"Failed to load manifest: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>
        /// Fresh install: force mandatory components on and lock them; pre-check the wine
        /// stub (we are on Linux). Maintenance: leave the update-driven selection.
        /// </summary>
        private void ApplyDefaultSelections()
        {
            if (IsMaintenanceMode)
                return;

            foreach (var vm in Components)
            {
                if (vm.IsMandatory)
                {
                    vm.IsSelected = true;
                    vm.IsEnabled = false;
                }
                else if (vm.Component.Id == ComponentIds.WineStub)
                {
                    vm.IsSelected = true; // Always wanted on Linux.
                }
                else
                {
                    vm.IsSelected = true; // "full" install default
                }
            }
        }

        public async Task InstallSelectedAsync()
        {
            if (!IsGameValid)
            {
                Status = "Choose a valid game folder first.";
                return;
            }

            var selected = Components.Where(c => c.IsSelected).Select(c => c.Component).ToList();
            if (selected.Count == 0)
            {
                Status = "No components selected.";
                return;
            }

            IsBusy = true;
            Progress = 0;
            try
            {
                var progress = new Progress<InstallProgress>(p =>
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        Status = $"[{p.ComponentIndex}/{p.ComponentCount}] {p.Phase} {p.ComponentName}…";
                        Progress = p.Percent;
                    });
                });

                await _installer.InstallAsync(GameDirectory, selected, progress, ChecksumMismatchPrompt);
                Status = $"Done. Installed/updated {selected.Count} component(s).";
                Progress = 100;

                // Refresh installed-state badges.
                await RefreshManifestAsync();
                RefreshGameStatus();
            }
            catch (Exception ex)
            {
                Status = $"Install failed: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>
        /// Offline install from a folder containing local_sh2ee.dat and the component
        /// archives it references. Returns false if no local manifest was found.
        /// </summary>
        public async Task<bool> InstallOfflineAsync(string sourceDir)
        {
            if (!IsGameValid)
            {
                Status = "Choose a valid game folder first.";
                return false;
            }

            var localComps = ManifestService.ReadLocalManifest(sourceDir);
            if (localComps.Count == 0)
            {
                Status = $"No {ManifestService.LocalManifestFileName} found in the selected folder.";
                return false;
            }

            // Exclude the setup_tool pseudo-entry; install the rest that have a file present.
            var toInstall = localComps.Where(c => c.Id != ComponentIds.SetupTool).ToList();

            IsBusy = true;
            Progress = 0;
            try
            {
                var progress = new Progress<InstallProgress>(p => Dispatcher.UIThread.Post(() =>
                {
                    Status = $"[{p.ComponentIndex}/{p.ComponentCount}] {p.Phase} {p.ComponentName}…";
                    Progress = p.Percent;
                }));

                await _installer.InstallLocalAsync(GameDirectory, toInstall, sourceDir, progress);
                Status = $"Offline install complete ({toInstall.Count} component(s)).";
                Progress = 100;
                RefreshGameStatus();
                return true;
            }
            catch (Exception ex)
            {
                Status = $"Offline install failed: {ex.Message}";
                return false;
            }
            finally
            {
                IsBusy = false;
            }
        }

        public void Uninstall()
        {
            if (!IsGameValid) return;
            IsBusy = true;
            try
            {
                InstallerService.Uninstall(GameDirectory);
                Status = "SH2: Enhanced Edition removed; original sh2pc.exe restored.";
                Components.Clear();
                RefreshGameStatus();
            }
            catch (Exception ex)
            {
                Status = $"Uninstall failed: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        public string ApplyDllOverrides()
        {
            string? prefix = GameEnvironment.TryFindPrefixRoot(GameDirectory);
            if (prefix == null)
                return "No Wine prefix found around the game folder. Use the launch-option method instead.";

            bool ok = DllOverrideService.TryApplyToPrefix(prefix, out string message);
            Status = message;
            return message + (ok ? "\n\nRestart the game for the overrides to take effect." : "");
        }

        // ---- Steam integration -------------------------------------------------------

        public const string SteamAppName = "Silent Hill 2: Enhanced Edition";

        private ulong? _steamGameId;

        /// <summary>
        /// Adds SH2 as a non-Steam game with the DLL-override launch option pre-filled.
        /// Returns a user-facing message describing the result.
        /// </summary>
        public string AddToSteam()
        {
            if (!IsGameValid)
                return "Choose a valid game folder first.";

            string exe = Path.Combine(GameDirectory, GameEnvironment.GameExe);
            try
            {
                _steamGameId = SteamShortcuts.AddShortcut(
                    SteamAppName, exe, GameDirectory, DllOverrideService.SteamLaunchOption);
            }
            catch (Exception ex)
            {
                Status = $"Failed to add Steam shortcut: {ex.Message}";
                return Status;
            }

            if (_steamGameId == null)
            {
                Status = "Steam install not found — is Steam installed for this user?";
                return Status;
            }

            Status = "Added to Steam.";
            return "Added \"" + SteamAppName + "\" to Steam.\n\n" +
                   "Restart Steam, then set its compatibility tool to Proton " +
                   "(Properties → Compatibility → Force Proton). The DLL-override launch " +
                   "option has been filled in automatically.";
        }

        /// <summary>Launches SH2 through Steam (so Proton wraps it). Requires the Steam shortcut.</summary>
        public string LaunchViaSteam()
        {
            if (!IsGameValid)
                return "Choose a valid game folder first.";

            string exe = Path.Combine(GameDirectory, GameEnvironment.GameExe);
            ulong gameId = _steamGameId
                ?? SteamShortcuts.RunGameId(SteamShortcuts.ComputeAppId(exe, SteamAppName));

            string url = SteamShortcuts.RunGameUrl(gameId);
            if (!UrlLauncher.Open(url))
            {
                Status = "Could not launch via Steam.";
                return "Could not open " + url + ". Make sure Steam is running and the game " +
                       "has been added to Steam (use \"Add to Steam\" first).";
            }

            Status = "Launching via Steam…";
            return "";
        }

        // ---- Config editor -----------------------------------------------------------

        public void LoadConfigSchema()
        {
            if (_schema != null) return;
            _schema = _config.LoadSchema();
            _featureVms.Clear();
            ConfigTabs.Clear();
            foreach (var tab in _schema.Tabs)
            {
                ConfigTabs.Add(tab);
                foreach (var feat in tab.Sections.SelectMany(s => s.Features))
                    _featureVms[feat.Name] = new FeatureViewModel(feat);
            }
        }

        public void LoadConfigValues()
        {
            LoadConfigSchema();
            if (_schema == null) return;

            var current = ConfigService.ReadCurrentValues(GameDirectory);
            _extraConfigOptions = ConfigService.ReadExtraOptions(GameDirectory, _schema);

            foreach (var (name, vm) in _featureVms)
            {
                if (current != null && current.TryGetValue(name, out var val))
                    vm.SetFromIniValue(val);
                else
                    vm.ResetToDefault();
            }

            ApplySpeedrunState();
            Status = current != null
                ? "Loaded settings from d3d8.ini."
                : "No d3d8.ini yet — showing defaults.";
        }

        public bool SaveConfig()
        {
            if (_schema == null) return false;
            if (!File.Exists(Path.Combine(GameDirectory, "d3d8.dll")) && !IsMaintenanceMode)
            {
                Status = "Install the SH2 Enhancements module before saving settings.";
                return false;
            }

            var values = _featureVms.ToDictionary(kv => kv.Key, kv => kv.Value.CurrentValue);
            ConfigService.Save(GameDirectory, _schema, values, _extraConfigOptions);
            Status = "Saved d3d8.ini.";
            return true;
        }

        /// <summary>Save & Launch: writes d3d8.ini then launches the game through Steam.</summary>
        public string SaveAndLaunch()
        {
            if (!SaveConfig())
                return Status;
            return LaunchViaSteam();
        }

        public void ResetConfigDefaults()
        {
            foreach (var vm in _featureVms.Values)
                vm.ResetToDefault();
            ApplySpeedrunState();
            Status = "Settings reset to defaults (not yet saved).";
        }

        /// <summary>The current SpeedrunMode value (0/1/2) from the editor.</summary>
        private int CurrentSpeedrunMode =>
            _featureVms.TryGetValue(SpeedrunMode.FeatureName, out var vm) &&
            int.TryParse(vm.CurrentValue, out var v) ? v : 0;

        /// <summary>
        /// Reacts to a change in the SpeedrunMode dropdown: locks/unlocks speedrun-toggleable
        /// features and forces their speedrun values, mirroring the upstream config tool.
        /// Returns the message to surface to the user (empty if nothing changed).
        /// </summary>
        public string OnSpeedrunModeChanged(int previousMode)
        {
            int mode = CurrentSpeedrunMode;
            if (mode == previousMode)
                return "";

            bool enabling = previousMode == SpeedrunMode.Disabled && mode != SpeedrunMode.Disabled;
            bool disabling = mode == SpeedrunMode.Disabled && previousMode != SpeedrunMode.Disabled;
            bool switching = !enabling && !disabling;

            foreach (var vm in _featureVms.Values)
            {
                if (!vm.IsSpeedrunToggleable)
                    continue;

                if (disabling)
                {
                    vm.ResetToDefault();
                    vm.IsLocked = false;
                }
                else
                {
                    vm.ApplySpeedrunDefault(mode, srAlreadyActive: switching);
                    vm.IsLocked = true;
                }
            }

            return disabling
                ? "Speedrun Mode disabled — settings unlocked and reset to defaults."
                : "Speedrun Mode enabled — affected settings have been locked.";
        }

        /// <summary>Applies the lock state to match the loaded SpeedrunMode (used after load/reset).</summary>
        public void ApplySpeedrunState()
        {
            bool active = CurrentSpeedrunMode != SpeedrunMode.Disabled;
            foreach (var vm in _featureVms.Values)
                if (vm.IsSpeedrunToggleable)
                    vm.IsLocked = active;
        }
    }
}
