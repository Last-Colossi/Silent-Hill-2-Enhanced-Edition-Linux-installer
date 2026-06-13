using System.Diagnostics;
using System.IO.Compression;

namespace SH2EESetup.Services
{
    /// <summary>
    /// Extracts a component archive into the game directory. The upstream packages are
    /// all .zip, handled natively; any other format falls back to a system 7z/7za binary.
    /// </summary>
    public class ExtractionService
    {
        /// <summary>
        /// Extracts <paramref name="archivePath"/> into <paramref name="gameDir"/>, overwriting
        /// existing files, and reports 0-100 progress across the entries.
        /// </summary>
        public async Task ExtractAsync(
            string archivePath,
            string gameDir,
            IProgress<double>? progress,
            CancellationToken ct = default)
        {
            string ext = Path.GetExtension(archivePath).ToLowerInvariant();
            if (ext == ".zip")
                await Task.Run(() => ExtractZip(archivePath, gameDir, progress, ct), ct);
            else
                await ExtractWith7zAsync(archivePath, gameDir, ct);

            progress?.Report(100);
        }

        private static void ExtractZip(
            string archivePath, string gameDir, IProgress<double>? progress, CancellationToken ct)
        {
            using var archive = ZipFile.OpenRead(archivePath);
            int count = archive.Entries.Count;
            int done = 0;

            foreach (var entry in archive.Entries)
            {
                ct.ThrowIfCancellationRequested();

                // Normalise the Windows-style backslashes some archives use.
                string relative = entry.FullName.Replace('\\', '/');
                string destPath = Path.GetFullPath(Path.Combine(gameDir, relative));

                // Guard against path traversal ("zip slip").
                string gameRoot = Path.GetFullPath(gameDir) + Path.DirectorySeparatorChar;
                if (!destPath.StartsWith(gameRoot, StringComparison.Ordinal))
                    throw new IOException($"Archive entry escapes target directory: {entry.FullName}");

                if (relative.EndsWith('/') || entry.Name.Length == 0)
                {
                    Directory.CreateDirectory(destPath);
                }
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                    entry.ExtractToFile(destPath, overwrite: true);
                }

                done++;
                progress?.Report(done * 100.0 / count);
            }
        }

        private static async Task ExtractWith7zAsync(string archivePath, string gameDir, CancellationToken ct)
        {
            string? exe = Find7z();
            if (exe == null)
                throw new InvalidOperationException(
                    "This archive is not a .zip and no 7z/7za binary was found on PATH. " +
                    "Install p7zip (e.g. `rpm-ostree install p7zip` or via Flatpak) and retry.");

            var psi = new ProcessStartInfo(exe)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            psi.ArgumentList.Add("x");
            psi.ArgumentList.Add(archivePath);
            psi.ArgumentList.Add("-o" + gameDir);
            psi.ArgumentList.Add("-y");

            using var proc = Process.Start(psi)
                ?? throw new InvalidOperationException($"Failed to start {exe}.");
            await proc.WaitForExitAsync(ct);
            if (proc.ExitCode != 0)
            {
                string err = await proc.StandardError.ReadToEndAsync(ct);
                throw new InvalidOperationException($"7z extraction failed (exit {proc.ExitCode}): {err}");
            }
        }

        private static string? Find7z()
        {
            string[] names = { "7z", "7za", "7zz" };
            string path = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (var dir in path.Split(Path.PathSeparator))
            {
                foreach (var name in names)
                {
                    string candidate = Path.Combine(dir, name);
                    if (File.Exists(candidate))
                        return candidate;
                }
            }
            return null;
        }
    }
}
