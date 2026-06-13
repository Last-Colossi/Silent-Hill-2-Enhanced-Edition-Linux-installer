namespace SH2EESetup.Models
{
    /// <summary>
    /// One row of the upstream web manifest (_sh2ee.csv): a downloadable component.
    /// </summary>
    public class WebComponent
    {
        public required string Id { get; init; }
        public required string Name { get; init; }
        public required string Version { get; init; }
        public required string Url { get; init; }
        public required string Sha256 { get; init; }

        public string FileName => Url[(Url.LastIndexOf('/') + 1)..];
    }

    /// <summary>
    /// One row of SH2EEsetup.dat: the per-game record of what is installed.
    /// The file format matches the Windows setup tool exactly so both tools
    /// can manage the same installation.
    /// </summary>
    public class InstalledComponent
    {
        public required string Id { get; set; }
        public bool IsInstalled { get; set; }
        public required string Version { get; set; }
    }

    /// <summary>
    /// One row of a local_sh2ee.dat manifest: a component whose archive is already on
    /// disk (offline install). Format: id,name,fileName,version.
    /// </summary>
    public class LocalComponent
    {
        public required string Id { get; init; }
        public required string Name { get; init; }
        public required string FileName { get; init; }
        public required string Version { get; init; }
    }

    public static class ComponentIds
    {
        public const string SetupTool = "setup_tool";
        public const string Sh2eModule = "sh2emodule";
        public const string WineStub = @"sh2emodule\wine_stub";
        public const string EnhancedExe = "ee_exe";
        public const string Essentials = "ee_essentials";
        public const string ImagePack = "img_pack";
        public const string FmvPack = "fmv_pack";
        public const string AudioPack = "audio_pack";
        public const string Xidi = "xidi";
        public const string Credits = "credits";

        /// <summary>Components the upstream installer force-checks on a fresh install.</summary>
        public static readonly string[] Mandatory = { Sh2eModule, EnhancedExe, Credits };
    }
}
