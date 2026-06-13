using System.Diagnostics;

namespace SH2EESetup.Platform
{
    /// <summary>Opens URLs / steam:// links portably, including from inside a Flatpak sandbox.</summary>
    public static class UrlLauncher
    {
        public static bool Open(string url)
        {
            // Inside Flatpak we can't spawn steam directly; xdg-open routes through the portal.
            string[] launchers = Environment.GetEnvironmentVariable("FLATPAK_ID") != null
                ? new[] { "xdg-open" }
                : new[] { "xdg-open", "steam" };

            foreach (var launcher in launchers)
            {
                try
                {
                    var psi = new ProcessStartInfo(launcher)
                    {
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                    };
                    psi.ArgumentList.Add(url);
                    if (Process.Start(psi) != null)
                        return true;
                }
                catch
                {
                    // Try the next launcher.
                }
            }
            return false;
        }
    }
}
