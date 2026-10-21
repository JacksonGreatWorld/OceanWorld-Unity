#!/usr/bin/env bash
set -euo pipefail
usage() {
  cat <<'HELP'
Build the Quest 3 profile with Unity 6000.0.84f1 Android Build Support.
Usage: UNITY_EDITOR=/path/to/Unity ./Scripts/build-quest3.sh <major.minor.patch> <versionCode>
Example: UNITY_EDITOR=/path/to/Unity ./Scripts/build-quest3.sh 2.7.0 270
Close other Unity instances for this project. Package installation must finish first.
Uses the existing Library; restores PICO settings after normal success/failure.
A killed Editor or power loss cannot execute finally: inspect settings before rebuilding.
HELP
}
if [[ "${1:-}" == '--help' || "${1:-}" == '-h' ]]; then usage; exit 0; fi
if [[ $# != 2 || ! "$1" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ || ! "$2" =~ ^[1-9][0-9]*$ ]]; then usage >&2; exit 2; fi
if [[ ${#2} -gt 10 ]] || (( 10#$2 > 2100000000 )); then echo 'Invalid Android versionCode.' >&2; exit 2; fi
if [[ -z "${UNITY_EDITOR:-}" || ! -x "$UNITY_EDITOR" ]]; then echo 'Set UNITY_EDITOR to the Unity 6000.0.84f1 executable.' >&2; exit 2; fi
OCEAN_PROJECT_ROOT="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$OCEAN_PROJECT_ROOT"
mkdir -p Builds/logs
OCEAN_QUEST_LOG="$OCEAN_PROJECT_ROOT/Builds/logs/quest3-$(date -u +%Y%m%dT%H%M%SZ)-$$.log"
printf 'Building Quest 3 %s (code %s)\nLog: %s\n' "$1" "$2" "$OCEAN_QUEST_LOG"
exec "$UNITY_EDITOR" -batchmode -quit -buildTarget Android \
  -projectPath "$OCEAN_PROJECT_ROOT" -executeMethod SwanUnderwater.Editor.QuestBuild.BuildAndroid \
  -codexTargetPlatform quest3 -codexReleaseVersion "$1" -codexVersionCode "$2" -logFile "$OCEAN_QUEST_LOG"
