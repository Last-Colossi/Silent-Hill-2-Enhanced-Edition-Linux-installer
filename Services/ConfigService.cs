using System.Reflection;
using System.Xml.Linq;
using SH2EESetup.Models;

namespace SH2EESetup.Services
{
    /// <summary>
    /// Loads the embedded config.xml schema, applies a game's d3d8.ini values onto it,
    /// and writes the ini back out in the upstream config tool's format.
    /// </summary>
    public class ConfigService
    {
        private const string ConfigIniName = "d3d8.ini";

        /// <summary>Parses the schema shipped as an embedded resource (Resources/config.xml).</summary>
        public ConfigDocument LoadSchema()
        {
            using var stream = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream("SH2EESetup.Resources.config.xml")
                ?? throw new InvalidOperationException("Embedded config.xml resource not found.");
            using var reader = new StreamReader(stream);
            return ParseSchema(reader.ReadToEnd());
        }

        public static ConfigDocument ParseSchema(string xml)
        {
            var doc = new ConfigDocument();
            var root = XDocument.Parse(xml).Root
                ?? throw new InvalidDataException("config.xml has no root element.");

            var iniEl = root.Element("Ini")?.Element("Preface");
            if (iniEl != null)
                doc.Preface = iniEl.Value;

            foreach (var tabEl in root.Elements("Tab"))
            {
                var tab = new ConfigTab { Name = tabEl.Attribute("name")?.Value ?? "" };
                foreach (var secEl in tabEl.Elements("Section"))
                {
                    string secName = secEl.Attribute("name")?.Value ?? "";
                    var section = new ConfigSection { Name = secName };

                    foreach (var featEl in secEl.Elements("Feature"))
                    {
                        var choicesEl = featEl.Element("Choices");
                        var feature = new ConfigFeature
                        {
                            Name = featEl.Attribute("name")?.Value ?? "",
                            SectionName = secName,
                            Title = featEl.Element("Title")?.Value ?? "",
                            Description = featEl.Element("Description")?.Value ?? "",
                            IsSpeedrunToggleable = featEl.Attribute("speedrun")?.Value == "true",
                            IsCheckbox = choicesEl?.Attribute("type")?.Value == "check",
                        };

                        if (choicesEl != null)
                        {
                            foreach (var valEl in choicesEl.Elements("Value"))
                            {
                                feature.Choices.Add(new ConfigChoice
                                {
                                    Name = valEl.Attribute("name")?.Value ?? "",
                                    Value = valEl.Value.Trim(),
                                    IsDefault = valEl.Attribute("default")?.Value == "true",
                                    IsSpeedrunDefault = valEl.Attribute("speedrun-default")?.Value == "true",
                                    IsSpeedrunSetSeed = valEl.Attribute("speedrun-set")?.Value == "true",
                                    IsSpeedrunRandom = valEl.Attribute("speedrun-random")?.Value == "true",
                                });
                            }
                        }

                        section.Features.Add(feature);
                    }

                    tab.Sections.Add(section);
                }
                doc.Tabs.Add(tab);
            }

            foreach (var sEl in root.Element("Strings")?.Elements("S") ?? Enumerable.Empty<XElement>())
            {
                string id = sEl.Attribute("id")?.Value ?? "";
                if (id.Length > 0)
                    doc.Strings[id] = sEl.Value;
            }

            return doc;
        }

        /// <summary>
        /// Returns the saved value for each feature from the game's d3d8.ini, or null when
        /// the file is absent. Used to initialise the editor's selections.
        /// </summary>
        public static Dictionary<string, string>? ReadCurrentValues(string gameDir)
        {
            string path = Path.Combine(gameDir, ConfigIniName);
            if (!File.Exists(path))
                return null;
            return IniFile.ReadFlatValues(File.ReadAllText(path));
        }

        /// <summary>
        /// Detects keys present in the game's d3d8.ini that aren't part of the schema —
        /// the upstream "Extra" section, surfaced read-only and preserved on save.
        /// </summary>
        public static List<KeyValuePair<string, string>> ReadExtraOptions(string gameDir, ConfigDocument schema)
        {
            var current = ReadCurrentValues(gameDir);
            if (current == null)
                return new();

            var known = schema.AllFeatures.Select(f => f.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            return current
                .Where(kv => !known.Contains(kv.Key))
                .Select(kv => new KeyValuePair<string, string>(kv.Key, kv.Value))
                .ToList();
        }

        /// <summary>
        /// Writes d3d8.ini from the current selections, preserving any extra options.
        /// </summary>
        public static void Save(
            string gameDir,
            ConfigDocument schema,
            IReadOnlyDictionary<string, string> selectedValues,
            IReadOnlyList<KeyValuePair<string, string>> extra)
        {
            var sections = new List<IniSection>();
            foreach (var tab in schema.Tabs)
            {
                foreach (var sec in tab.Sections)
                {
                    var iniSec = new IniSection { Name = sec.Name };
                    foreach (var feat in sec.Features)
                    {
                        string value = selectedValues.TryGetValue(feat.Name, out var v)
                            ? v
                            : feat.Choices.ElementAtOrDefault(feat.DefaultIndex)?.Value ?? "0";
                        iniSec.Options.Add(new IniOption
                        {
                            Name = feat.Name,
                            Value = value,
                            Description = feat.Description,
                        });
                    }
                    sections.Add(iniSec);
                }
            }

            string text = IniFile.Build(schema.Preface, sections, extra);
            File.WriteAllText(Path.Combine(gameDir, ConfigIniName), text);
        }
    }
}
