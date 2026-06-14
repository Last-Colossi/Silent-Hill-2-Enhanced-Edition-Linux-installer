using System.Diagnostics;

namespace SH2EESetup.Setup
{
    /// <summary>
    /// Launches the bundled configuration app (SH2EEConfig), which ships alongside the
    /// setup executable in the release. The installed game directory is passed as the
    /// first argument so the config app knows which d3d8.ini to edit.
    /// </summary>
    public static class ConfigLauncher
    {
        public static bool Launch(string gameDirectory)
        {
            // When running from an AppImage, re-launch the same AppImage in "config mode"
            // (AppRun routes --config to SH2EEConfig). This gives the config app its own
            // AppImage mount, since the setup process is about to exit and would otherwise
            // unmount the image out from under a sibling binary launched off the same mount.
            string? appImage = Environment.GetEnvironmentVariable("APPIMAGE");
            if (!string.IsNullOrEmpty(appImage) && File.Exists(appImage))
            {
                try
                {
                    var psi = new ProcessStartInfo(appImage) { UseShellExecute = false };
                    psi.ArgumentList.Add("--config");
                    psi.ArgumentList.Add(gameDirectory);
                    Process.Start(psi);
                    return true;
                }
                catch
                {
                    // Fall through to the sibling-binary search below.
                }
            }

            // Search next to the setup executable. For single-file publishes the binary
            // lives alongside ProcessPath; for framework/normal layouts it's BaseDirectory.
            var dirs = new List<string> { AppContext.BaseDirectory };
            if (Path.GetDirectoryName(Environment.ProcessPath) is { } procDir)
                dirs.Add(procDir);

            foreach (var dir in dirs.Distinct())
            {
                foreach (var name in new[] { "SH2EEConfig", "SH2EEConfig.exe" })
                {
                    string path = Path.Combine(dir, name);
                    if (!File.Exists(path))
                        continue;
                    try
                    {
                        var psi = new ProcessStartInfo(path) { UseShellExecute = false };
                        psi.ArgumentList.Add(gameDirectory);
                        Process.Start(psi);
                        return true;
                    }
                    catch
                    {
                        return false;
                    }
                }
            }
            return false;
        }
    }
}
