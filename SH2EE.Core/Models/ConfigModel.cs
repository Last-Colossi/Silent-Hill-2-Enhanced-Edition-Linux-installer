namespace SH2EESetup.Models
{
    /// <summary>The parsed config.xml: tabs of sections of features, plus a preface and UI strings.</summary>
    public class ConfigDocument
    {
        public string Preface { get; set; } = "";
        public List<ConfigTab> Tabs { get; } = new();
        public Dictionary<string, string> Strings { get; } = new(StringComparer.Ordinal);

        /// <summary>All features across all tabs/sections, flattened.</summary>
        public IEnumerable<ConfigFeature> AllFeatures =>
            Tabs.SelectMany(t => t.Sections).SelectMany(s => s.Features);

        public string GetString(string id, string fallback) =>
            Strings.TryGetValue(id, out var v) ? v : fallback;
    }

    public class ConfigTab
    {
        public required string Name { get; init; }
        public List<ConfigSection> Sections { get; } = new();
    }

    /// <summary>Maps to an [ini section] in d3d8.ini.</summary>
    public class ConfigSection
    {
        public required string Name { get; init; }
        public List<ConfigFeature> Features { get; } = new();
    }

    /// <summary>
    /// One configurable setting. <see cref="Name"/> is the d3d8.ini key; the selected
    /// <see cref="ConfigChoice"/> supplies the value written to the ini.
    /// </summary>
    public class ConfigFeature
    {
        public required string Name { get; init; }
        public required string SectionName { get; init; }
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public bool IsSpeedrunToggleable { get; set; }
        public bool IsCheckbox { get; set; }   // <Choices type="check"> vs "list"
        public List<ConfigChoice> Choices { get; } = new();

        /// <summary>Index of the choice flagged default="true", or 0.</summary>
        public int DefaultIndex
        {
            get
            {
                int idx = Choices.FindIndex(c => c.IsDefault);
                return idx < 0 ? 0 : idx;
            }
        }

        public ConfigChoice? FindByValue(string iniValue) =>
            Choices.FirstOrDefault(c => c.Value == iniValue);
    }

    public class ConfigChoice
    {
        public required string Name { get; init; }   // display label
        public required string Value { get; init; }  // value stored in d3d8.ini
        public bool IsDefault { get; init; }

        // Speedrun value selection (from the value-level attributes in config.xml):
        public bool IsSpeedrunDefault { get; init; }   // speedrun-default — any speedrun mode
        public bool IsSpeedrunSetSeed { get; init; }   // speedrun-set    — Set Seed mode (2)
        public bool IsSpeedrunRandom { get; init; }    // speedrun-random — True Random mode (1)
    }

    /// <summary>The SpeedrunMode d3d8.ini values, matching config.xml.</summary>
    public static class SpeedrunMode
    {
        public const string FeatureName = "SpeedrunMode";
        public const int Disabled = 0;
        public const int TrueRandom = 1;
        public const int SetSeed = 2;
    }
}
