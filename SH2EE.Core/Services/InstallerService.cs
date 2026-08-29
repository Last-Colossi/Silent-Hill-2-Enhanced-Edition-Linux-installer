using SH2EESetup.Models;
using SH2EESetup.Platform;

namespace SH2EESetup.Services
{
    public class InstallProgress
    {
        public required string ComponentName { get; init; }
        public int ComponentIndex { get; init; }
        public int ComponentCount { get; init; }
        public string Phase { get; init; } = "";    // "Downloading", "Verifying", "Extracting"
        public double Percent { get; init; }        // 0-100 within the current phase
    }

    /// <summary>User's choice when a downloaded component fails checksum verification.</summary>
    public enum ChecksumAction { Retry, Skip, Abort }

    /// <summary>Asked to resolve a checksum mismatch; returns the action to take.</summary>
    public delegate Task<ChecksumAction> ChecksumMismatchHandler(WebComponent component);

    /// <summary>
    /// Orchestrates the download → verify → extract pipeline for selected components,
    /// reproducing the upstream installer's per-component cleanup, .exe backup, and
    /// d3d8.ini settings preservation, then updates SH2EEsetup.dat.
    /// </summary>
    public class InstallerService
    {
        private readonly DownloadService _download;
        private readonly ExtractionService _extraction;
        private readonly string _setupToolVersion;

        public InstallerService(DownloadService download, ExtractionService extraction, string setupToolVersion)
        {
            _download = download;
            _extraction = extraction;
            _setupToolVersion = setupToolVersion;
        }

        /// <summary>Where component archives are staged while downloading.</summary>
        public static string TempDownloadDir =>
            Path.Combine(Path.GetTempPath(), "sh2ee-setup-linux");

        /// <summary>
        /// Installs the given components into <paramref name="gameDir"/>. Existing d3d8.ini
        /// settings are preserved across a module reinstall/update, matching upstream.
        /// </summary>
        public async Task InstallAsync(
            string gameDir,
            IReadOnlyList<WebComponent> selected,
            IProgress<InstallProgress>? progress,
            ChecksumMismatchHandler? onChecksumMismatch = null,
            CancellationToken ct = default)
        {
            string tempDir = TempDownloadDir;
            Directory.CreateDirectory(tempDir);

            var savedValues = CaptureIniSettings(gameDir, selected.Select(c => c.Id));
            var installed = ManifestService.ReadInstalled(gameDir)
                .ToDictionary(c => c.Id, c => c, StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < selected.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var comp = selected[i];

                void Report(string phase, double pct) => progress?.Report(new InstallProgress
                {
                    ComponentName = comp.Name,
                    ComponentIndex = i + 1,
                    ComponentCount = selected.Count,
                    Phase = phase,
                    Percent = pct,
                });

                // Download + verify, with retry/skip on checksum failure.
                string? archive = null;
                bool skip = false;
                while (true)
                {
                    Report("Downloading", 0);
                    archive = await _download.DownloadAsync(
                        comp, tempDir, new Progress<double>(p => Report("Downloading", p)), ct);

                    Report("Verifying", 0);
                    if (await DownloadService.VerifyChecksumAsync(archive, comp.Sha256, ct))
                        break;

                    // Mismatch: ask the caller what to do (default: abort).
                    var action = onChecksumMismatch != null
                        ? await onChecksumMismatch(comp)
                        : ChecksumAction.Abort;

                    TryDelete(archive);
                    if (action == ChecksumAction.Retry)
                        continue;
                    if (action == ChecksumAction.Skip)
                    {
                        skip = true;
                        break;
                    }
                    throw new InvalidDataException(
                        $"Checksum mismatch for {comp.FileName}; installation aborted.");
                }

                if (skip)
                    continue;

                await ApplyArchiveAsync(gameDir, archive!, comp, installed, Report, ct);
                TryDelete(archive);
            }

            RestoreIniSettings(gameDir, savedValues);
            ManifestService.WriteInstalled(gameDir, _setupToolVersion, installed.Values);
        }

        /// <summary>
        /// Offline install: extracts pre-downloaded component archives listed in a
        /// local_sh2ee.dat manifest from <paramref name="sourceDir"/>, with no network
        /// access. Components whose archive file is missing are skipped.
        /// </summary>
        public async Task InstallLocalAsync(
            string gameDir,
            IReadOnlyList<LocalComponent> selected,
            string sourceDir,
            IProgress<InstallProgress>? progress,
            CancellationToken ct = default)
        {
            var savedValues = CaptureIniSettings(gameDir, selected.Select(c => c.Id));
            var installed = ManifestService.ReadInstalled(gameDir)
                .ToDictionary(c => c.Id, c => c, StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < selected.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var local = selected[i];
                string archive = Path.Combine(sourceDir, local.FileName);
                if (!File.Exists(archive))
                    continue;

                void Report(string phase, double pct) => progress?.Report(new InstallProgress
                {
                    ComponentName = local.Name,
                    ComponentIndex = i + 1,
                    ComponentCount = selected.Count,
                    Phase = phase,
                    Percent = pct,
                });

                // Adapt to the shared apply path. Url carries the filename so FileName works;
                // checksum is "notUsed" since the local manifest doesn't carry hashes.
                var comp = new WebComponent
                {
                    Id = local.Id,
                    Name = local.Name,
                    Version = local.Version,
                    Url = local.FileName,
                    Sha256 = "notUsed",
                };

                await ApplyArchiveAsync(gameDir, archive, comp, installed, Report, ct);
            }

            RestoreIniSettings(gameDir, savedValues);
            ManifestService.WriteInstalled(gameDir, _setupToolVersion, installed.Values);
        }

        /// <summary>Shared per-component steps: cleanup/backup, extract, record installed.</summary>
        private async Task ApplyArchiveAsync(
            string gameDir,
            string archive,
            WebComponent comp,
            Dictionary<string, InstalledComponent> installed,
            Action<string, double> report,
            CancellationToken ct)
        {
            PreExtract(gameDir, comp);
            report("Extracting", 0);
            await _extraction.ExtractAsync(
                archive, gameDir, new Progress<double>(p => report("Extracting", p)), ct);

            installed[comp.Id] = new InstalledComponent
            {
                Id = comp.Id,
                IsInstalled = true,
                Version = comp.Version,
            };
        }

        private static Dictionary<string, string>? CaptureIniSettings(string gameDir, IEnumerable<string> ids)
        {
            if (!ids.Contains(ComponentIds.Sh2eModule))
                return null;
            string iniPath = Path.Combine(gameDir, "d3d8.ini");
            return File.Exists(iniPath) ? IniFile.ReadFlatValues(File.ReadAllText(iniPath)) : null;
        }

        private static void RestoreIniSettings(string gameDir, Dictionary<string, string>? savedValues)
        {
            if (savedValues == null)
                return;
            string iniPath = Path.Combine(gameDir, "d3d8.ini");
            if (File.Exists(iniPath))
                File.WriteAllText(iniPath,
                    IniFile.RestoreValues(File.ReadAllText(iniPath), savedValues));
        }

        private static void TryDelete(string? path)
        {
            try { if (path != null && File.Exists(path)) File.Delete(path); }
            catch { }
        }

        /// <summary>
        /// Per-component actions performed before extraction: deleting stale files from
        /// prior versions and backing up the original sh2pc.exe. Lifted from the upstream
        /// wpExtract.iss logic.
        /// </summary>
        private static void PreExtract(string gameDir, WebComponent comp)
        {
            switch (comp.Id)
            {
                case ComponentIds.Sh2eModule:
                    DeleteFiles(gameDir,
                        "d3d8.res", "d3d8.dat", "D3DCompiler_43.dll", "D3DX9_43.dll",
                        "dsound.dll", "dsoal-aldrv.dll",
                        "sh2e/etc/resource/r_menu_e.res", "sh2e/etc/resource/r_menu_f.res",
                        "sh2e/etc/resource/r_menu_g.res", "sh2e/etc/resource/r_menu_i.res",
                        "sh2e/etc/resource/r_menu_j.res", "sh2e/etc/resource/r_menu_s.res");
                    DeleteTree(gameDir, "sh2e/resources");
                    break;

                case ComponentIds.Xidi:
                    DeleteFiles(gameDir,
                        "Dinput.dll", "Dinput8.dll", "XInput1_3.dll", "XInput1_4.dll", "XInputPlus.ini");
                    break;

                case ComponentIds.FmvPack:
                    DeleteTreeContents(gameDir, "sh2e/movie", "*.bik", exclude: "credits.bik");
                    break;

                case ComponentIds.Credits:
                    DeleteFiles(gameDir, "sh2e/movie/credits.bik");
                    break;

                case ComponentIds.EnhancedExe:
                    // Back up the original .exe once before overwriting it.
                    string exe = Path.Combine(gameDir, "sh2pc.exe");
                    string bak = exe + ".bak";
                    if (File.Exists(exe) && !File.Exists(bak))
                        File.Move(exe, bak);
                    break;
            }
        }

        /// <summary>
        /// Removes a SH2:EE installation, restoring the original sh2pc.exe from backup.
        /// Mirrors the upstream CustomUninstall.iss file list.
        /// </summary>
        public static void Uninstall(string gameDir)
        {
            DeleteTree(gameDir, "sh2e");
            DeleteTree(gameDir, "lang");
            DeleteFiles(gameDir,
                "alsoft.ini", "d3d8.cfg", "d3d8.dll", "d3d8.ini", "d3d8.log", "d3d8.res",
                "D3DCompiler_43.dll", "D3DX9_43.dll", "Dinput.dll", "Dinput8.dll",
                "dsoal-aldrv.dll", "dsound.dll", "SH2EEsetup.dat", "SH2EEconfig.exe",
                "SH2EEconfig.xml", "XInput1_3.dll", "XInputPlus.ini", "Xidi.ini");

            // Restore the original executable.
            string exe = Path.Combine(gameDir, "sh2pc.exe");
            string bak = exe + ".bak";
            if (File.Exists(bak))
            {
                if (File.Exists(exe))
                    File.Delete(exe);
                File.Move(bak, exe);
            }
        }

        private static void DeleteFiles(string gameDir, params string[] relativePaths)
        {
            foreach (var rel in relativePaths)
            {
                try
                {
                    string p = Path.Combine(gameDir, rel.Replace('/', Path.DirectorySeparatorChar));
                    if (File.Exists(p))
                        File.Delete(p);
                }
                catch { /* best-effort, matches upstream */ }
            }
        }

        private static void DeleteTree(string gameDir, string relativeDir)
        {
            try
            {
                string p = Path.Combine(gameDir, relativeDir.Replace('/', Path.DirectorySeparatorChar));
                if (Directory.Exists(p))
                    Directory.Delete(p, recursive: true);
            }
            catch { }
        }

        private static void DeleteTreeContents(string gameDir, string relativeDir, string pattern, string? exclude)
        {
            try
            {
                string p = Path.Combine(gameDir, relativeDir.Replace('/', Path.DirectorySeparatorChar));
                if (!Directory.Exists(p))
                    return;
                foreach (var file in Directory.EnumerateFiles(p, pattern))
                {
                    if (exclude != null &&
                        string.Equals(Path.GetFileName(file), exclude, StringComparison.OrdinalIgnoreCase))
                        continue;
                    File.Delete(file);
                }
            }
            catch { }
        }
    }
}
