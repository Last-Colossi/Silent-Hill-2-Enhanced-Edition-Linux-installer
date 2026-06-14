using Avalonia;

namespace SH2EESetup.Config
{
    internal static class Program
    {
        /// <summary>First CLI argument, if any, is the game directory passed by the Setup app.</summary>
        public static string? GameDirectoryArg { get; private set; }

        [STAThread]
        public static void Main(string[] args)
        {
            if (args.Length > 0 && args[0].Length > 0)
                GameDirectoryArg = args[0];

            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }

        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .WithInterFont()
                .LogToTrace();
    }
}
