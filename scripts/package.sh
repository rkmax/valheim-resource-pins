#!/usr/bin/env bash
# Builds ResourcePins in Release and makes the Thunderstore zip in dist/.
# Runs local only: the build needs Valheim's assemblies from your Steam install.
# The version comes from Version in Plugin.cs and is copied into manifest.json.
set -euo pipefail
cd "$(dirname "$0")/.."

VERSION="$(grep -oP 'const string Version = "\K[^"]+' src/ResourcePins/Plugin.cs)"
[[ -n "$VERSION" ]] || { echo "error: no Version in Plugin.cs" >&2; exit 1; }
echo "Packaging ResourcePins $VERSION"

dotnet build src/ResourcePins -c Release
DLL=src/ResourcePins/bin/Release/ResourcePins.dll

STAGE="$(mktemp -d)"
trap 'rm -rf "$STAGE"' EXIT
mkdir -p "$STAGE/plugins"
cp package/thunderstore/icon.png README.md CHANGELOG.md LICENSE "$STAGE/"
cp "$DLL" "$STAGE/plugins/"
sed -E "s/(\"version_number\": \")[^\"]*\"/\1$VERSION\"/" package/thunderstore/manifest.json > "$STAGE/manifest.json"

mkdir -p dist
OUT="$PWD/dist/ResourcePins-$VERSION.zip"
rm -f "$OUT"
(cd "$STAGE" && zip -rq "$OUT" .)
echo "Wrote $OUT"
