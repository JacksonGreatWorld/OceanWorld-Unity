#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat <<'EOF'
Build OceanWorld with Unity 6000.0.84f1 and its Android Build Support modules.

Usage: UNITY_EDITOR=/path/to/Unity ./Scripts/build-android.sh <major.minor.patch> <versionCode>
Example: UNITY_EDITOR=/path/to/Unity ./Scripts/build-android.sh 2.6.2 262

Close other Unity instances for this project first. Fetch Git LFS assets before
building. GRADLE_USER_HOME may be set to relocate Gradle caches. Build logs and
APK/manifests are written under Builds/; signing keys are managed separately.
EOF
}

if [[ "${1:-}" == "--help" || "${1:-}" == "-h" ]]; then usage; exit 0; fi
if [[ $# != 2 || ! "$1" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ || ! "$2" =~ ^[1-9][0-9]*$ ]]; then
  usage >&2
  exit 2
fi
# Android versionCode is a positive signed integer, limited to 2,100,000,000.
if [[ ${#2} -gt 10 ]] || (( 10#$2 > 2100000000 )); then
  echo 'versionCode must be between 1 and 2100000000.' >&2
  exit 2
fi
if [[ -z "${UNITY_EDITOR:-}" || ! -x "$UNITY_EDITOR" ]]; then
  echo 'Set UNITY_EDITOR to the executable for Unity 6000.0.84f1.' >&2
  exit 2
fi

OCEAN_PROJECT_ROOT="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$OCEAN_PROJECT_ROOT"
mkdir -p Builds/logs
OCEAN_BUILD_LOG="$OCEAN_PROJECT_ROOT/Builds/logs/android-$(date -u +%Y%m%dT%H%M%SZ)-$$.log"
printf 'Building OceanWorld %s (code %s)\nLog: %s\n' "$1" "$2" "$OCEAN_BUILD_LOG"
exec "$UNITY_EDITOR" -batchmode -quit -buildTarget Android \
  -projectPath "$OCEAN_PROJECT_ROOT" \
  -executeMethod SwanUnderwater.Editor.CodexBuild.BuildAndroid \
  -codexReleaseVersion "$1" -codexVersionCode "$2" \
  -logFile "$OCEAN_BUILD_LOG"
