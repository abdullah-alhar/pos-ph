#!/usr/bin/env bash
# Builds the Windows installer from macOS/Linux (Velopack cross-packaging).
#   ./build/release.sh 1.0.1
set -euo pipefail
cd "$(dirname "$0")/.."
VERSION="${1:?usage: release.sh <version>}"
dotnet tool restore
dotnet test
rm -rf publish/win-x64
dotnet publish src/VetPos.App -c Release -o publish/win-x64 -p:Version="$VERSION"
# Ship no debug symbols: harder to reverse-engineer, and the shop only ever gets the compiled app.
find publish/win-x64 -name '*.pdb' -delete
echo "Stripped $(find publish/win-x64 -name '*.pdb' | wc -l | tr -d ' ') pdb file(s) (expected 0)"
# Code signing (stops Windows Smart App Control / SmartScreen blocking VetPOS). Set SIGN_TEMPLATE to a
# signing command with {{file}}, e.g. with jsign (works on macOS / Linux):
#   SIGN_TEMPLATE='jsign --storetype TRUSTEDSIGNING --keystore weu.codesigning.azure.net --storepass "$TOKEN" --alias acct/profile --tsaurl http://timestamp.acs.microsoft.com {{file}}'
SIGN_ARGS=()
if [ -n "${SIGN_TEMPLATE:-}" ]; then SIGN_ARGS=(--signTemplate "$SIGN_TEMPLATE"); echo "Signing every file"; else echo "WARNING: not signed (set SIGN_TEMPLATE)"; fi
dotnet vpk "[win]" pack -u VetPOS.App -v "$VERSION" -p publish/win-x64 -e VetPOS.exe -o publish/releases \
  --packTitle "VetPOS" --packAuthors "VetPOS" -i src/VetPos.App/Assets/vetpos.ico --runtime win-x64 --framework vcredist143-x64 --splashImage build/splash.png ${SIGN_ARGS[@]+"${SIGN_ARGS[@]}"}
echo "Done: publish/releases"
