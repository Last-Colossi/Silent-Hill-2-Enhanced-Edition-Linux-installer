using SH2EESetup.Models;

namespace SH2EESetup.ViewModels
{
    /// <summary>A selectable component row in the install/maintenance list.</summary>
    public class ComponentViewModel : BaseViewModel
    {
        private bool _isSelected;
        private bool _isEnabled = true;

        public required WebComponent Component { get; init; }

        /// <summary>Human-readable description from the manifest/known component table.</summary>
        public string Description { get; init; } = "";

        /// <summary>True if this component is recorded as installed in SH2EEsetup.dat.</summary>
        public bool IsInstalled { get; set; }

        /// <summary>Installed version (from SH2EEsetup.dat), or null if not installed.</summary>
        public string? InstalledVersion { get; set; }

        /// <summary>Components the installer forces on a fresh install (cannot be unchecked).</summary>
        public bool IsMandatory { get; init; }

        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        public bool IsEnabled
        {
            get => _isEnabled;
            set => SetProperty(ref _isEnabled, value);
        }

        public string Name => Component.Name;
        public string AvailableVersion => Component.Version;

        public bool UpdateAvailable =>
            IsInstalled &&
            !string.Equals(InstalledVersion, Component.Version, StringComparison.OrdinalIgnoreCase);

        /// <summary>Status badge shown next to the component name.</summary>
        public string StatusLabel
        {
            get
            {
                if (!IsInstalled)
                    return $"Available v{AvailableVersion}";
                if (UpdateAvailable)
                    return $"Update: v{InstalledVersion} → v{AvailableVersion}";
                return $"Installed v{InstalledVersion}";
            }
        }
    }
}
