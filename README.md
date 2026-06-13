# SH2:EE Setup (Linux)

A native Linux port of the [Silent Hill 2: Enhanced Edition](https://enhanced.townofsilenthill.com/SH2/)
Setup Tool and Configuration Tool, built with Avalonia (.NET 8). It targets
Bazzite/SteamOS and any Linux setup running SH2 PC under Wine/Proton.

The upstream tools are Windows-only:
- **Setup Tool** — an [Inno Setup](https://github.com/nipkownix/SH2EE-web-installer)
  web installer (Pascal script + Windows plugins).
- **Configuration Tool** — `SH2EEconfig.exe`, a native Win32 app that edits `d3d8.ini`.

This port reimplements both as one cross-platform app. It reads the **same upstream
component manifest** (`_sh2ee.csv`) and the **same `config.xml` settings schema**, so it
stays current with the project automatically and produces byte-compatible
`SH2EEsetup.dat` / `d3d8.ini` files (the Windows tools can still manage the same install).

## What it does

### Install / Maintenance tab
- Fetches the live component manifest from the SH2:EE servers.
- Fresh install: force-checks the mandatory components (module, enhanced exe, credits)
  and the Linux **Wine Stub**, lets you pick the optional packs.
- Maintenance mode (when `SH2EEsetup.dat` is present): shows installed vs. available
  versions and pre-checks only components with an update available.
- Per-component download → SHA-256 verify → pre-extraction cleanup → extract, mirroring
  the upstream installer (stale-file deletion, `sh2pc.exe` backup, `d3d8.ini` setting
  preservation across module updates).
- **Checksum mismatches** are handled interactively: Retry / Skip / Abort.
- **Offline install** from a folder containing `local_sh2ee.dat` and the pre-downloaded
  component archives — no network needed.
- Uninstall restores the original `sh2pc.exe` and removes all project files.

### Configure tab
- Full parity with `SH2EEconfig.exe`: all **141 features** across 9 tabs, rendered as
  checkboxes / dropdowns with the upstream titles and descriptions.
- Loads current values from `d3d8.ini`, preserves manually-added "extra" keys, writes the
  file back in the upstream format (preface + per-option description comments).
- **Speedrun Mode** parity: selecting True Random / Set Seed forces and locks the affected
  settings to their speedrun values; disabling unlocks and resets them — matching the
  upstream `SetValueSpeedrunDefault` logic.
- **Save & Launch Game** (launches via Steam, see below) and reset-to-defaults.

### Linux / Proton tab
- The enhancements need the project's `d3d8.dll` wrapper to load as **native** under
  Wine/Proton. Two ways to set that up:
  - **Auto:** when the game lives inside a detectable Wine prefix, writes the DLL
    overrides into the prefix's `user.reg` (the equivalent of the upstream "Wine Stub"
    registry writes), with a one-time backup.
  - **Universal:** a `WINEDLLOVERRIDES="..." %command%` launch-option string to paste into
    Steam/Lutris/Heroic when the game is a loose copy outside any prefix.
- **Add to Steam:** registers `sh2pc.exe` as a non-Steam game by editing Steam's binary
  `shortcuts.vdf`, with the DLL-override launch option pre-filled. Computes the non-Steam
  `rungameid` so the **Launch via Steam** / **Save & Launch** buttons can start the game
  through Proton. (Restart Steam afterwards and force a Proton version in the shortcut's
  Compatibility properties.)

## Game directory detection

SH2 PC (2002) is not a Steam title — it's installed offline into an arbitrary folder. So
**manual selection is the primary path** (Browse…). Auto-detect is a convenience that scans
common Wine/Proton prefix locations (Steam `compatdata`, Lutris, Heroic, Bottles, `~/.wine`)
for `sh2pc.exe`.

## Build & run

```sh
dotnet build
dotnet run
```

## Install (Flatpak)

The `flatpak/` directory holds a Flathub-style manifest plus `.desktop` and
`.metainfo.xml`, mirroring the MirrorsEdgeTweaks packaging. To build locally:

```sh
# one-time: generate the offline NuGet sources
python3 flatpak-dotnet-generator.py flatpak/nuget-sources.json SH2EESetupLinux.csproj

flatpak-builder --user --install --force-clean build-dir \
  flatpak/io.github.last_colossi.SilentHill2Enhancements.yml
flatpak run io.github.last_colossi.SilentHill2Enhancements
```

> The app-id and git URL in the manifest follow the MirrorsEdgeTweaks org convention as a
> placeholder — change them to your own repo before publishing to Flathub. Unlike the ME
> app, this one requests `--filesystem=home` because SH2 PC can be installed anywhere.

## Known gaps vs. upstream

- Per-file checksum handling offers Retry/Skip/Abort (the upstream wording differs slightly).
- Offline mode installs every component present in `local_sh2ee.dat`; it doesn't render a
  separate component-picker for offline sources.

## Layout

```
Models/        WebComponent, InstalledComponent, LocalComponent, ConfigDocument, descriptions
Services/      ManifestService, DownloadService, ExtractionService,
               InstallerService, ConfigService, IniFile
Platform/      GameEnvironment (detection), DllOverrideService,
               SteamShortcuts + ShortcutsVdf (Add to Steam), UrlLauncher
ViewModels/    MainViewModel + Component/Feature VMs (incl. Speedrun Mode logic)
Views/         MainWindow (shell + dynamic install list & config editor)
Resources/     config.xml (embedded, from upstream), icon.ico
flatpak/       Flathub manifest, .desktop, .metainfo.xml, icons
```

## License

This port's own code is MIT (see [LICENSE](LICENSE)). The embedded `config.xml`
and the component manifest format come from the Silent Hill 2: Enhanced Edition
project (zlib-style license). Not affiliated with Konami or the SH2:EE team.
