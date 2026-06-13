using System.Diagnostics;

namespace SH2EESetup.Platform
{
    /// <summary>
    /// Locating the Silent Hill 2 PC install and its Wine/Proton prefix on Linux.
    ///
    /// SH2 PC (2002) is not a Steam title — users install it offline from disc/GOG/backup
    /// into an arbitrary folder (Documents, Downloads, a Lutris prefix, a non-Steam Steam
    /// shortcut, …). So manual selection of the game directory is the primary path; the
    /// auto-scan below only offers convenience suggestions.
    /// </summary>
    public static class GameEnvironment
    {
        /// <summary>The main game executable; its presence marks a valid install folder.</summary>
        public const string GameExe = "sh2pc.exe";

        /// <summary>
        /// Secondary marker the upstream installer checks for. Confirms the folder really
        /// holds extracted SH2 game data and not just a stray sh2pc.exe.
        /// </summary>
        public const string DataMarker = "data/pic/etc/konami.tex";

        private static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        /// <summary>
        /// A directory is a valid SH2 install if it contains sh2pc.exe. The data marker is
        /// preferred but a freshly-copied disc may not match its casing, so the exe alone
        /// is enough to proceed (the upstream tool warns rather than blocks in that case).
        /// </summary>
        public static bool IsValidGameDir(string dir) =>
            !string.IsNullOrWhiteSpace(dir) &&
            File.Exists(Path.Combine(dir, GameExe));

        public static bool HasDataMarker(string dir) =>
            File.Exists(Path.Combine(dir, DataMarker.Replace('/', Path.DirectorySeparatorChar)));

        /// <summary>
        /// Best-effort scan of common prefix containers for a folder holding sh2pc.exe.
        /// Bounded in depth so it stays fast; never throws. Manual selection covers
        /// everything this misses.
        /// </summary>
        public static List<string> CandidateGameDirectories()
        {
            var found = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var root in PrefixSearchRoots())
            {
                if (!Directory.Exists(root))
                    continue;
                ScanForGameExe(root, depth: 0, maxDepth: 6, found, seen);
            }

            return found;
        }

        /// <summary>
        /// Roots likely to contain a Wine/Proton prefix's drive_c (and therefore an SH2
        /// install run through one of the common launchers).
        /// </summary>
        private static IEnumerable<string> PrefixSearchRoots()
        {
            // Steam (native + Flatpak) non-Steam-shortcut prefixes.
            foreach (var steam in new[]
            {
                Path.Combine(Home, ".local", "share", "Steam"),
                Path.Combine(Home, ".var", "app", "com.valvesoftware.Steam", ".local", "share", "Steam"),
                Path.Combine(Home, ".steam", "steam"),
            })
            {
                string compat = Path.Combine(steam, "steamapps", "compatdata");
                if (Directory.Exists(compat))
                {
                    foreach (var prefix in SafeEnumerateDirs(compat))
                        yield return Path.Combine(prefix, "pfx", "drive_c");
                }
            }

            // Lutris default prefix location.
            yield return Path.Combine(Home, "Games");
            yield return Path.Combine(Home, ".var", "app", "net.lutris.Lutris", "data", "lutris");

            // Heroic / generic Wine prefixes.
            yield return Path.Combine(Home, "Games", "Heroic", "Prefixes");
            yield return Path.Combine(Home, ".wine", "drive_c");

            // Bottles.
            yield return Path.Combine(Home, ".var", "app", "com.usebottles.bottles", "data", "bottles", "bottles");
        }

        private static void ScanForGameExe(
            string dir, int depth, int maxDepth, List<string> found, HashSet<string> seen)
        {
            if (depth > maxDepth)
                return;

            try
            {
                if (File.Exists(Path.Combine(dir, GameExe)) && seen.Add(dir))
                {
                    found.Add(dir);
                    return; // No need to descend further into a confirmed install.
                }
            }
            catch
            {
                return;
            }

            foreach (var sub in SafeEnumerateDirs(dir))
                ScanForGameExe(sub, depth + 1, maxDepth, found, seen);
        }

        private static IEnumerable<string> SafeEnumerateDirs(string dir)
        {
            try
            {
                return Directory.EnumerateDirectories(dir);
            }
            catch
            {
                return Array.Empty<string>();
            }
        }

        /// <summary>
        /// Walks up from the game directory to find the Wine prefix root (the parent of
        /// drive_c). Returns null when the game lives outside any prefix (e.g. a loose copy
        /// in ~/Downloads run via a custom Proton mapping) — in that case the caller should
        /// fall back to the WINEDLLOVERRIDES launch-option approach.
        /// </summary>
        public static string? TryFindPrefixRoot(string gameDir)
        {
            var dir = new DirectoryInfo(Path.GetFullPath(gameDir));
            while (dir != null)
            {
                if (string.Equals(dir.Name, "drive_c", StringComparison.OrdinalIgnoreCase) &&
                    dir.Parent != null &&
                    File.Exists(Path.Combine(dir.Parent.FullName, "system.reg")))
                {
                    return dir.Parent.FullName;
                }
                dir = dir.Parent;
            }
            return null;
        }

        public static bool IsFlatpakSteamPresent() =>
            Directory.Exists(Path.Combine(
                Home, ".var", "app", "com.valvesoftware.Steam", ".local", "share", "Steam"));
    }
}
