namespace SH2EESetup.Models
{
    /// <summary>
    /// Component descriptions, lifted from the upstream installer's English language file
    /// so the maintenance UI can explain each package.
    /// </summary>
    public static class ComponentDescriptions
    {
        private static readonly Dictionary<string, string> Map = new(StringComparer.OrdinalIgnoreCase)
        {
            [ComponentIds.Sh2eModule] =
                "The SH2 Enhancements module provides programming-based fixes and enhancements. " +
                "This is the \"brains\" of the project and is required to be installed.",
            [ComponentIds.WineStub] =
                "Places a d3dx.dll stub in the game's folder so the game loads the project's local " +
                "d3d8.dll under Linux/Wine/Proton. Must be used with the Enhanced Executable package.",
            [ComponentIds.EnhancedExe] =
                "This executable provides compatibility with newer operating systems and is required " +
                "to be installed.",
            [ComponentIds.Essentials] =
                "The Enhanced Edition Essential Files provide geometry fixes, camera clipping " +
                "adjustments, high resolution text, and language/textual improvements for the game.",
            [ComponentIds.ImagePack] =
                "The Image Enhancement Pack provides upscaled, remastered, and remade full screen images.",
            [ComponentIds.FmvPack] =
                "The FMV Enhancement Pack provides improved quality of the game's full motion videos.",
            [ComponentIds.AudioPack] =
                "The Audio Enhancement Pack provides restored quality of the game's audio files.",
            [ComponentIds.Xidi] =
                "Provides compatibility with modern controllers.",
            [ComponentIds.Credits] =
                "The Silent Hill 2 PC credits video, including Silent Hill 2: Enhanced Edition credits. " +
                "A supplementary video, separate from the original game credits.",
        };

        public static string Get(string id) =>
            Map.TryGetValue(id, out var d) ? d : "";
    }
}
