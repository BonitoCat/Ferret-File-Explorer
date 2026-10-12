#! /bin/bash

CONTROL_FILE_X64_FULL="./fe-linux-x64-full/DEBIAN/control"
CONTROL_FILE_X64_HEADLESS="./fe-linux-x64-headless/DEBIAN/control"
CONTROL_FILE_ARM64_FULL="./fe-linux-arm64-full/DEBIAN/control"
CONTROL_FILE_ARM64_HEADLESS="./fe-linux-arm64-headless/DEBIAN/control"

VERSION=$(sed -n 's/^Version: //p' "$CONTROL_FILE_X64_FULL")
echo "Current version: $VERSION"

read -p "Enter new version number: " NEW_VERSION

if [ -f "$CONTROL_FILE_X64_FULL" ]; then
    echo "Changing version in $CONTROL_FILE_X64_FULL"
    sed -i "s/^Version: .*/Version: $NEW_VERSION/" "$CONTROL_FILE_X64_FULL"
fi

if [ -f "$CONTROL_FILE_X64_HEADLESS" ]; then
    echo "Changing version in $CONTROL_FILE_X64_HEADLESS"
    sed -i "s/^Version: .*/Version: $NEW_VERSION/" "$CONTROL_FILE_X64_HEADLESS"
fi

if [ -f "$CONTROL_FILE_ARM64_FULL" ]; then
    echo "Changing version in $CONTROL_FILE_X64_FULL"
    sed -i "s/^Version: .*/Version: $NEW_VERSION/" "$CONTROL_FILE_ARM64_FULL"
fi

if [ -f "$CONTROL_FILE_ARM64_HEADLESS" ]; then
    echo "Changing version in $CONTROL_FILE_ARM64_HEADLESS"
    sed -i "s/^Version: .*/Version: $NEW_VERSION/" "$CONTROL_FILE_ARM64_HEADLESS"
fi

dotnet publish ../FileExplorer.Cli.csproj \
    -c Release -r linux-x64 -f net8.0 --self-contained true \
    -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    -p:IncludeAllContentForSelfExtract=true

dotnet publish ../FileExplorer.Cli.csproj \
    -c Release -r linux-arm64 -f net8.0 --self-contained true \
    -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    -p:IncludeAllContentForSelfExtract=true

dotnet publish ../FileExplorer.Cli.csproj \
    -c Release -r win-x64 -f net8.0 --self-contained true \
    -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    -p:IncludeAllContentForSelfExtract=true

BUILD_FILE_X64="../bin/Release/net8.0/linux-x64/publish/FileExplorer.Cli"
BUILD_FILE_ARM64="../bin/Release/net8.0/linux-arm64/publish/FileExplorer.Cli"
BUILD_FILE_WIN64="../bin/Release/net8.0/win-x64/publish/FileExplorer.Cli.exe"

BUILD_FILE_X64_FULL_APPIMG_DEST="./AppDir/usr/bin/fe"
BUILD_FILE_X64_FULL_DEST="./fe-linux-x64-full/usr/local/bin/fe"
BUILD_FILE_X64_HEADLESS_DEST="./fe-linux-x64-headless/usr/local/bin/fe"
BUILD_FILE_ARM64_FULL_DEST="./fe-linux-arm64-full/usr/local/bin/fe"
BUILD_FILE_ARM64_HEADLESS_DEST="./fe-linux-arm64-headless/usr/local/bin/fe"
BUILD_FILE_WIN64_DEST="./output/fe.exe"

cp $BUILD_FILE_X64 $BUILD_FILE_X64_FULL_APPIMG_DEST
cp $BUILD_FILE_X64 $BUILD_FILE_X64_FULL_DEST
cp $BUILD_FILE_X64 $BUILD_FILE_X64_HEADLESS_DEST
cp $BUILD_FILE_ARM64 $BUILD_FILE_ARM64_FULL_DEST
cp $BUILD_FILE_ARM64 $BUILD_FILE_ARM64_HEADLESS_DEST
cp $BUILD_FILE_WIN64 $BUILD_FILE_WIN64_DEST

dpkg-deb --build fe-cli-linux-x64-full ./output/fe-cli-linux-x64-full.deb
dpkg-deb --build fe-cli-linux-x64-headless ./output/fe-cli-linux-x64-headless.deb
dpkg-deb --build fe-cli-linux-arm64-full ./output/fe-cli-linux-arm64-full.deb
dpkg-deb --build fe-cli-linux-arm64-headless ./output/fe-cli-linux-arm64-headless.deb
ARCH=x86_64 ./appimagetool-x86_64.AppImage AppDir ./output/fe-cli.AppImage

echo "Press any key to exit..."
read _

