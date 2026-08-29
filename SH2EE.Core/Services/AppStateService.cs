using SH2EESetup.Platform;

namespace SH2EESetup.Services
{
    /// <summary>
    /// Remembers the installation the user last worked with, so relaunching either tool lands
    /// on their game instead of re-deriving it.
    ///
    /// This matters beyond saving a second: auto-detect is depth-limited, and an install
    /// nested deeply enough (Heroic's Prefixes/default/&lt;game&gt;/pfx/drive_c/... layout gets
    /// close) can sit outside its reach. Once the user has told us where the game is, there is
    /// no reason to ever guess again.
    ///
    /// Every operation is best-effort. This is a convenience cache, never a source of truth —
    /// a missing, unreadable or stale file must degrade to the old detect-and-ask behaviour
    /// rather than break startup.
    /// </summary>
    public static class AppStateService
    {
        private const string LastGameDirKey = "lastGameDirectory";

        /// <summary>
        /// Under Flatpak this resolves inside the sandbox (~/.var/app/&lt;id&gt;/config), so the
        /// Flatpak and AppImage builds keep separate memories. That is the correct sandbox
        /// behaviour and not worth subverting — the fallback is simply a re-scan.
        ///
        /// DoNotVerify is deliberate. The default option *checks that the directory already
        /// exists* and returns an empty string when it doesn't, which would silently disable
        /// this whole feature on any profile without a ~/.config yet — including a Flatpak's
        /// first run. We create the directory ourselves before writing, so the check is both
        /// unwanted and unnecessary.
        /// </summary>
        public static string? StateFilePath
        {
            get
            {
                try
                {
                    string configRoot = Environment.GetFolderPath(
                        Environment.SpecialFolder.ApplicationData,
                        Environment.SpecialFolderOption.DoNotVerify);
                    return string.IsNullOrEmpty(configRoot)
                        ? null   // No HOME at all: give up rather than guess.
                        : Path.Combine(configRoot, "sh2ee-setup", "state.ini");
                }
                catch
                {
                    return null;
                }
            }
        }

        /// <summary>
        /// The last game directory the user worked with, or null when there isn't one or it no
        /// longer holds Silent Hill 2 (uninstalled game, unmounted drive, deleted folder).
        /// Validated on every read so a stale entry can never send the UI somewhere wrong.
        /// </summary>
        public static string? GetRememberedGameDirectory()
        {
            string? dir = ReadValue(LastGameDirKey);
            return dir != null && GameEnvironment.IsValidGameDir(dir) ? dir : null;
        }

        public static void RememberGameDirectory(string gameDir)
        {
            if (!GameEnvironment.IsValidGameDir(gameDir))
                return;
            WriteValue(LastGameDirKey, gameDir);
        }

        /// <summary>Drops the remembered directory, sending the next launch back to detection.</summary>
        public static void Forget() => WriteValue(LastGameDirKey, null);

        private static string? ReadValue(string key)
        {
            try
            {
                string? path = StateFilePath;
                if (path == null || !File.Exists(path))
                    return null;

                foreach (var rawLine in File.ReadAllLines(path))
                {
                    var line = rawLine.Trim();
                    if (line.Length == 0 || line.StartsWith('#'))
                        continue;

                    int eq = line.IndexOf('=');
                    if (eq <= 0)
                        continue;
                    if (!string.Equals(line[..eq].Trim(), key, StringComparison.OrdinalIgnoreCase))
                        continue;

                    string value = line[(eq + 1)..].Trim();
                    return value.Length > 0 ? value : null;
                }
            }
            catch
            {
                // Unreadable state is no state.
            }
            return null;
        }

        private static void WriteValue(string key, string? value)
        {
            try
            {
                string? path = StateFilePath;
                if (path == null)
                    return;

                var lines = File.Exists(path)
                    ? File.ReadAllLines(path).ToList()
                    : new List<string>();

                // Rewrite the key in place so any other settings stored here survive.
                lines.RemoveAll(l =>
                {
                    int eq = l.IndexOf('=');
                    return eq > 0 &&
                           string.Equals(l[..eq].Trim(), key, StringComparison.OrdinalIgnoreCase);
                });

                if (value != null)
                    lines.Add($"{key}={value}");

                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllLines(path, lines);
            }
            catch
            {
                // Failing to remember is not worth interrupting anyone over.
            }
        }
    }
}
