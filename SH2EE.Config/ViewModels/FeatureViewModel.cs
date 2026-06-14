using SH2EESetup.Models;

namespace SH2EESetup.ViewModels
{
    /// <summary>
    /// Editable wrapper around a <see cref="ConfigFeature"/>: tracks the currently selected
    /// choice and surfaces it as the value to write to d3d8.ini.
    /// </summary>
    public class FeatureViewModel : BaseViewModel
    {
        private int _selectedIndex;
        private bool _isLocked;

        public FeatureViewModel(ConfigFeature feature)
        {
            Feature = feature;
            _selectedIndex = feature.DefaultIndex;
        }

        public ConfigFeature Feature { get; }

        /// <summary>True when Speedrun Mode is active and this feature is speedrun-toggleable.</summary>
        public bool IsLocked
        {
            get => _isLocked;
            set => SetProperty(ref _isLocked, value);
        }

        public bool IsSpeedrunToggleable => Feature.IsSpeedrunToggleable;

        public string Title => Feature.Title;
        public string Description => Feature.Description;
        public bool IsCheckbox => Feature.IsCheckbox;

        /// <summary>Display labels for the combo-box presentation of list-type features.</summary>
        public IReadOnlyList<string> ChoiceLabels =>
            Feature.Choices.Select(c => c.Name).ToList();

        public int SelectedIndex
        {
            get => _selectedIndex;
            set
            {
                if (SetProperty(ref _selectedIndex, value))
                    OnPropertyChanged(nameof(IsChecked));
            }
        }

        /// <summary>For checkbox features: maps the boolean onto the two choices.</summary>
        public bool IsChecked
        {
            get => CurrentValue == "1";
            set
            {
                int idx = Feature.Choices.FindIndex(c => c.Value == (value ? "1" : "0"));
                if (idx >= 0)
                    SelectedIndex = idx;
            }
        }

        /// <summary>The d3d8.ini value for the current selection.</summary>
        public string CurrentValue =>
            Feature.Choices.ElementAtOrDefault(_selectedIndex)?.Value
            ?? Feature.Choices.ElementAtOrDefault(Feature.DefaultIndex)?.Value
            ?? "0";

        /// <summary>Sets the selection from a value read out of d3d8.ini.</summary>
        public void SetFromIniValue(string iniValue)
        {
            int idx = Feature.Choices.FindIndex(c => c.Value == iniValue);
            if (idx >= 0)
                SelectedIndex = idx;
        }

        public void ResetToDefault() => SelectedIndex = Feature.DefaultIndex;

        /// <summary>
        /// Applies the speedrun-mode value selection for this feature, mirroring the
        /// upstream SetValueSpeedrunDefault: mode-specific value first (random=1 / set=2),
        /// then — unless we're only switching between already-active modes — the generic
        /// speedrun-default, then the normal default.
        /// </summary>
        public void ApplySpeedrunDefault(int srValue, bool srAlreadyActive)
        {
            if (Feature.Name == SpeedrunMode.FeatureName)
                return;

            int idx;
            if (srValue == SpeedrunMode.TrueRandom &&
                (idx = Feature.Choices.FindIndex(c => c.IsSpeedrunRandom)) >= 0)
            {
                SelectedIndex = idx;
                return;
            }
            if (srValue == SpeedrunMode.SetSeed &&
                (idx = Feature.Choices.FindIndex(c => c.IsSpeedrunSetSeed)) >= 0)
            {
                SelectedIndex = idx;
                return;
            }

            if (srAlreadyActive)
                return;

            idx = Feature.Choices.FindIndex(c => c.IsSpeedrunDefault);
            SelectedIndex = idx >= 0 ? idx : Feature.DefaultIndex;
        }
    }
}
