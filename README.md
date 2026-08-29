# SH2:EE Setup & Config (Linux)

A native Linux port of the [Silent Hill 2: Enhanced Edition](https://enhanced.townofsilenthill.com/SH2/)
Setup Tool and Configuration Tool, built with Avalonia (.NET 8). It targets
Bazzite/SteamOS and any Linux setup running SH2 PC under Wine/Proton.

The upstream tools are Windows-only:
- **Setup Tool** — an [Inno Setup](https://github.com/nipkownix/SH2EE-web-installer)
  web installer (Pascal script + Windows plugins).
- **Configuration Tool** — `SH2EEconfig.exe`, a native Win32 app that edits `d3d8.ini`.

This port ships them as **two apps that share one logic library**, reading the **same
upstream component manifest** (`_sh2ee.csv`) and the **same `config.xml` settings schema**,
so it stays current with the project automatically and produces byte-compatible
`SH2EEsetup.dat` / `d3d8.ini` files (the Windows tools can still manage the same install):

- **`SH2EESetup`** — a step-by-step installer wizard.
- **`SH2EEConfig`** — the standalone settings editor, auto-launched when the wizard finishes.

## The Setup wizard (`SH2EESetup`)

**Returning users get a menu, not the wizard.** The tool remembers the installation you last
worked with (`~/.config/sh2ee-setup/state.ini`), and when the Enhanced Edition is already
installed there it opens on a launch menu instead of step 1:

- **Modify or update the installation** — jumps into the flow below at step 2.
- **Launch the Configuration tool** — opens `SH2EEConfig` for that install. This is the
  reliable way to reach the config app from the AppImage, which ships only one desktop entry.
- **Uninstall the Enhanced Edition** — see below.
- **Create offline installation files** — produces a folder of component archives plus a
  `local_sh2ee.dat`, either by **rebuilding them from your existing installation** (no
  download at all) or by downloading fresh copies. Upstream only offers backups as a
  side-effect of installing; here you don't have to reinstall to get a copy of the files.
  Cancellable, and a cancelled run still leaves a valid — just smaller — folder.
- **Use a different folder…** — drops back to step 1 for a second install.

You reach the same menu when you pick an installed folder by hand — Browse or the detected
list opens it directly; a path typed into the box offers a **Show options…** button instead,
since re-navigating on every keystroke would fight you mid-edit. The folder is remembered as
soon as it's chosen, not only once an install finishes, so browsing to your game once is
enough even if you close the app straight after.

The remembered path is validated on every read, so a deleted folder or unmounted drive falls
back to detection rather than pointing at nothing. It also sidesteps the auto-detect depth
limit: once you've told the tool where the game is, an install nested deeper than the scan
reaches is still found instantly. `SH2EEConfig` reads the same memory, so launching it
standalone opens on the right game.

1. **Locate** — auto-detects your SH2 install on launch; if not found, tells you and lets you
   Browse to the folder with `sh2pc.exe`, then confirms whether detection succeeded.
2. **Source** — download the Enhanced Edition content automatically, or install from a local
   folder (`local_sh2ee.dat` + pre-downloaded packages). When downloading, **Also keep the
   downloaded files for offline reinstalls** mirrors upstream's backup option: archives go
   straight to a folder you choose instead of `/tmp`, and a `local_sh2ee.dat` is written
   beside them in upstream's exact format (CRLF, `notDownloaded,0.0` for components you
   skipped) — so that folder can drive step 2's offline path later, on this machine or another.
3. **What to install** — **Quick** (all content) or **Custom** (toggle exactly which
   components). Each per-component download is SHA-256 verified, with interactive
   Retry/Skip/Abort on a mismatch.
4. **Install** — progress per component, mirroring the upstream installer (stale-file
   cleanup, `sh2pc.exe` backup, `d3d8.ini` setting preservation across module updates).
   **Cancel installation** stops after the component in flight, deletes the staging folder,
   and records what genuinely made it into `SH2EEsetup.dat` — a cancelled install reports
   what it has rather than pretending it did nothing. Re-run to finish, or Uninstall to clear.
5. **Add to Steam** — optionally registers `sh2pc.exe` as a non-Steam game (editing Steam's
   binary `shortcuts.vdf`) with the launch options **pre-filled**, then reminds you to force
   a Proton version (Proton-GE / Proton Experimental recommended). **Finish** opens the
   config app.

**Uninstall** — when step 1 finds an existing installation, an **Uninstall…** button appears.
Install writes to three places, and all three are undone (each an opt-out checkbox):

| | What is removed |
|---|---|
| Game folder | The mod's files, per upstream's `CustomUninstall.iss` list, restoring your backed-up `sh2pc.exe`. Saves, game data and your own files are untouched. |
| Wine prefix | Only the `DllOverrides` keys this tool wrote; overrides you set for other DLLs stay. |
| Steam | The `Silent Hill 2: Enhanced Edition` shortcut, matched on name **and** exe so your own shortcuts are never touched. |

Uninstall refuses to run on a folder that doesn't contain `sh2pc.exe`, warns when no `.exe`
backup exists (the enhanced executable then stays in place), and is safe to run twice.

## The Config app (`SH2EEConfig`)

- Full parity with `SH2EEconfig.exe`: all **141 features** across 9 tabs, rendered as
  checkboxes / dropdowns with the upstream titles and descriptions.
- Loads current values from `d3d8.ini`, preserves manually-added "extra" keys, writes the
  file back in the upstream format (preface + per-option description comments).
- **Speedrun Mode** parity: selecting True Random / Set Seed forces and locks the affected
  settings; disabling unlocks and resets them — matching upstream `SetValueSpeedrunDefault`.
- **Save & Launch Game** (via Steam) and reset-to-defaults.
- Receives the game directory as its first CLI argument (passed by the wizard); run
  standalone, it auto-detects or lets you Browse.

## Linux / Proton notes

- The mod's `d3d8.dll` wrapper must load as **native** under Wine/Proton, and its DirectX 9
  mode (needed for shaders) must run on **WineD3D**, not DXVK — DXVK 2.2+ regressed this mod
  (fog renders as blocky cubes, and changing resolution/render scale crashes on device
  reset). The launch options the wizard fills in handle both:

  ```
  WINEDLLOVERRIDES="d3d8,dinput,dinput8,dsound,xinput1_3=n,b" PROTON_USE_WINED3D=1 %command%
  ```

  WineD3D keeps **all shaders** enabled; the only cost is OpenGL-vs-Vulkan performance,
  negligible for a 2001 game. (See upstream issue #557 and DXVK #3943.)

## Rebuilding offline files from an installation

The Enhanced Edition packages total **~4 GB**, and the project's servers are not fast. If the
files are already installed on a machine, downloading them again to make an offline set is
pure waste — so the tool can rebuild the archives straight from the installation.

Two things make this work:

- **Offline installs don't verify checksums.** `InstallLocalAsync` passes `notUsed`, so a
  rebuilt archive doesn't need to be byte-identical to upstream's — which it never could be,
  since zip timestamps and compression settings differ.
- **A component map supplies what the installation doesn't record.** `SH2EEsetup.dat` stores
  only *which* components are installed, never *which files they put on disk*, so
  `SH2EE.Core/Resources/component-files.csv` maps all 984 files to their component, with
  upstream's own CRC-32 and size for each.

That map is generated by `tools/refresh-component-map.py`, which reads each archive's **ZIP
central directory over HTTP Range requests** — the file list and per-file CRC-32 live at the
end of a ZIP, so it transfers a few hundred KB instead of 4 GB and finishes in under a minute.
Re-run it whenever upstream bumps a component version:

```sh
python3 tools/refresh-component-map.py SH2EE.Core/Resources/component-files.csv
```

The CRCs also give **per-file verification against upstream** — a stronger check than hashing
the finished archive, since it compares the bytes actually on disk with what upstream shipped.
Files that differ or are missing are reported rather than silently packaged. Note this is an
integrity check, not a security one: CRC-32 catches corruption and edits, not deliberate
tampering. `d3d8.ini` is excluded — the config app rewrites it by design.

When a component's installed version doesn't match the map's, the archive is still built but
the mismatch is reported, since the file list may no longer be accurate for that version.

## Game directory detection

SH2 PC (2002) is not a Steam title — it's installed offline into an arbitrary folder, so
manual selection is always available (Browse…). Auto-detect scans common Wine/Proton prefix
locations (Steam `compatdata`, Lutris, Heroic, Bottles, `~/.wine`) for `sh2pc.exe`.

## Build & run

```sh
dotnet build SH2EE.sln
dotnet run --project SH2EE.Setup     # the wizard
dotnet run --project SH2EE.Config    # the settings editor
```

## Install (Flatpak)

The `flatpak/` directory holds a Flathub-style manifest, two `.desktop` files, and a
`.metainfo.xml`. To build locally:

```sh
# one-time: generate the offline NuGet sources (for the solution)
python3 flatpak-dotnet-generator.py flatpak/nuget-sources.json SH2EE.Setup/SH2EE.Setup.csproj

flatpak-builder --user --install --force-clean build-dir \
  flatpak/io.github.last_colossi.SilentHill2Enhancements.yml
flatpak run io.github.last_colossi.SilentHill2Enhancements
```

> The app-id and git URL in the manifest follow the MirrorsEdgeTweaks org convention as a
> placeholder — change them to your own repo before publishing to Flathub. This app requests
> `--filesystem=home` because SH2 PC can be installed anywhere.

## Layout

```
SH2EE.Core/      shared library: Models, Services, Platform, embedded config.xml
  Models/        WebComponent, InstalledComponent, LocalComponent, ConfigDocument, descriptions
  Services/      ManifestService, DownloadService, ExtractionService,
                 InstallerService, ConfigService, IniFile
  Platform/      GameEnvironment, DllOverrideService, SteamShortcuts + ShortcutsVdf, UrlLauncher
SH2EE.Setup/     wizard app (WizardViewModel + step views, launches the config app on finish)
SH2EE.Config/    standalone settings editor (ConfigViewModel + editor, Speedrun Mode logic)
assets/          shared icon
flatpak/         Flathub manifest, two .desktop files, .metainfo.xml, icons
```

## License

This port's own code is MIT (see [LICENSE](LICENSE)). The embedded `config.xml`
and the component manifest format come from the Silent Hill 2: Enhanced Edition
project (zlib-style license). Not affiliated with Konami or the SH2:EE team.
