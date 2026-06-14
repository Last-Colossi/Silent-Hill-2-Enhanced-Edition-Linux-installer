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
