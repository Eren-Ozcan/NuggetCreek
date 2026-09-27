#!/usr/bin/env bash
# Phase 3.1 device check: installs an APK on the connected phone, plays it with taps for a while
# and reports APK size, PSS (the RAM budget number) and the PerfProbe frame rate lines.
# Budgets from the design doc: APK < 200 MB, PSS < 300 MB for the game (< 450 MB with an ad
# shown), >= 30 FPS on an old phone.
#
# Usage: scripts/android-measure.sh [apk] [seconds]
#   apk      default: newest Builds/Android/*-measure.apk (menu Nugget Creek > Android > Build Measurement APK)
#   seconds  play time after a 15 s warm-up, default 120
set -euo pipefail

PKG=com.yilkgames.nuggetcreek
APK=${1:-$(ls -t Builds/Android/*-measure.apk 2>/dev/null | head -1)}
SECONDS_TO_PLAY=${2:-120}
[ -f "$APK" ] || { echo "no APK found; build one first" >&2; exit 1; }

echo "device: $(adb shell getprop ro.product.model | tr -d '\r'), Android $(adb shell getprop ro.build.version.release | tr -d '\r')"
echo "apk: $APK ($(( $(stat -c %s "$APK") / 1024 / 1024 )) MB)"

if ! adb install -r "$APK" >/dev/null; then
    echo "install failed; if the phone has a build signed with another key, uninstall it first" \
         "(this wipes the save: back up shared_prefs with run-as from a development build)" >&2
    exit 1
fi

adb logcat -c
adb shell am force-stop "$PKG"
adb shell monkey -p "$PKG" -c android.intent.category.LAUNCHER 1 >/dev/null 2>&1
sleep 15

# Tap across the creek area a few times a second, like a player collecting.
read -r W H < <(adb shell wm size | tr -d '\r' | sed -E 's/.*: ([0-9]+)x([0-9]+).*/\1 \2/')
end=$(( $(date +%s) + SECONDS_TO_PLAY ))
i=0
while [ "$(date +%s)" -lt "$end" ]; do
    x=$(( W * (25 + (i * 37) % 50) / 100 ))
    y=$(( H * (35 + (i * 23) % 25) / 100 ))
    adb shell input tap "$x" "$y"
    i=$(( i + 1 ))
done

echo "--- memory"
adb shell dumpsys meminfo "$PKG" | tr -d '\r' | grep -E "TOTAL PSS|TOTAL:|Native Heap|Graphics|GL mtrack|EGL mtrack" | head -8
echo "--- frame rate (PerfProbe, 10 s windows)"
adb logcat -d -s Unity | tr -d '\r' | grep "\[PerfProbe\]" | sed -E 's/.*\[PerfProbe\]/ /'
