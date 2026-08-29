using System.IO.Compression;
using SH2EESetup.Models;
using SH2EESetup.Platform;

namespace SH2EESetup.Services
{
    /// <summary>What a repackage run produced, and anything the user should know about it.</summary>
    public sealed class RepackageReport
    {
        public List<string> Archives { get; } = new();
        public List<string> Warnings { get; } = new();

        public int FilesPackaged { get; internal set; }
        public int FilesMissing { get; internal set; }
        public int FilesModified { get; internal set; }

        /// <summary>Every packaged file matched upstream's CRC-32 and none were missing.</summary>
        public bool ExactMatch => FilesMissing == 0 && FilesModified == 0;
    }

    /// <summary>
    /// Rebuilds component archives from an installation already on disk, so a set of offline
    /// installation files can be produced without re-downloading ~4 GB from the SH2:EE servers.
    ///
    /// This works for two reasons. Offline installs don't verify checksums — InstallLocalAsync
    /// passes "notUsed" — so a rebuilt archive need not be byte-identical to upstream's, which
    /// it never could be (zip timestamps and compression settings differ). And
    /// <see cref="ComponentFileMap"/> supplies the per-component file lists that the
    /// installation itself never recorded.
    ///
    /// The map also carries upstream's own CRC-32 per file, so every file is verified as it is
    /// packaged. That is a stronger check than hashing the finished archive would be: it
    /// compares the actual bytes on disk against what upstream shipped, file by file. CRC-32
    /// catches corruption and edits, not deliberate tampering — it is an integrity check, not
    /// a security one.
    /// </summary>
    public static class RepackageService
    {
        /// <summary>
        /// Files that legitimately differ from what upstream shipped, and must not be reported
        /// as modified. d3d8.ini is the settings file the config app rewrites; the .bak is our
        /// own copy of the user's original executable.
        /// </summary>
        private static readonly HashSet<string> MutableFiles =
            new(StringComparer.OrdinalIgnoreCase) { "d3d8.ini", "sh2pc.exe.bak" };

        /// <summary>Whether there is an installation here that the map can account for.</summary>
        public static bool CanRepackage(string gameDir) =>
            GameEnvironment.IsValidGameDir(gameDir) &&
            ComponentFileMap.InstalledAndMapped(gameDir).Count > 0;

        /// <summary>
        /// Rebuilds an archive per installed component into <paramref name="outputDir"/> and
        /// writes local_sh2ee.dat beside them. Cancellation leaves whatever finished, and the
        /// manifest still describes it, matching the download path's behaviour.
        /// </summary>
        public static async Task<RepackageReport> RunAsync(
            string gameDir,
            string outputDir,
            IProgress<InstallProgress>? progress = null,
            CancellationToken ct = default)
        {
            var report = new RepackageReport();

            if (!GameEnvironment.IsValidGameDir(gameDir))
            {
                report.Warnings.Add(
                    $"\"{gameDir}\" doesn't contain {GameEnvironment.GameExe}, so there is " +
                    "nothing to repackage.");
                return report;
            }

            var mapped = ComponentFileMap.InstalledAndMapped(gameDir);
            if (mapped.Count == 0)
            {
                report.Warnings.Add(
                    "None of the installed components could be matched to a known package, so " +
                    "there is nothing to rebuild. Use the download option instead.");
                return report;
            }

            ReportUnmappable(gameDir, mapped, report);
            Directory.CreateDirectory(outputDir);

            var packaged = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                for (int i = 0; i < mapped.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    var component = mapped[i];

                    progress?.Report(new InstallProgress
                    {
                        ComponentName = component.Name,
                        ComponentIndex = i + 1,
                        ComponentCount = mapped.Count,
                        Phase = "Packaging",
                        Percent = 0,
                    });

                    if (ComponentFileMap.IsVersionStale(gameDir, component))
                    {
                        report.Warnings.Add(
                            $"{component.Name} is installed at a different version than this " +
                            $"tool knows about ({component.Version}), so its file list may be " +
                            "out of date. The package was still built. Downloading this one " +
                            "instead would give a guaranteed-complete copy.");
                    }

                    if (await PackageComponentAsync(gameDir, outputDir, component, report, progress, i, mapped.Count, ct))
                        packaged.Add(component.Id);
                }
            }
            finally
            {
                // Describe whatever was produced, even after a cancellation — the same
                // contract the download path offers.
                ManifestService.WriteLocalManifest(outputDir, ToWebComponents(mapped), packaged);
            }

            return report;
        }

        private static async Task<bool> PackageComponentAsync(
            string gameDir, string outputDir, MappedComponent component,
            RepackageReport report, IProgress<InstallProgress>? progress,
            int index, int count, CancellationToken ct)
        {
            string archivePath = Path.Combine(outputDir, component.ArchiveFileName);
            var missing = new List<string>();
            var modified = new List<string>();
            int written = 0;

            try
            {
                // Build into a temporary file so a cancelled or failed archive never looks
                // like a complete one.
                string temp = archivePath + ".partial";
                if (File.Exists(temp))
                    File.Delete(temp);

                await Task.Run(() =>
                {
                    using var zip = ZipFile.Open(temp, ZipArchiveMode.Create);
                    for (int f = 0; f < component.Files.Count; f++)
                    {
                        ct.ThrowIfCancellationRequested();
                        var mappedFile = component.Files[f];
                        string source = Path.Combine(
                            gameDir, mappedFile.Path.Replace('/', Path.DirectorySeparatorChar));

                        if (!File.Exists(source))
                        {
                            missing.Add(mappedFile.Path);
                            continue;
                        }

                        if (!MutableFiles.Contains(Path.GetFileName(mappedFile.Path)) &&
                            Crc32.OfFile(source) != mappedFile.Crc32)
                        {
                            modified.Add(mappedFile.Path);
                        }

                        zip.CreateEntryFromFile(source, mappedFile.Path, CompressionLevel.Fastest);
                        written++;

                        if (f % 8 == 0)
                        {
                            progress?.Report(new InstallProgress
                            {
                                ComponentName = component.Name,
                                ComponentIndex = index + 1,
                                ComponentCount = count,
                                Phase = "Packaging",
                                Percent = (f + 1) * 100.0 / component.Files.Count,
                            });
                        }
                    }
                }, ct);

                if (File.Exists(archivePath))
                    File.Delete(archivePath);
                File.Move(temp, archivePath);
            }
            catch (OperationCanceledException)
            {
                TryDelete(archivePath + ".partial");
                throw;
            }
            catch (Exception ex)
            {
                TryDelete(archivePath + ".partial");
                report.Warnings.Add($"Couldn't package {component.Name}: {ex.Message}");
                return false;
            }

            report.FilesPackaged += written;
            report.FilesMissing += missing.Count;
            report.FilesModified += modified.Count;
            report.Archives.Add($"{component.ArchiveFileName} — {written} file(s)");

            if (missing.Count > 0)
            {
                report.Warnings.Add(
                    $"{component.Name}: {missing.Count} file(s) were expected but not found on " +
                    $"disk (e.g. {missing[0]}). They are absent from the archive, so installing " +
                    "from it won't restore them.");
            }

            if (modified.Count > 0)
            {
                report.Warnings.Add(
                    $"{component.Name}: {modified.Count} file(s) don't match the checksums the " +
                    $"Enhanced Edition project published (e.g. {modified[0]}). They were " +
                    "packaged exactly as they are on this PC, which is expected if you've " +
                    "modified them yourself.");
            }

            return true;
        }

        /// <summary>Flags installed components the map can't account for at all.</summary>
        private static void ReportUnmappable(
            string gameDir, List<MappedComponent> mapped, RepackageReport report)
        {
            var known = mapped.Select(m => m.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var unmapped = ManifestService.ReadInstalled(gameDir)
                .Where(c => c.IsInstalled && c.Id != ComponentIds.SetupTool && !known.Contains(c.Id))
                .Select(c => c.Id)
                .ToList();

            if (unmapped.Count > 0)
            {
                report.Warnings.Add(
                    "These installed components aren't recognised by this tool and were " +
                    $"skipped: {string.Join(", ", unmapped)}. Download them separately if you " +
                    "need a complete set.");
            }
        }

        /// <summary>
        /// Adapts the map to the shape WriteLocalManifest expects. Url carries the archive
        /// name so WebComponent.FileName resolves without any network metadata.
        /// </summary>
        private static List<WebComponent> ToWebComponents(IEnumerable<MappedComponent> mapped) =>
            mapped.Select(m => new WebComponent
            {
                Id = m.Id,
                Name = m.Name,
                Version = m.Version,
                Url = m.ArchiveFileName,
                Sha256 = "notUsed",
            }).ToList();

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }
    }
}
