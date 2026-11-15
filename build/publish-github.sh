#!/usr/bin/env bash
# Builds a new version on this Mac and publishes it to GitHub Releases (same as the GitHub Action).
#   GITHUB_TOKEN=ghp_xxx ./build/publish-github.sh 0.5.0
# The token needs "contents: write" on the repository. Installed shops pick the update up by themselves.
set -euo pipefail
cd "$(dirname "$0")/.."
VERSION="${1:?usage: publish-github.sh <version>}"
: "${GITHUB_TOKEN:?set GITHUB_TOKEN first}"
REPO="https://github.com/abdullah-alhar/pos-ph"
dotnet tool restore
rm -rf publish/releases && mkdir -p publish/releases
dotnet vpk "[win]" download github --repoUrl "$REPO" --token "$GITHUB_TOKEN" -o publish/releases || echo "No earlier release yet"
./build/release.sh "$VERSION"
dotnet vpk "[win]" upload github --repoUrl "$REPO" --token "$GITHUB_TOKEN" -o publish/releases \
  --publish --releaseName "VetPOS $VERSION" --tag "v$VERSION" --merge
echo "Published VetPOS $VERSION to $REPO/releases"
