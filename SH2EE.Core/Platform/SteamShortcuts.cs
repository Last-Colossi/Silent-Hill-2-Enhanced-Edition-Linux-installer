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
        /// Steam's non-Steam appid: CRC32 of (exe + appname) with the top bit set. The
        /// 32-bit value is used as the shortcut's "appid"; the 64-bit rungameid shifts it
        /// left 32 and sets bit 25 of the low word (the 0x02000000 "shortcut" flag).
        /// </summary>
        public static uint ComputeAppId(string exePath, string appName)
        {
            // Steam hashes the exe exactly as stored in the shortcut (quoted).
            string input = $"\"{exePath.Trim('"')}\"" + appName;
            uint crc = Crc32(Encoding.UTF8.GetBytes(input));
            return crc | 0x80000000u;
        }

        public static ulong RunGameId(uint appId) =>
            ((ulong)appId << 32) | 0x02000000UL;

        public static string RunGameUrl(ulong gameId) => $"steam://rungameid/{gameId}";

        // Standard CRC-32 (IEEE 802.3), matching Steam's shortcut hashing.
        private static readonly uint[] CrcTable = BuildCrcTable();

        private static uint[] BuildCrcTable()
        {
            var table = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                uint c = i;
                for (int k = 0; k < 8; k++)
                    c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                table[i] = c;
            }
            return table;
        }

        private static uint Crc32(byte[] data)
        {
            uint crc = 0xFFFFFFFFu;
            foreach (byte b in data)
                crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
            return crc ^ 0xFFFFFFFFu;
        }
    }
}
