using System.Security.Cryptography;
using SH2EESetup.Models;

namespace SH2EESetup.Services
{
    /// <summary>
    /// Downloads component archives to a temp directory and verifies their SHA256
    /// against the manifest (skipping verification when the manifest says "notUsed").
    /// </summary>
    public class DownloadService
    {
        private readonly HttpClient _http;

        public DownloadService(HttpClient http)
        {
            _http = http;
        }

        /// <summary>
        /// Downloads <paramref name="component"/> into <paramref name="destDir"/>, reporting
        /// 0-100 progress. Returns the full path to the downloaded archive.
        /// </summary>
        public async Task<string> DownloadAsync(
            WebComponent component,
            string destDir,
            IProgress<double>? progress,
            CancellationToken ct = default)
        {
            Directory.CreateDirectory(destDir);
            string destPath = Path.Combine(destDir, component.FileName);

            using var response = await _http.GetAsync(
                component.Url, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            long? total = response.Content.Headers.ContentLength;
            await using var source = await response.Content.ReadAsStreamAsync(ct);
            await using (var dest = new FileStream(
                destPath, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, true))
            {
                var buffer = new byte[1 << 16];
                long readTotal = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, ct)) > 0)
                {
                    await dest.WriteAsync(buffer.AsMemory(0, read), ct);
                    readTotal += read;
                    if (total is > 0)
                        progress?.Report(readTotal * 100.0 / total.Value);
                }
            }

            progress?.Report(100);
            return destPath;
        }

        /// <summary>
        /// Verifies a downloaded file's checksum. Returns true when the manifest entry
        /// is "notUsed" (no verification requested) or the hash matches.
        /// </summary>
        public static async Task<bool> VerifyChecksumAsync(
            string filePath, string expectedSha256, CancellationToken ct = default)
        {
            if (string.Equals(expectedSha256, "notUsed", StringComparison.OrdinalIgnoreCase))
                return true;

            await using var stream = File.OpenRead(filePath);
            byte[] hash = await SHA256.HashDataAsync(stream, ct);
            string actual = Convert.ToHexString(hash);
            return string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase);
        }
    }
}
