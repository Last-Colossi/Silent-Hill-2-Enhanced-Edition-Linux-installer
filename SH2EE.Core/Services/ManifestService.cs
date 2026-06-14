using System.Text;
using SH2EESetup.Models;

namespace SH2EESetup.Services
{
    /// <summary>
    /// Fetches and parses the upstream component manifest, and reads/writes the
    /// SH2EEsetup.dat install record in the game directory. Formats match the
    /// Windows setup tool byte-for-byte so the two tools interoperate.
    /// </summary>
    public class ManifestService
    {
        // The repo-hosted pointer file whose first line is the real CSV URL.
        public const string WebCsvPointerUrl =
            "https://raw.githubusercontent.com/elishacloud/Silent-Hill-2-Enhancements/master/Resources/webcsv.url";

        public const string SetupDatFileName = "SH2EEsetup.dat";
        public const string LocalManifestFileName = "local_sh2ee.dat";

        private readonly HttpClient _http;

        public ManifestService(HttpClient http)
        {
            _http = http;
        }

        public async Task<List<WebComponent>> FetchComponentsAsync(CancellationToken ct = default)
        {
            string pointer = await _http.GetStringAsync(WebCsvPointerUrl, ct);
            string csvUrl = pointer.Split('\n')[0].Trim();
            if (csvUrl.Length == 0)
                throw new InvalidDataException("webcsv.url pointer file was empty.");

            string csv = await _http.GetStringAsync(csvUrl, ct);
            var components = ParseWebCsv(csv);
            if (components.Count == 0)
                throw new InvalidDataException("Failed to parse any components from the web manifest.");
            return components;
        }

        public static List<WebComponent> ParseWebCsv(string csv)
        {
            var result = new List<WebComponent>();
            foreach (var rawLine in csv.Split('\n'))
            {
                var line = rawLine.TrimEnd('\r').Trim();
                if (line.Length == 0 || line.StartsWith('#'))
                    continue;

                var cols = line.Split(',');
                if (cols.Length < 5 || cols[0] == "id")
                    continue;

                result.Add(new WebComponent
                {
                    Id = cols[0],
                    Name = cols[1],
                    Version = cols[2],
                    Url = cols[3],
                    Sha256 = cols[4],
                });
            }
            return result;
        }

        /// <summary>
        /// Finds an offline-install manifest (local_sh2ee.dat) in <paramref name="sourceDir"/>
        /// and parses it. Returns an empty list when absent or unparseable.
        /// </summary>
        public static List<LocalComponent> ReadLocalManifest(string sourceDir)
        {
            string path = Path.Combine(sourceDir, LocalManifestFileName);
            return File.Exists(path) ? ParseLocalCsv(File.ReadAllText(path)) : new();
        }

        public static List<LocalComponent> ParseLocalCsv(string csv)
        {
            var result = new List<LocalComponent>();
            foreach (var rawLine in csv.Split('\n'))
            {
                var line = rawLine.TrimEnd('\r').Trim();
                if (line.Length == 0 || line.StartsWith('#'))
                    continue;

                var cols = line.Split(',');
                if (cols.Length < 4 || cols[0] == "id")
                    continue;

                result.Add(new LocalComponent
                {
                    Id = cols[0],
                    Name = cols[1],
                    FileName = cols[2],
                    Version = cols[3],
                });
            }
            return result;
        }

        public static bool IsMaintenanceInstall(string gameDir) =>
            File.Exists(Path.Combine(gameDir, SetupDatFileName)) &&
            Directory.Exists(Path.Combine(gameDir, "data"));

        public static List<InstalledComponent> ReadInstalled(string gameDir)
        {
            var result = new List<InstalledComponent>();
            string path = Path.Combine(gameDir, SetupDatFileName);
            if (!File.Exists(path))
                return result;

            foreach (var rawLine in File.ReadAllLines(path))
            {
                var line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith('#'))
                    continue;

                var cols = line.Split(',');
                if (cols.Length < 3 || cols[0] == "id")
                    continue;

                result.Add(new InstalledComponent
                {
                    Id = cols[0],
                    IsInstalled = string.Equals(cols[1], "true", StringComparison.OrdinalIgnoreCase),
                    Version = cols[2],
                });
            }
            return result;
        }

        /// <summary>
        /// Writes SH2EEsetup.dat in the exact upstream format (CRLF, header lines,
        /// setup_tool entry first).
        /// </summary>
        public static void WriteInstalled(string gameDir, string setupToolVersion, IEnumerable<InstalledComponent> components)
        {
            var sb = new StringBuilder();
            sb.Append("# **DO NOT MODIFY THIS FILE!**\r\n");
            sb.Append("id,isInstalled,version\r\n");
            sb.Append($"setup_tool,true,{setupToolVersion}\r\n");
            foreach (var comp in components)
            {
                if (comp.Id == ComponentIds.SetupTool)
                    continue;
                sb.Append($"{comp.Id},{(comp.IsInstalled ? "true" : "false")},{comp.Version}\r\n");
            }
            File.WriteAllText(Path.Combine(gameDir, SetupDatFileName), sb.ToString());
        }

        /// <summary>
        /// Mirrors the upstream isUpdateAvailable(): a component is updatable when it
        /// is installed and the web version differs from the local one.
        /// </summary>
        public static bool IsUpdateAvailable(WebComponent web, InstalledComponent? local) =>
            local is { IsInstalled: true } &&
            !string.Equals(web.Version, local.Version, StringComparison.OrdinalIgnoreCase);
    }
}
