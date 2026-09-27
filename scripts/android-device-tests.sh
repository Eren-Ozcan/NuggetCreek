#!/usr/bin/env bash
# Device test pass on the connected phone with a development APK (debug key, so run-as works).
# Checks what the editor tests cannot: cold and warm starts, the process being killed in the
# background, no network, a large system font, rotation, Android's own monkey and memory.
# Every step scans logcat for crashes, ANRs and Unity exceptions.
#
# The phone's save is backed up first and restored at the end, whatever happens in between.
#
# Usage: scripts/android-device-tests.sh [apk] [monkey-events]
#   apk            default: newest Builds/Android/*-dev.apk (menu Nugget Creek > Android > Build Dev APK)
#   monkey-events  default 5000
set -uo pipefail
export MSYS_NO_PATHCONV=1

PKG=com.yilkgames.nuggetcreek
PREFS=shared_prefs/$PKG.v2.playerprefs.xml
APK=${1:-$(ls -t Builds/Android/*-dev.apk 2>/dev/null | head -1)}
EVENTS=${2:-5000}
OUT=${NC_DEVICE_OUT:-Builds/Android/device-tests}
[ -f "$APK" ] || { echo "no APK found; build one first" >&2; exit 1; }
mkdir -p "$OUT"

failures=0
pass() { echo "PASS  $1"; }
fail() { echo "FAIL  $1"; failures=$((failures + 1)); }

launch() {
    adb shell monkey -p "$PKG" -c android.intent.category.LAUNCHER 1 >/dev/null 2>&1
}

alive() { [ -n "$(adb shell pidof "$PKG" | tr -d '\r')" ]; }

# Total PSS in MB, the number the design doc budgets (< 300 MB).
pss_mb() {
    local kb
    kb=$(adb shell dumpsys meminfo "$PKG" | tr -d '\r' | awk '/TOTAL PSS:|^ *TOTAL /{for(i=1;i<=NF;i++) if($i ~ /^[0-9]+$/){print $i; exit}}')
    [ -n "$kb" ] && echo $((kb / 1024))
}

read -r W H < <(adb shell wm size | tr -d '\r' | sed -E 's/.*: ([0-9]+)x([0-9]+).*/\1 \2/')

# Answers the first-launch age screen (18 or older, then Accept) if it is showing; otherwise
# the taps land on the open creek and do no harm.
pass_gate() {
    adb shell input tap $((W * 81 / 100)) $((H * 367 / 1000))
    sleep 1
    adb shell input tap $((W * 73 / 100)) $((H * 745 / 1000))
    sleep 2
}

# Crashes, ANRs and Unity exceptions since the last logcat clear; saves the log for the step.
check_log() {
    local step=$1
    adb logcat -d > "$OUT/$step.log"
    local bad
    bad=$(grep -E "FATAL EXCEPTION|ANR in $PKG|Unity *: .*(Exception|NullReference)" "$OUT/$step.log" | head -5)
    if [ -n "$bad" ]; then
        fail "$step: errors in logcat ($OUT/$step.log)"
        echo "$bad" | sed 's/^/      /'
        return 1
    fi
    return 0
}

step_done() {
    local step=$1
    if alive && check_log "$step"; then pass "$step (PSS $(pss_mb) MB)"; else alive || fail "$step: process not running"; fi
    adb exec-out screencap -p > "$OUT/$step.png"
}

echo "device: $(adb shell getprop ro.product.model | tr -d '\r'), Android $(adb shell getprop ro.build.version.release | tr -d '\r')"
echo "apk: $APK"

# --- save backup ---
adb shell am force-stop "$PKG"
BACKUP="$OUT/prefs-backup.xml"
had_save=0
if adb exec-out run-as "$PKG" cat "$PREFS" > "$BACKUP" 2>/dev/null && [ -s "$BACKUP" ]; then
    had_save=1
    echo "save backed up to $BACKUP"
fi

restore() {
    adb shell am force-stop "$PKG"
    adb shell settings put system font_scale 1.0 >/dev/null
    adb shell settings put system accelerometer_rotation 1 >/dev/null
    adb shell svc wifi enable >/dev/null 2>&1
    adb shell svc data enable >/dev/null 2>&1
    if [ "$had_save" = 1 ]; then
        adb push "$BACKUP" /data/local/tmp/nc-prefs.xml >/dev/null
        adb shell run-as "$PKG" cp /data/local/tmp/nc-prefs.xml "$PREFS"
        adb shell rm /data/local/tmp/nc-prefs.xml
        echo "save restored"
    fi
}
trap restore EXIT

if ! adb install -r "$APK" >/dev/null; then
    fail "install (a build signed with another key needs an uninstall first, which drops the save)"
    exit 1
fi
pass "install"

# 1. Cold start: the process comes up and draws within the budget.
adb logcat -c
launch
sleep 12
pass_gate
step_done "1-cold-start"
load=$(grep -oE "game_loaded.*load_ms[^,)]*" "$OUT/1-cold-start.log" | head -1)
[ -n "$load" ] && echo "      $load"

# 2. Warm start: home, wait, back to the game.
adb logcat -c
adb shell input keyevent KEYCODE_HOME
sleep 5
launch
sleep 5
step_done "2-warm-start"

# 3. Killed in the background (low memory): the save written on pause brings the game back.
adb logcat -c
adb shell input keyevent KEYCODE_HOME
sleep 3
adb shell am kill "$PKG"
sleep 2
launch
sleep 12
step_done "3-killed-in-background"

# 4. No network: trusted time and Firebase fail, the game still runs and pays nothing offline.
adb shell am force-stop "$PKG"
adb shell svc wifi disable >/dev/null 2>&1
adb shell svc data disable >/dev/null 2>&1
sleep 3
adb logcat -c
launch
sleep 15
step_done "4-no-network"
adb shell svc wifi enable >/dev/null 2>&1
adb shell svc data enable >/dev/null 2>&1

# 5. Largest usual font scale: text grows to its 130% cap.
adb shell am force-stop "$PKG"
adb shell settings put system font_scale 1.3
adb logcat -c
launch
sleep 12
step_done "5-font-scale-1.3"
adb shell settings put system font_scale 1.0

# 6. Rotation request: the game stays portrait and keeps running.
adb logcat -c
adb shell settings put system accelerometer_rotation 0
adb shell settings put system user_rotation 1
sleep 4
step_done "6-rotation"
adb shell settings put system user_rotation 0
adb shell settings put system accelerometer_rotation 1

# 7. Android's monkey: random taps and drags only (no system keys or app switches).
adb logcat -c
monkey_out=$(adb shell monkey -p "$PKG" -s 42 --throttle 60 \
    --pct-touch 55 --pct-motion 35 --pct-trackball 0 --pct-nav 0 --pct-majornav 0 \
    --pct-syskeys 0 --pct-appswitch 0 --pct-anyevent 0 --pct-flip 0 --pct-pinchzoom 10 \
    -v "$EVENTS" 2>&1)
echo "$monkey_out" > "$OUT/7-monkey.txt"
if echo "$monkey_out" | grep -qE "// CRASH|// NOT RESPONDING|Monkey aborted"; then
    adb logcat -d > "$OUT/7-monkey.log"
    fail "7-monkey: crash or ANR ($OUT/7-monkey.txt, $OUT/7-monkey.log)"
    echo "$monkey_out" | grep -m1 -A3 -E "// CRASH|// NOT RESPONDING" | sed 's/^/      /'
else
    step_done "7-monkey-$EVENTS-events"
fi

# 8. Memory after all of it (design doc budget: PSS < 300 MB).
mb=$(pss_mb)
if [ -n "$mb" ]; then
    if [ "$mb" -lt 300 ]; then pass "8-memory: PSS $mb MB"; else fail "8-memory: PSS $mb MB over 300"; fi
fi

echo
[ "$failures" = 0 ] && echo "all device steps passed" || echo "$failures step(s) failed; logs and screenshots in $OUT"
exit "$failures"
