using System.Collections.ObjectModel;
using Avalonia.Media;
using Avalonia.Threading;
using SH2EESetup.Models;
using SH2EESetup.Platform;
using SH2EESetup.Services;
using SH2EESetup.ViewModels;

namespace SH2EESetup.Setup.ViewModels
{
    /// <summary>
    /// Uninstall sits outside the numbered install flow: it is reached from Locate when an
    /// existing installation is found there, and returns to Locate or closes.
    /// </summary>
    public enum WizardStep { Locate, Source, InstallType, Progress, Steam, Uninstall }
    public enum InstallSource { Download, Local }
    public enum InstallKind { Quick, Custom }

    /// <summary>
    /// Drives the setup wizard end to end. Holds all per-step state and the operations
    /// (detect → choose source → choose components → install → optional Add-to-Steam),
    /// reusing the shared Core services.
    /// </summary>
    public class WizardViewModel : BaseViewModel
    {
        private readonly HttpClient _http;
        private readonly ManifestService _manifest;
        private readonly DownloadService _download;
        private readonly ExtractionService _extraction;
        private readonly InstallerService _installer;

        private List<WebComponent> _webComponents = new();
        private List<LocalComponent> _localComponents = new();

        public WizardViewModel()
        {
            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("SH2EE-setup-linux/1.0");
            _manifest = new ManifestService(_http);
            _download = new DownloadService(_http);
            _extraction = new ExtractionService();
            _installer = new InstallerService(_download, _extraction, "1.1.5");
        }

        // ---- Step state --------------------------------------------------------------

        private WizardStep _step = WizardStep.Locate;
        public WizardStep Step
        {
            get => _step;
            private set
            {
                if (SetProperty(ref _step, value))
                {
                    OnPropertyChanged(nameof(StepTitle));
                    OnPropertyChanged(nameof(StepNumberText));
                    OnPropertyChanged(nameof(CanGoBack));
                    OnPropertyChanged(nameof(NextLabel));
                    OnPropertyChanged(nameof(IsFinishStep));
                    RefreshCanGoNext();
                }
            }
        }

        public string StepTitle => Step switch
        {
            WizardStep.Locate => "Find your Silent Hill 2 installation",
            WizardStep.Source => "Choose your installation source",
            WizardStep.InstallType => "Choose what to install",
            WizardStep.Progress => "Installing the Enhanced Edition",
            WizardStep.Steam => "Almost done",
            WizardStep.Uninstall => "Remove the Enhanced Edition",
            _ => "",
        };

        public string StepNumberText => Step == WizardStep.Uninstall
            ? "Uninstall"
            : $"Step {(int)Step + 1} of 5";

        public bool CanGoBack =>
            Step is WizardStep.Source or WizardStep.InstallType ||
            (Step == WizardStep.Uninstall && !IsBusy && !UninstallComplete);

        public bool IsFinishStep => Step == WizardStep.Steam;

        public string NextLabel => Step switch
        {
            WizardStep.InstallType => "Install",
            WizardStep.Steam => "Finish",
            WizardStep.Uninstall => UninstallComplete ? "Close" : "Uninstall",
            _ => "Next",
        };

        private bool _canGoNext;
        public bool CanGoNext
        {
            get => _canGoNext;
            private set => SetProperty(ref _canGoNext, value);
        }

        private bool _isBusy;
        public bool IsBusy
        {
            get => _isBusy;
            set
            {
                if (SetProperty(ref _isBusy, value))
                {
                    OnPropertyChanged(nameof(CanGoBack));
                    RefreshCanGoNext();
                }
            }
        }

        // ---- Step 1: Locate ----------------------------------------------------------

        private string _gameDirectory = "";
        public string GameDirectory
        {
            get => _gameDirectory;
            set
            {
                if (SetProperty(ref _gameDirectory, value))
                {
                    OnPropertyChanged(nameof(IsGameValid));
                    OnPropertyChanged(nameof(HasExistingInstall));
                    OnPropertyChanged(nameof(HasOriginalExeBackup));
                    OnPropertyChanged(nameof(ShowMissingBackupWarning));
                    UpdateDetectionStatus();
                    RefreshCanGoNext();
                }
            }
        }

        public bool IsGameValid => GameEnvironment.IsValidGameDir(GameDirectory);

        private string _detectStatus = "Looking for your Silent Hill 2 installation…";
        public string DetectStatus
        {
            get => _detectStatus;
            set => SetProperty(ref _detectStatus, value);
        }

        private IBrush _detectBrush = Brushes.Goldenrod;
        public IBrush DetectBrush
        {
            get => _detectBrush;
            set => SetProperty(ref _detectBrush, value);
        }

        public ObservableCollection<string> DetectedDirectories { get; } = new();

        public bool HasDetected => DetectedDirectories.Count > 0;

        /// <summary>Runs at startup: auto-detect, or tell the user to pick manually.</summary>
        public void AutoDetect()
        {
            DetectedDirectories.Clear();
            var candidates = GameEnvironment.CandidateGameDirectories();
            foreach (var c in candidates)
                DetectedDirectories.Add(c);
            OnPropertyChanged(nameof(HasDetected));

            if (candidates.Count > 0)
            {
                GameDirectory = candidates[0];
            }
            else
            {
                DetectStatus = "We couldn't find Silent Hill 2 automatically. " +
                               "Click Browse and pick the folder that contains sh2pc.exe " +
                               "(this is where the game is installed).";
                DetectBrush = Brushes.Goldenrod;
            }
        }

        private void UpdateDetectionStatus()
        {
            if (GameDirectory.Length == 0)
                return;
            if (IsGameValid)
            {
                bool data = GameEnvironment.HasDataMarker(GameDirectory);
                DetectStatus = data
                    ? "✓ Great — Silent Hill 2 was found here. You're ready to continue."
                    : "✓ Found sh2pc.exe here. (The game data looks incomplete, but you can still continue.)";
                DetectBrush = Brushes.MediumSeaGreen;
            }
            else
            {
                DetectStatus = "✗ This folder doesn't contain sh2pc.exe. Please pick the folder where Silent Hill 2 is installed.";
                DetectBrush = Brushes.OrangeRed;
            }
        }

        // ---- Step 2: Source ----------------------------------------------------------

        private InstallSource _source = InstallSource.Download;
        public InstallSource Source
        {
            get => _source;
            set
            {
                if (SetProperty(ref _source, value))
                {
                    OnPropertyChanged(nameof(IsLocalSource));
                    OnPropertyChanged(nameof(IsDownloadSource));
                    OnPropertyChanged(nameof(LocalSourceStatus));
                    RefreshCanGoNext();
                }
            }
        }

        public bool IsLocalSource
        {
            get => Source == InstallSource.Local;
            set { if (value) Source = InstallSource.Local; }
        }

        public bool IsDownloadSource
        {
            get => Source == InstallSource.Download;
            set { if (value) Source = InstallSource.Download; }
        }

        private string _localSourceDir = "";
        public string LocalSourceDir
        {
            get => _localSourceDir;
            set
            {
                if (SetProperty(ref _localSourceDir, value))
                {
                    OnPropertyChanged(nameof(LocalSourceStatus));
                    RefreshCanGoNext();
                }
            }
        }

        public string LocalSourceStatus
        {
            get
            {
                if (!IsLocalSource || LocalSourceDir.Length == 0)
                    return "";
                var comps = ManifestService.ReadLocalManifest(LocalSourceDir);
                return comps.Count > 0
                    ? $"✓ Found a local manifest with {comps.Count} component(s)."
                    : "✗ No local_sh2ee.dat found in this folder.";
            }
        }

        // ---- Step 3: Install type ----------------------------------------------------

        private InstallKind _kind = InstallKind.Quick;
        public InstallKind Kind
        {
            get => _kind;
            set
            {
                if (SetProperty(ref _kind, value))
                {
                    OnPropertyChanged(nameof(IsCustom));
                    OnPropertyChanged(nameof(IsQuick));
                }
            }
        }

        public bool IsCustom
        {
            get => Kind == InstallKind.Custom;
            set { if (value) Kind = InstallKind.Custom; }
        }

        public bool IsQuick
        {
            get => Kind == InstallKind.Quick;
            set { if (value) Kind = InstallKind.Quick; }
        }

        public ObservableCollection<ComponentViewModel> Components { get; } = new();

        /// <summary>Loads the component list for the chosen source (called entering step 3).</summary>
        public async Task<string?> LoadComponentsAsync()
        {
            Components.Clear();
            try
            {
                if (Source == InstallSource.Download)
                {
                    _webComponents = await _manifest.FetchComponentsAsync();
                    bool maintenance = ManifestService.IsMaintenanceInstall(GameDirectory);
                    var installed = ManifestService.ReadInstalled(GameDirectory)
                        .ToDictionary(c => c.Id, c => c, StringComparer.OrdinalIgnoreCase);

                    foreach (var comp in _webComponents)
                    {
                        if (comp.Id == ComponentIds.SetupTool)
                            continue;
                        installed.TryGetValue(comp.Id, out var local);
                        bool mandatory = ComponentIds.Mandatory.Contains(comp.Id);
                        Components.Add(new ComponentViewModel
                        {
                            Component = comp,
                            Description = ComponentDescriptions.Get(comp.Id),
                            IsInstalled = local?.IsInstalled ?? false,
                            InstalledVersion = local?.IsInstalled == true ? local.Version : null,
                            IsMandatory = mandatory && !maintenance,
                            IsSelected = true,
                            IsEnabled = !(mandatory && !maintenance),
                        });
                    }
                }
                else
                {
                    _localComponents = ManifestService.ReadLocalManifest(LocalSourceDir)
                        .Where(c => c.Id != ComponentIds.SetupTool).ToList();
                    foreach (var lc in _localComponents)
                    {
                        Components.Add(new ComponentViewModel
                        {
                            // Adapt the local row to the shared ComponentViewModel shape.
                            Component = new WebComponent
                            {
                                Id = lc.Id, Name = lc.Name, Version = lc.Version,
                                Url = lc.FileName, Sha256 = "notUsed",
                            },
                            Description = ComponentDescriptions.Get(lc.Id),
                            IsSelected = true,
                            IsEnabled = true,
                        });
                    }
                }
                return null;
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        // ---- Step 4: Progress / install ----------------------------------------------

        private double _progress;
        public double Progress
        {
            get => _progress;
            set => SetProperty(ref _progress, value);
        }

        private string _progressStatus = "";
        public string ProgressStatus
        {
            get => _progressStatus;
            set => SetProperty(ref _progressStatus, value);
        }

        private bool _installComplete;
        public bool InstallComplete
        {
            get => _installComplete;
            set { if (SetProperty(ref _installComplete, value)) RefreshCanGoNext(); }
        }

        public ChecksumMismatchHandler? ChecksumMismatchPrompt { get; set; }

        public async Task RunInstallAsync()
        {
            IsBusy = true;
            InstallComplete = false;
            Progress = 0;
            var progress = new Progress<InstallProgress>(p => Dispatcher.UIThread.Post(() =>
            {
                ProgressStatus = $"[{p.ComponentIndex}/{p.ComponentCount}] {p.Phase} {p.ComponentName}…";
                Progress = p.Percent;
            }));

            try
            {
                if (Source == InstallSource.Download)
                {
                    var selected = Components.Where(c => c.IsSelected || Kind == InstallKind.Quick)
                        .Select(c => c.Component).ToList();
                    await _installer.InstallAsync(GameDirectory, selected, progress, ChecksumMismatchPrompt);
                }
                else
                {
                    var ids = Components.Where(c => c.IsSelected || Kind == InstallKind.Quick)
                        .Select(c => c.Component.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    var selected = _localComponents.Where(c => ids.Contains(c.Id)).ToList();
                    await _installer.InstallLocalAsync(GameDirectory, selected, LocalSourceDir, progress);
                }

                ProgressStatus = "All components installed successfully.";
                Progress = 100;
                InstallComplete = true;
            }
            catch (Exception ex)
            {
                ProgressStatus = $"✗ Installation failed: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        // ---- Step 5: Steam -----------------------------------------------------------

        private bool _addToSteam = true;
        public bool AddToSteam
        {
            get => _addToSteam;
            set => SetProperty(ref _addToSteam, value);
        }

        /// <summary>Defined in Core so uninstall matches on exactly the name install wrote.</summary>
        public const string SteamAppName = SteamShortcuts.DefaultAppName;

        /// <summary>
        /// Performs the Add-to-Steam action if chosen, returning a message to show the user.
        /// </summary>
        public string FinishSteam()
        {
            if (!AddToSteam)
                return "";

            string exe = Path.Combine(GameDirectory, GameEnvironment.GameExe);
            try
            {
                var gameId = SteamShortcuts.AddShortcut(
                    SteamAppName, exe, GameDirectory, DllOverrideService.SteamLaunchOption);
                if (gameId == null)
                    return "We couldn't find Steam, so the game wasn't added. You can add it " +
                           "manually later from the Configuration app.";
            }
            catch (Exception ex)
            {
                return $"Couldn't add the game to Steam: {ex.Message}";
            }

            return "\"" + SteamAppName + "\" was added to Steam, with the required launch " +
                   "options already filled in.\n\n" +
                   "Two quick things to finish up in Steam:\n" +
                   "  1.  Fully restart Steam so the new game appears.\n" +
                   "  2.  Open the game's Properties → Compatibility, tick \"Force the use of a " +
                   "specific Steam Play compatibility tool\", and choose Proton — Proton-GE or " +
                   "Proton Experimental work best.";
        }

        /// <summary>Launches the bundled configuration app for the installed game.</summary>
        public void LaunchConfigApp()
        {
            ConfigLauncher.Launch(GameDirectory);
        }

        // ---- Uninstall ---------------------------------------------------------------

        /// <summary>Whether the located folder has an SH2:EE install worth offering to remove.</summary>
        public bool HasExistingInstall => UninstallService.IsInstalled(GameDirectory);

        public bool HasOriginalExeBackup => UninstallService.HasOriginalExeBackup(GameDirectory);

        /// <summary>Drives the "your original .exe can't be restored" banner on the uninstall step.</summary>
        public bool ShowMissingBackupWarning => HasExistingInstall && !HasOriginalExeBackup;

        private bool _uninstallGameFiles = true;
        public bool UninstallGameFiles
        {
            get => _uninstallGameFiles;
            set { if (SetProperty(ref _uninstallGameFiles, value)) OnUninstallSelectionChanged(); }
        }

        private bool _uninstallDllOverrides = true;
        public bool UninstallDllOverrides
        {
            get => _uninstallDllOverrides;
            set { if (SetProperty(ref _uninstallDllOverrides, value)) OnUninstallSelectionChanged(); }
        }

        private bool _uninstallSteamShortcut = true;
        public bool UninstallSteamShortcut
        {
            get => _uninstallSteamShortcut;
            set { if (SetProperty(ref _uninstallSteamShortcut, value)) OnUninstallSelectionChanged(); }
        }

        /// <summary>Unticking every box must disable the Uninstall button, not run a no-op.</summary>
        private void OnUninstallSelectionChanged()
        {
            OnPropertyChanged(nameof(UninstallPlan));
            RefreshCanGoNext();
        }

        private string _uninstallStatus = "";
        public string UninstallStatus
        {
            get => _uninstallStatus;
            set => SetProperty(ref _uninstallStatus, value);
        }

        private string _uninstallSummary = "";
        public string UninstallSummary
        {
            get => _uninstallSummary;
            set => SetProperty(ref _uninstallSummary, value);
        }

        private bool _uninstallHadWarnings;
        public bool UninstallHadWarnings
        {
            get => _uninstallHadWarnings;
            set => SetProperty(ref _uninstallHadWarnings, value);
        }

        private bool _uninstallComplete;
        public bool UninstallComplete
        {
            get => _uninstallComplete;
            set
            {
                if (SetProperty(ref _uninstallComplete, value))
                {
                    OnPropertyChanged(nameof(NextLabel));
                    OnPropertyChanged(nameof(CanGoBack));
                    RefreshCanGoNext();
                }
            }
        }

        /// <summary>A plain-language list of exactly what the Uninstall button will do.</summary>
        public string UninstallPlan
        {
            get
            {
                var parts = new List<string>();
                if (UninstallGameFiles)
                    parts.Add("Enhanced Edition files in " + GameDirectory);
                if (UninstallDllOverrides)
                    parts.Add("Wine DLL overrides for this game's prefix");
                if (UninstallSteamShortcut)
                    parts.Add("the \"" + SteamAppName + "\" Steam shortcut");
                return parts.Count == 0
                    ? "Nothing is selected, so nothing will be removed."
                    : "This will remove:\n  •  " + string.Join("\n  •  ", parts);
            }
        }

        public void StartUninstall()
        {
            UninstallComplete = false;
            UninstallHadWarnings = false;
            UninstallSummary = "";
            UninstallStatus = "";
            OnPropertyChanged(nameof(HasOriginalExeBackup));
            OnPropertyChanged(nameof(ShowMissingBackupWarning));
            SetStep(WizardStep.Uninstall);
        }

        public async Task RunUninstallAsync()
        {
            IsBusy = true;
            UninstallStatus = "Starting…";

            var options = new UninstallOptions
            {
                RemoveGameFiles = UninstallGameFiles,
                RemoveDllOverrides = UninstallDllOverrides,
                RemoveSteamShortcut = UninstallSteamShortcut,
            };
            var progress = new Progress<string>(s =>
                Dispatcher.UIThread.Post(() => UninstallStatus = s));

            try
            {
                // Snapshot the directory: the field is bindable and must not be read from
                // the worker thread.
                string dir = GameDirectory;
                var report = await Task.Run(() => UninstallService.Run(dir, options, progress));

                var sections = new List<string>();
                if (report.Done.Count > 0)
                    sections.Add(string.Join("\n", report.Done.Select(d => "•  " + d)));
                if (report.Warnings.Count > 0)
                    sections.Add(string.Join("\n\n", report.Warnings.Select(w => "⚠  " + w)));

                UninstallSummary = sections.Count > 0
                    ? string.Join("\n\n", sections)
                    : "There was nothing to remove.";
                UninstallHadWarnings = report.Warnings.Count > 0;
                UninstallStatus = report.Succeeded
                    ? "The Enhanced Edition has been removed."
                    : "Uninstall finished, but not everything could be removed.";
            }
            catch (Exception ex)
            {
                UninstallStatus = "✗ Uninstall failed.";
                UninstallSummary = ex.Message;
                UninstallHadWarnings = true;
            }
            finally
            {
                UninstallComplete = true;
                IsBusy = false;
                OnPropertyChanged(nameof(HasExistingInstall));
                OnPropertyChanged(nameof(IsGameValid));
            }
        }

        // ---- Navigation --------------------------------------------------------------

        public void GoBack()
        {
            Step = Step switch
            {
                WizardStep.Source => WizardStep.Locate,
                WizardStep.InstallType => WizardStep.Source,
                WizardStep.Uninstall => WizardStep.Locate,
                _ => Step,
            };
        }

        public void SetStep(WizardStep step) => Step = step;

        public void RefreshCanGoNext()
        {
            CanGoNext = !IsBusy && Step switch
            {
                WizardStep.Locate => IsGameValid,
                WizardStep.Source => Source == InstallSource.Download ||
                                     (LocalSourceDir.Length > 0 &&
                                      ManifestService.ReadLocalManifest(LocalSourceDir).Count > 0),
                WizardStep.InstallType => true,
                WizardStep.Progress => InstallComplete,
                WizardStep.Steam => true,
                // Before running, Next is the Uninstall trigger (a confirmation dialog still
                // stands between it and any deletion); after, it just closes the window.
                WizardStep.Uninstall => UninstallComplete || UninstallGameFiles ||
                                        UninstallDllOverrides || UninstallSteamShortcut,
                _ => false,
            };
        }
    }
}
