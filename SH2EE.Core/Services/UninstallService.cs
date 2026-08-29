using SH2EESetup.Platform;

namespace SH2EESetup.Services
{
    /// <summary>Which parts of an SH2:EE installation to tear down.</summary>
    public sealed class UninstallOptions
    {
        /// <summary>Delete the mod's files from the game folder and restore the original .exe.</summary>
        public bool RemoveGameFiles { get; init; } = true;

        /// <summary>Strip the DllOverrides keys this tool wrote into the Wine prefix.</summary>
        public bool RemoveDllOverrides { get; init; } = true;

        /// <summary>Remove the non-Steam shortcut from every logged-in Steam account.</summary>
        public bool RemoveSteamShortcut { get; init; } = true;

        /// <summary>Delete leftover component archives from the temp folder.</summary>
        public bool RemoveTempDownloads { get; init; } = true;
    }

    /// <summary>
    /// A record of what uninstall actually did, so the user gets told rather than being
    /// shown a bare "done". Warnings are things worth knowing about, not failures — the
    /// uninstall carries on regardless.
    /// </summary>
    public sealed class UninstallReport
    {
        public List<string> Done { get; } = new();
        public List<string> Warnings { get; } = new();

        /// <summary>False only when the game files step could not run at all.</summary>
        public bool Succeeded { get; internal set; } = true;
    }

    /// <summary>
    /// Removes an SH2:EE installation and the traces it leaves outside the game folder.
    ///
    /// Install writes to three places — the game folder, the Wine prefix's user.reg, and
    /// Steam's shortcuts.vdf — and only the first has ever been undone. Each step here is
    /// independent and best-effort: one failing must not strand the others, because a
    /// half-removed install is worse than either extreme.
    /// </summary>
    public static class UninstallService
    {
        /// <summary>
        /// Whether there is an SH2:EE install here worth removing. Deliberately broader than
        /// <see cref="ManifestService.IsMaintenanceInstall"/>: a partial or interrupted install
        /// may have no SH2EEsetup.dat, and that is exactly when someone wants to clean up.
        /// </summary>
        public static bool IsInstalled(string gameDir)
        {
            if (string.IsNullOrWhiteSpace(gameDir) || !Directory.Exists(gameDir))
                return false;

            return File.Exists(Path.Combine(gameDir, ManifestService.SetupDatFileName))
                || Directory.Exists(Path.Combine(gameDir, "sh2e"))
                || File.Exists(Path.Combine(gameDir, "d3d8.dll"));
        }

        /// <summary>
        /// Whether the original sh2pc.exe was backed up and can be restored. Without it the
        /// Enhanced Edition executable stays behind, so the UI warns before going ahead.
        /// </summary>
        public static bool HasOriginalExeBackup(string gameDir) =>
            !string.IsNullOrWhiteSpace(gameDir) &&
            File.Exists(Path.Combine(gameDir, GameEnvironment.GameExe + ".bak"));

        /// <summary>
        /// Runs the uninstall. Synchronous and quick (file deletes only) but worth running off
        /// the UI thread, since deleting the sh2e tree can take a moment on a slow disk.
        /// </summary>
        public static UninstallReport Run(
            string gameDir, UninstallOptions options, IProgress<string>? progress = null)
        {
            var report = new UninstallReport();

            // Hard guard. Everything below deletes files inside gameDir, so refuse outright
            // unless this really is a Silent Hill 2 folder. A mistyped path must never cost
            // someone a directory.
            if (!GameEnvironment.IsValidGameDir(gameDir))
            {
                report.Succeeded = false;
                report.Warnings.Add(
                    $"\"{gameDir}\" doesn't contain {GameEnvironment.GameExe}, so nothing was removed.");
                return report;
            }

            if (options.RemoveGameFiles)
                RemoveGameFiles(gameDir, report, progress);

            if (options.RemoveDllOverrides)
                RemoveDllOverrides(gameDir, report, progress);

            if (options.RemoveSteamShortcut)
                RemoveSteamShortcut(gameDir, report, progress);

            if (options.RemoveTempDownloads)
                RemoveTempDownloads(report, progress);

            return report;
        }

        private static void RemoveGameFiles(
            string gameDir, UninstallReport report, IProgress<string>? progress)
        {
            progress?.Report("Removing Enhanced Edition files…");
            bool hadBackup = HasOriginalExeBackup(gameDir);

            try
            {
                InstallerService.Uninstall(gameDir);
            }
            catch (Exception ex)
            {
                report.Succeeded = false;
                report.Warnings.Add($"Some game files could not be removed: {ex.Message}");
                return;
            }

            report.Done.Add("Removed the Enhanced Edition files from the game folder.");

            if (hadBackup)
            {
                report.Done.Add("Restored your original sh2pc.exe.");
            }
            else
            {
                report.Warnings.Add(
                    "No backup of the original sh2pc.exe was found, so the Enhanced Edition " +
                    "executable is still in place. The game will still run, but to get a " +
                    "completely stock install you'll need to restore sh2pc.exe from your " +
                    "original disc or GOG copy.");
            }
        }

        private static void RemoveDllOverrides(
            string gameDir, UninstallReport report, IProgress<string>? progress)
        {
            progress?.Report("Clearing Wine DLL overrides…");

            string? prefix = GameEnvironment.TryFindPrefixRoot(gameDir);
            if (prefix == null)
            {
                // Expected whenever the game lives outside a prefix and was launched with
                // WINEDLLOVERRIDES in the launch options instead. Nothing was written, so
                // there is nothing to undo — not worth troubling the user about.
                return;
            }

            if (DllOverrideService.TryRemoveFromPrefix(prefix, out string message))
                report.Done.Add("Cleared the Wine DLL overrides from the game's prefix.");
            else
                report.Warnings.Add($"Couldn't clear the Wine DLL overrides: {message}");
        }

        private static void RemoveSteamShortcut(
            string gameDir, UninstallReport report, IProgress<string>? progress)
        {
            progress?.Report("Removing the Steam shortcut…");

            try
            {
                string exe = Path.Combine(gameDir, GameEnvironment.GameExe);
                int removed = SteamShortcuts.RemoveShortcut(SteamShortcuts.DefaultAppName, exe);
                if (removed > 0)
                {
                    report.Done.Add(removed == 1
                        ? "Removed the Steam shortcut. Restart Steam for it to disappear."
                        : $"Removed the Steam shortcut from {removed} Steam accounts. " +
                          "Restart Steam for them to disappear.");
                }
            }
            catch (Exception ex)
            {
                report.Warnings.Add($"Couldn't remove the Steam shortcut: {ex.Message}");
            }
        }

        private static void RemoveTempDownloads(UninstallReport report, IProgress<string>? progress)
        {
            progress?.Report("Cleaning up downloaded files…");

            try
            {
                string temp = InstallerService.TempDownloadDir;
                if (Directory.Exists(temp))
                {
                    Directory.Delete(temp, recursive: true);
                    report.Done.Add("Deleted the leftover downloaded component archives.");
                }
            }
            catch
            {
                // Temp files are the operating system's problem if we can't clear them.
            }
        }
    }
}
