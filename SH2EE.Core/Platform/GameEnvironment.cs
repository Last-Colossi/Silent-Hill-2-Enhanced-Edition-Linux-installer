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

            try
            {
                foreach (var root in PrefixSearchRoots())
                {
                    if (!Directory.Exists(root))
                        continue;
                    ScanForGameExe(root, depth: 0, maxDepth: 6, found, seen);
                }
            }
            catch
            {
                // The scan is only a convenience: whatever it turned up before going wrong
                // is still useful, and manual Browse covers the rest. Never let it take the
                // wizard down on startup.
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
            {
                if (IsDriveMappingDir(sub))
                    continue;
                ScanForGameExe(sub, depth + 1, maxDepth, found, seen);
            }
        }

        /// <summary>
        /// A Wine prefix's <c>dosdevices</c> folder holds nothing but drive-letter symlinks,
        /// and <c>z:</c> always points at the host filesystem root. Descending into it walks
        /// the scan straight out of the prefix and across the whole system — which is how
        /// looking for sh2pc.exe ended up reading <c>/proc/&lt;pid&gt;/map_files</c>. The real
        /// files live under drive_c, which we scan directly, so there is nothing to lose here.
        /// </summary>
        private static bool IsDriveMappingDir(string dir) =>
            string.Equals(Path.GetFileName(dir), "dosdevices", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Options for the scan. <see cref="EnumerationOptions.IgnoreInaccessible"/> keeps a
        /// single unreadable entry from aborting a listing; <c>AttributesToSkip = 0</c> keeps
        /// the default <see cref="Directory.EnumerateDirectories(string)"/> behaviour of
        /// visiting hidden folders (prefixes live in places like <c>~/.wine</c>).
        /// </summary>
        private static readonly EnumerationOptions ScanOptions = new()
        {
            IgnoreInaccessible = true,
            AttributesToSkip = 0,
        };

        /// <summary>
        /// Lists the sub-directories of <paramref name="dir"/>, skipping any we can't read.
        ///
        /// The listing has to be materialised *inside* the try. Directory enumeration is
        /// lazy, so simply returning the enumerable moved the actual I/O — and therefore any
        /// UnauthorizedAccessException — out to the caller's foreach, where nothing caught
        /// it. That is not hypothetical: a directory such as /proc/&lt;pid&gt;/map_files opens
        /// fine and then fails on the first read, so the throw lands in MoveNext().
        /// </summary>
        private static List<string> SafeEnumerateDirs(string dir)
        {
            var subdirs = new List<string>();
            try
            {
                foreach (var sub in Directory.EnumerateDirectories(dir, "*", ScanOptions))
                    subdirs.Add(sub);
            }
            catch
            {
                // Unreadable or vanished directory — keep whatever we managed to list.
            }
            return subdirs;
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
