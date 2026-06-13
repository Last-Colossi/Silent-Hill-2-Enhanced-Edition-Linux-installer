using System.Text;

namespace SH2EESetup.Services
{
    /// <summary>
    /// Minimal reader/updater for the d3d8.ini format used by the SH2:EE module.
    /// Keys are unique across the whole file (the config tool keys options by name, not
    /// section), so a flat name→value map is sufficient for lookups and value restores.
    /// </summary>
    public static class IniFile
    {
        /// <summary>
        /// Returns a flat map of option name → value for every "key = value" line,
        /// ignoring comments, blank lines and section headers.
        /// </summary>
        public static Dictionary<string, string> ReadFlatValues(string iniText)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var rawLine in iniText.Replace("\r\n", "\n").Split('\n'))
            {
                var line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('['))
                    continue;

                int eq = line.IndexOf('=');
                if (eq <= 0)
                    continue;

                string key = line[..eq].Trim();
                string value = line[(eq + 1)..].Trim();
                if (key.Length > 0)
                    result[key] = value;
            }
            return result;
        }

        /// <summary>
        /// Rewrites <paramref name="iniText"/> in place, replacing the value of any
        /// "key = value" line whose key appears in <paramref name="savedValues"/>. All
        /// comments, ordering and unknown keys are preserved. Mirrors the upstream
        /// installer restoring backed-up settings into a freshly-extracted ini.
        /// </summary>
        public static string RestoreValues(string iniText, IReadOnlyDictionary<string, string> savedValues)
        {
            bool crlf = iniText.Contains("\r\n");
            var lines = iniText.Replace("\r\n", "\n").Split('\n');

            for (int i = 0; i < lines.Length; i++)
            {
                var trimmed = lines[i].Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith(';') || trimmed.StartsWith('['))
                    continue;

                int eq = trimmed.IndexOf('=');
                if (eq <= 0)
                    continue;

                string key = trimmed[..eq].Trim();
                if (savedValues.TryGetValue(key, out var saved))
                    lines[i] = $"{key} = {saved}";
            }

            return string.Join(crlf ? "\r\n" : "\n", lines);
        }

        /// <summary>
        /// Writes a complete d3d8.ini from scratch in the upstream config tool's format:
        /// an optional preface, then per-section blocks where every option is preceded by
        /// its description as a comment. <paramref name="extra"/> holds unknown keys the
        /// user added manually, appended verbatim.
        /// </summary>
        public static string Build(
            string? preface,
            IEnumerable<IniSection> sections,
            IEnumerable<KeyValuePair<string, string>> extra)
        {
            var sb = new StringBuilder();
            if (!string.IsNullOrEmpty(preface))
                sb.Append(preface).Append("\n\n");

            foreach (var sec in sections)
            {
                sb.Append('[').Append(sec.Name).Append("]\n");
                foreach (var opt in sec.Options)
                {
                    if (!string.IsNullOrEmpty(opt.Description))
                        sb.Append("; ").Append(opt.Description.Replace("\n", " ")).Append('\n');
                    sb.Append(opt.Name).Append(" = ").Append(opt.Value).Append("\n\n");
                }
            }

            foreach (var kv in extra)
                sb.Append(kv.Key).Append(" = ").Append(kv.Value).Append('\n');

            return sb.ToString();
        }
    }

    public class IniSection
    {
        public required string Name { get; init; }
        public List<IniOption> Options { get; init; } = new();
    }

    public class IniOption
    {
        public required string Name { get; init; }
        public required string Value { get; init; }
        public string Description { get; init; } = "";
    }
}
