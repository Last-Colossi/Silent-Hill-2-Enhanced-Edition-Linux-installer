using System.Text;

namespace SH2EESetup.Platform
{
    /// <summary>
    /// Adds Silent Hill 2 as a non-Steam game by editing Steam's binary shortcuts.vdf, so
    /// it can run through Proton (and pick up the d3d8 DLL overrides via launch options).
    ///
    /// SH2 isn't a Steam title, so this is the cleanest way to get a working Proton wrapper
    /// and a one-click launch entry. We also compute the non-Steam "gameid" used by the
    /// steam://rungameid/ launch URL.
    /// </summary>
    public static class SteamShortcuts
    {
        /// <summary>
        /// The shortcut's display name. Install and uninstall must agree on it exactly — it is
        /// half of the match used to find the entry again, and it feeds the appid hash.
        /// </summary>
        public const string DefaultAppName = "Silent Hill 2: Enhanced Edition";

        private static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        private static IEnumerable<string> SteamRoots()
        {
            yield return Path.Combine(Home, ".local", "share", "Steam");
            yield return Path.Combine(Home, ".var", "app", "com.valvesoftware.Steam", ".local", "share", "Steam");
            yield return Path.Combine(Home, ".steam", "steam");
        }

        public static string? FindSteamRoot() =>
            SteamRoots().FirstOrDefault(r => Directory.Exists(Path.Combine(r, "userdata")));

        /// <summary>All Steam user account directories under userdata/.</summary>
        public static List<string> FindUserDataDirs()
        {
            var result = new List<string>();
            string? root = FindSteamRoot();
            if (root == null)
                return result;

            string userdata = Path.Combine(root, "userdata");
            foreach (var dir in Directory.EnumerateDirectories(userdata))
            {
                // Skip the "0"/"anonymous" placeholder accounts.
                string name = Path.GetFileName(dir);
                if (name != "0" && name != "anonymous")
                    result.Add(dir);
            }
            return result;
        }

        /// <summary>
        /// Adds (or updates) a non-Steam shortcut for the given exe in every logged-in
        /// Steam account's shortcuts.vdf. Returns the computed rungameid for launching, or
        /// null when Steam isn't installed. Backs up each shortcuts.vdf once.
        /// </summary>
        public static ulong? AddShortcut(string appName, string exePath, string startDir, string launchOptions)
        {
            var users = FindUserDataDirs();
            if (users.Count == 0)
                return null;

            uint appId = ComputeAppId(exePath, appName);

            foreach (var user in users)
            {
                string configDir = Path.Combine(user, "config");
                Directory.CreateDirectory(configDir);
                string vdfPath = Path.Combine(configDir, "shortcuts.vdf");

                var shortcuts = File.Exists(vdfPath)
                    ? ShortcutsVdf.Parse(File.ReadAllBytes(vdfPath))
                    : new List<Dictionary<string, object>>();

                // Replace an existing entry for the same exe+name, else append.
                shortcuts.RemoveAll(s =>
                    s.TryGetValue("Exe", out var e) && (e as string)?.Trim('"') == exePath.Trim('"') &&
                    s.TryGetValue("AppName", out var n) && (n as string) == appName);

                shortcuts.Add(new Dictionary<string, object>
                {
                    ["appid"] = unchecked((int)appId),
                    ["AppName"] = appName,
                    ["Exe"] = $"\"{exePath}\"",
                    ["StartDir"] = $"\"{startDir}\"",
                    ["icon"] = "",
                    ["ShortcutPath"] = "",
                    ["LaunchOptions"] = launchOptions,
                    ["IsHidden"] = 0,
                    ["AllowDesktopConfig"] = 1,
                    ["AllowOverlay"] = 1,
                    ["OpenVR"] = 0,
                    ["Devkit"] = 0,
                    ["DevkitGameID"] = "",
                    ["DevkitOverrideAppID"] = 0,
                    ["LastPlayTime"] = 0,
                    ["FlatpakAppID"] = "",
                    ["tags"] = new Dictionary<string, object>(),
                });

                if (!File.Exists(vdfPath + ".sh2ee.bak") && File.Exists(vdfPath))
                    File.Copy(vdfPath, vdfPath + ".sh2ee.bak");

                File.WriteAllBytes(vdfPath, ShortcutsVdf.Serialize(shortcuts));
            }

            return RunGameId(appId);
        }

        /// <summary>
        /// Removes the non-Steam shortcut this tool added, from every logged-in account's
        /// shortcuts.vdf. Returns how many entries were removed across all accounts.
        ///
        /// Matching is deliberately narrow — the app name must match, and the exe too when one
        /// is given — so a shortcut the user made themselves is never collateral damage. An
        /// account whose file can't be parsed or written is skipped rather than aborting the
        /// rest; uninstall should always make what progress it can.
        /// </summary>
        public static int RemoveShortcut(string appName, string? exePath = null)
        {
            int removed = 0;

            foreach (var user in FindUserDataDirs())
            {
                string vdfPath = Path.Combine(user, "config", "shortcuts.vdf");
                if (!File.Exists(vdfPath))
                    continue;

                try
                {
                    var shortcuts = ShortcutsVdf.Parse(File.ReadAllBytes(vdfPath));
                    int before = shortcuts.Count;
                    shortcuts.RemoveAll(s => IsOurShortcut(s, appName, exePath));
                    if (shortcuts.Count == before)
                        continue;

                    if (!File.Exists(vdfPath + ".sh2ee.bak"))
                        File.Copy(vdfPath, vdfPath + ".sh2ee.bak");

                    File.WriteAllBytes(vdfPath, ShortcutsVdf.Serialize(shortcuts));
                    removed += before - shortcuts.Count;
                }
                catch
                {
                    // Leave this account's file alone and carry on with the others.
                }
            }

            return removed;
        }

        private static bool IsOurShortcut(
            Dictionary<string, object> shortcut, string appName, string? exePath)
        {
            if (!shortcut.TryGetValue("AppName", out var name) || (name as string) != appName)
                return false;
            if (exePath == null)
                return true;
            return shortcut.TryGetValue("Exe", out var exe) &&
                   (exe as string)?.Trim('"') == exePath.Trim('"');
        }

        /// <summary>
        /// Steam's non-Steam appid: CRC32 of (exe + appname) with the top bit set. The
        /// 32-bit value is used as the shortcut's "appid"; the 64-bit rungameid shifts it
        /// left 32 and sets bit 25 of the low word (the 0x02000000 "shortcut" flag).
        /// </summary>
        public static uint ComputeAppId(string exePath, string appName)
        {
            // Steam hashes the exe exactly as stored in the shortcut (quoted).
            string input = $"\"{exePath.Trim('"')}\"" + appName;
            uint crc = Services.Crc32.OfBytes(Encoding.UTF8.GetBytes(input));
            return crc | 0x80000000u;
        }

        public static ulong RunGameId(uint appId) =>
            ((ulong)appId << 32) | 0x02000000UL;

        public static string RunGameUrl(ulong gameId) => $"steam://rungameid/{gameId}";

    }
}
