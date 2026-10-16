#!/usr/bin/env bash

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(dirname "$SCRIPT_DIR")"
OUTPUT_DIR="$SCRIPT_DIR/output"

mkdir -p "$OUTPUT_DIR"

CONTROL_X64_FULL="$SCRIPT_DIR/fe-linux-x64-full/DEBIAN/control"
CONTROL_X64_HEADLESS="$SCRIPT_DIR/fe-linux-x64-headless/DEBIAN/control"
CONTROL_ARM64_FULL="$SCRIPT_DIR/fe-linux-arm64-full/DEBIAN/control"
CONTROL_ARM64_HEADLESS="$SCRIPT_DIR/fe-linux-arm64-headless/DEBIAN/control"

CURRENT_VERSION=$(sed -n 's/^Version: //p' "$CONTROL_X64_FULL" | head -n 1)
echo "Current version: $CURRENT_VERSION"

if [ -n "$1" ]; then
    NEW_VERSION="$1"
else
    read -p "Enter new version number (leave empty to keep $CURRENT_VERSION): " NEW_VERSION
    if [ -z "$NEW_VERSION" ]; then
        NEW_VERSION="$CURRENT_VERSION"
    fi
fi

echo "Building version: $NEW_VERSION"

for ctrl in "$CONTROL_X64_FULL" "$CONTROL_X64_HEADLESS" "$CONTROL_ARM64_FULL" "$CONTROL_ARM64_HEADLESS"; do
    if [ -f "$ctrl" ]; then
        sed -i "s/^Version: .*/Version: $NEW_VERSION/" "$ctrl"
    fi
done

CLI_PROJ="$ROOT_DIR/FileExplorer.Cli/FileExplorer.Cli.csproj"
GUI_PROJ="$ROOT_DIR/FileExplorer.Gui/FileExplorer.Gui.csproj"

CLI_CTRL_LEGACY="$ROOT_DIR/FileExplorer.Cli/builds/fe-linux-x64-full/DEBIAN/control"
if [ -f "$CLI_CTRL_LEGACY" ]; then
    sed -i "s/^Version: .*/Version: $NEW_VERSION/" "$CLI_CTRL_LEGACY"
fi

echo "=========================================="
echo "Publishing FileExplorer.Cli..."
echo "=========================================="

echo ">> CLI linux-x64"
dotnet publish "$CLI_PROJ" \
    -c Release -r linux-x64 -f net8.0 --self-contained true \
    -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    -p:IncludeAllContentForSelfExtract=true

echo ">> CLI linux-arm64"
dotnet publish "$CLI_PROJ" \
    -c Release -r linux-arm64 -f net8.0 --self-contained true \
    -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    -p:IncludeAllContentForSelfExtract=true

echo "=========================================="
echo "Publishing FileExplorer.Gui..."
echo "=========================================="

echo ">> GUI linux-x64"
dotnet publish "$GUI_PROJ" \
    -c Release -r linux-x64 -f net8.0 --self-contained true \
    -p:PublishSingleFile=true \
    -p:PublishReadyToRun=true \
    -p:IncludeNativeLibrariesForSelfExtract=true

echo ">> GUI linux-arm64"
dotnet publish "$GUI_PROJ" \
    -c Release -r linux-arm64 -f net8.0 --self-contained true \
    -p:PublishSingleFile=true \
    -p:PublishReadyToRun=true \
    -p:IncludeNativeLibrariesForSelfExtract=true

CLI_BIN_X64="$ROOT_DIR/FileExplorer.Cli/bin/Release/net8.0/linux-x64/publish/FileExplorer.Cli"
CLI_BIN_ARM64="$ROOT_DIR/FileExplorer.Cli/bin/Release/net8.0/linux-arm64/publish/FileExplorer.Cli"

GUI_BIN_X64="$ROOT_DIR/FileExplorer.Gui/bin/Release/net8.0/linux-x64/publish/FileExplorer.Gui"
GUI_BIN_ARM64="$ROOT_DIR/FileExplorer.Gui/bin/Release/net8.0/linux-arm64/publish/FileExplorer.Gui"

echo "=========================================="
echo "Assembling package trees..."
echo "=========================================="

cp "$CLI_BIN_X64" "$SCRIPT_DIR/fe-linux-x64-full/usr/bin/fe"
cp "$GUI_BIN_X64" "$SCRIPT_DIR/fe-linux-x64-full/usr/bin/ferret"
cp "$GUI_BIN_X64" "$SCRIPT_DIR/fe-linux-x64-full/usr/bin/fe-gui"

cp "$CLI_BIN_X64" "$SCRIPT_DIR/fe-linux-x64-headless/usr/bin/fe"

cp "$CLI_BIN_ARM64" "$SCRIPT_DIR/fe-linux-arm64-full/usr/bin/fe"
cp "$GUI_BIN_ARM64" "$SCRIPT_DIR/fe-linux-arm64-full/usr/bin/ferret"
cp "$GUI_BIN_ARM64" "$SCRIPT_DIR/fe-linux-arm64-full/usr/bin/fe-gui"

cp "$CLI_BIN_ARM64" "$SCRIPT_DIR/fe-linux-arm64-headless/usr/bin/fe"

cp "$CLI_BIN_X64" "$SCRIPT_DIR/AppDir/usr/bin/fe"
cp "$GUI_BIN_X64" "$SCRIPT_DIR/AppDir/usr/bin/ferret"
cp "$GUI_BIN_X64" "$SCRIPT_DIR/AppDir/usr/bin/fe-gui"

chmod 755 "$SCRIPT_DIR/fe-linux-x64-full/usr/bin/fe"
chmod 755 "$SCRIPT_DIR/fe-linux-x64-full/usr/bin/ferret"
chmod 755 "$SCRIPT_DIR/fe-linux-x64-full/usr/bin/fe-gui"
chmod 755 "$SCRIPT_DIR/fe-linux-x64-headless/usr/bin/fe"
chmod 755 "$SCRIPT_DIR/fe-linux-arm64-full/usr/bin/fe"
chmod 755 "$SCRIPT_DIR/fe-linux-arm64-full/usr/bin/ferret"
chmod 755 "$SCRIPT_DIR/fe-linux-arm64-full/usr/bin/fe-gui"
chmod 755 "$SCRIPT_DIR/fe-linux-arm64-headless/usr/bin/fe"
chmod 755 "$SCRIPT_DIR/AppDir/usr/bin/fe"
chmod 755 "$SCRIPT_DIR/AppDir/usr/bin/ferret"
chmod 755 "$SCRIPT_DIR/AppDir/usr/bin/fe-gui"
chmod 755 "$SCRIPT_DIR/AppDir/AppRun"

echo "=========================================="
echo "Building .deb packages..."
echo "=========================================="

dpkg-deb --root-owner-group --build "$SCRIPT_DIR/fe-linux-x64-full" "$OUTPUT_DIR/ferret-linux-x64-full.deb"
dpkg-deb --root-owner-group --build "$SCRIPT_DIR/fe-linux-x64-headless" "$OUTPUT_DIR/ferret-linux-x64-headless.deb"
dpkg-deb --root-owner-group --build "$SCRIPT_DIR/fe-linux-arm64-full" "$OUTPUT_DIR/ferret-linux-arm64-full.deb"
dpkg-deb --root-owner-group --build "$SCRIPT_DIR/fe-linux-arm64-headless" "$OUTPUT_DIR/ferret-linux-arm64-headless.deb"

APPIMAGETOOL="$SCRIPT_DIR/appimagetool-x86_64.AppImage"
if [ -f "$APPIMAGETOOL" ]; then
    echo "=========================================="
    echo "Building AppImage..."
    echo "=========================================="
    ARCH=x86_64 "$APPIMAGETOOL" "$SCRIPT_DIR/AppDir" "$OUTPUT_DIR/ferret.AppImage"
fi

echo "=========================================="
echo "Build complete! Generated outputs in $OUTPUT_DIR:"
echo "=========================================="
ls -lh "$OUTPUT_DIR"
