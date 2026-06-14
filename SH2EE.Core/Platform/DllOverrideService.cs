using System.Text;

namespace SH2EESetup.Platform
{
    /// <summary>
    /// Applies the DLL overrides SH2:EE needs under Wine/Proton. The project's d3d8.dll
    /// wrapper (and the dinput/dsound/xinput shims) must load as native rather than the
    /// builtin Wine versions, otherwise the enhancements never engage.
    ///
    /// The upstream Windows installer's "Wine Stub" component does this by writing
    /// HKCU\Software\Wine\DllOverrides. We do the equivalent here either by editing the
    /// prefix's user.reg directly, or — when the prefix can't be located — by handing the
    /// user a WINEDLLOVERRIDES string to paste into their launcher's launch options.
    /// </summary>
    public static class DllOverrideService
    {
        /// <summary>
        /// The DLLs that must be forced to native ("n,b" = native then builtin). Matches the
        /// set the upstream wine_stub component overrides.
        /// </summary>
        public static readonly string[] OverriddenDlls =
            { "d3d8", "dinput", "dinput8", "dsound", "xinput1_3" };

        /// <summary>
        /// The value for a launcher's WINEDLLOVERRIDES environment variable / launch option,
        /// e.g. WINEDLLOVERRIDES="d3d8,dinput,dinput8,dsound,xinput1_3=n,b" %command%
        /// </summary>
        public static string WineDllOverridesValue =>
            string.Join(',', OverriddenDlls) + "=n,b";

        /// <summary>
        /// Routes Direct3D 9 through WineD3D instead of DXVK. The mod's d3d8to9 conversion
        /// (needed for its shaders) hits DXVK 2.2+ regressions under Proton — fog renders as
        /// blocky cubes at the wrong height, and changing resolution / render scale (a D3D9
        /// device reset) crashes the game. WineD3D implements D3D9 fixed-function fog and
        /// device-reset far closer to native, fixing both while keeping every shader enabled.
        /// See upstream issue #557 and DXVK #3943.
        /// </summary>
        public const string ProtonForceWineD3D = "PROTON_USE_WINED3D=1";

        public static string SteamLaunchOption =>
            $"WINEDLLOVERRIDES=\"{WineDllOverridesValue}\" {ProtonForceWineD3D} %command%";

        /// <summary>
        /// Writes the DLL overrides into the prefix's user.reg under
        /// [Software\\Wine\\DllOverrides]. Returns true on success. Creates a one-time
        /// backup of user.reg alongside it.
        /// </summary>
        public static bool TryApplyToPrefix(string prefixRoot, out string message)
        {
            string userReg = Path.Combine(prefixRoot, "user.reg");
            if (!File.Exists(userReg))
            {
                message = $"No user.reg found in prefix: {prefixRoot}";
                return false;
            }

            try
            {
                string backup = userReg + ".sh2ee.bak";
                if (!File.Exists(backup))
                    File.Copy(userReg, backup);

                string content = File.ReadAllText(userReg);
                content = UpsertDllOverrides(content);
                File.WriteAllText(userReg, content);

                message = $"DLL overrides written to {userReg}";
                return true;
            }
            catch (Exception ex)
            {
                message = $"Failed to write user.reg: {ex.Message}";
                return false;
            }
        }

        /// <summary>
        /// Inserts/updates the [Software\\Wine\\DllOverrides] section in a user.reg body,
        /// setting each required DLL to "native,builtin" without disturbing other keys.
        /// </summary>
        internal static string UpsertDllOverrides(string regContent)
        {
            const string header = "[Software\\\\Wine\\\\DllOverrides]";
            var lines = regContent.Replace("\r\n", "\n").Split('\n').ToList();

            int sectionStart = lines.FindIndex(l =>
                l.TrimStart().StartsWith(header, StringComparison.Ordinal));

            if (sectionStart < 0)
            {
                // Append a fresh section at the end.
                var sb = new StringBuilder(regContent.TrimEnd('\n', '\r'));
                sb.Append("\n\n").Append(header).Append('\n');
                foreach (var dll in OverriddenDlls)
                    sb.Append($"\"{dll}\"=\"native,builtin\"\n");
                return sb.ToString();
            }

            // Find the extent of the existing section (up to the next "[..]" header).
            int sectionEnd = lines.FindIndex(sectionStart + 1, l =>
                l.TrimStart().StartsWith('['));
            if (sectionEnd < 0)
                sectionEnd = lines.Count;

            foreach (var dll in OverriddenDlls)
            {
                string keyPrefix = $"\"{dll}\"=";
                int existing = lines.FindIndex(sectionStart + 1, sectionEnd - sectionStart - 1,
                    l => l.TrimStart().StartsWith(keyPrefix, StringComparison.OrdinalIgnoreCase));

                string entry = $"\"{dll}\"=\"native,builtin\"";
                if (existing >= 0)
                {
                    lines[existing] = entry;
                }
                else
                {
                    lines.Insert(sectionStart + 1, entry);
                    sectionEnd++;
                }
            }

            return string.Join('\n', lines);
        }
    }
}
