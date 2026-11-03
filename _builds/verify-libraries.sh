#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT_DIR"

dotnet build FileExplorer.sln -c Release -m:1
for project in \
    Libraries/LoggerLib/Tests/Library.Tests.csproj \
    Libraries/InputLib/Tests/InputLib.Tests.csproj \
    Libraries/TuiLib/Tests/TuiLib.Tests.csproj \
    FileExplorer.Credentials.Tests/FileExplorer.Credentials.Tests.csproj \
    FileExplorer.Startup.Tests/FileExplorer.Startup.Tests.csproj; do
    dotnet run --project "$project" -c Release --no-build
done
python3 _tests/terminal_smoke.py "$(command -v dotnet)" FileExplorer.Cli/bin/Release/net8.0/FileExplorer.Cli.dll

# Optional integration checks require additional desktop services/tools.
if [[ "${1:-}" == "--desktop" ]]; then
    python3 _tests/keyring_smoke.py "$(command -v dotnet)" FileExplorer.Credentials.Tests/bin/Release/net8.0/FileExplorer.Credentials.Tests.dll
    python3 _tests/desktop_smoke.py "$(command -v dotnet)" FileExplorer.Gui/bin/Release/net8.0/FileExplorer.Gui.dll
fi
