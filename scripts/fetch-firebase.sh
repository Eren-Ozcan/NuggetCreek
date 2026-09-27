#!/usr/bin/env bash
# Downloads the pinned Firebase Unity SDK tarballs that Packages/manifest.json references.
# They are too large for the repo (the app package alone is 62 MB), so they stay out of git.
# Usage: scripts/fetch-firebase.sh   (from the repo root; safe to re-run)
set -euo pipefail

cd "$(dirname "$0")/.."
dir=Packages/firebase
mkdir -p "$dir"
registry=https://dl.google.com/games/registry/unity

packages=(
  "com.google.external-dependency-manager 1.2.187 8afe23d7026539c59e6339d6034c3b3e9d667dcc26c285bc2ab2c187a21a84e1"
  "com.google.firebase.app 13.17.0 d74830478ed10eab56d848d92484e23b3aa9b27cbd1c843c21745be04f57eaa5"
  "com.google.firebase.analytics 13.17.0 8f857d19e09794f6f50921d0af419cbcd86dfb94e53586a9f41270728ae1f8f8"
  "com.google.firebase.remote-config 13.17.0 1862f969c6a1f78496796379a7d703deeb877fb5b687b9ed9dd06ba5fa8a16ee"
  "com.google.firebase.crashlytics 13.17.0 0911e76db75e0a6410493e3db2e609b619ab0386cd7e9d66689406c3c7ec5658"
)

for entry in "${packages[@]}"; do
  read -r name version sha <<<"$entry"
  file="$dir/$name-$version.tgz"
  if [ ! -f "$file" ] || ! echo "$sha  $file" | sha256sum -c --status; then
    echo "Downloading $name $version"
    curl -sfL -o "$file" "$registry/$name/$name-$version.tgz"
  fi
  echo "$sha  $file" | sha256sum -c --quiet
done
echo "Firebase packages ready in $dir"
