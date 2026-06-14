#!/usr/bin/env bash
# Builds the SH2:EE AppImage, bundling both the setup wizard (default entry) and the
# standalone config app (reached via the AppImage's --config mode). Output goes to
# ./dist by default, or the directory given as the first argument.
#
# Requires: .NET 8 SDK (dotnet on PATH) and curl.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUT="${1:-$ROOT/dist}"
ID="io.github.last_colossi.SilentHill2Enhancements"
AD="$(mktemp -d)/AppDir"

mkdir -p "$AD/usr/bin"
dotnet publish "$ROOT/SH2EE.Setup/SH2EE.Setup.csproj"  -c Release -r linux-x64 \
    --self-contained true -p:PublishSingleFile=true -o "$AD/usr/bin"
dotnet publish "$ROOT/SH2EE.Config/SH2EE.Config.csproj" -c Release -r linux-x64 \
    --self-contained true -p:PublishSingleFile=true -o "$AD/usr/bin"
rm -f "$AD/usr/bin"/*.pdb

install -Dm755 "$ROOT/appimage/AppRun"           "$AD/AppRun"
install -Dm644 "$ROOT/appimage/$ID.desktop"      "$AD/$ID.desktop"
install -Dm644 "$ROOT/appimage/$ID.desktop"      "$AD/usr/share/applications/$ID.desktop"
install -Dm644 "$ROOT/flatpak/icons/sh2ee-setup-256.png" "$AD/$ID.png"
install -Dm644 "$ROOT/flatpak/icons/sh2ee-setup-256.png" \
    "$AD/usr/share/icons/hicolor/256x256/apps/$ID.png"

mkdir -p "$OUT"
TOOL="$(mktemp --suffix=.AppImage)"
curl -sSL -o "$TOOL" \
    https://github.com/AppImage/appimagetool/releases/download/continuous/appimagetool-x86_64.AppImage
chmod +x "$TOOL"
ARCH=x86_64 "$TOOL" --appimage-extract-and-run "$AD" "$OUT/SilentHill2Enhancements-x86_64.AppImage"

echo "Built: $OUT/SilentHill2Enhancements-x86_64.AppImage"
