#!/usr/bin/env bash
# Copies the game art (sprites, portraits, creek backgrounds, map) and audio into Assets/Resources.
# The art stays out of this public repo; the masters live in the private pictures repo
# (nugget-creek/game-art). Without it the game still runs with its greybox shapes.
# Usage: scripts/fetch-art.sh [pictures-clone]   (default ../pictures; safe to re-run)
set -euo pipefail

cd "$(dirname "$0")/.."
src="${1:-${NC_PICTURES_DIR:-../pictures}}/nugget-creek/game-art"
if [ ! -d "$src/Resources/Sprites" ]; then
  echo "No art at $src. Clone the private pictures repo next to this one or pass its path." >&2
  exit 1
fi

rm -rf Assets/Resources/Sprites Assets/Resources/Audio
mkdir -p Assets/Resources
cp -r "$src/Resources/Sprites" "$src/Resources/Sprites.meta" Assets/Resources/
cp "$src/Resources.meta" Assets/
echo "Art ready: $(find Assets/Resources/Sprites -type f ! -name '*.meta' | wc -l) files in Assets/Resources/Sprites"
# Audio is optional: the game is silent without it.
if [ -d "$src/Resources/Audio" ]; then
  cp -r "$src/Resources/Audio" "$src/Resources/Audio.meta" Assets/Resources/
  echo "Audio ready: $(find Assets/Resources/Audio -type f ! -name '*.meta' | wc -l) files in Assets/Resources/Audio"
fi
