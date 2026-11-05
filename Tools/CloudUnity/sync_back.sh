#!/bin/bash
# Copy Unity-generated project state (assets with their .meta GUIDs, settings, scene, prefabs, package lock) from the
# scratch project back into the git working tree. Excludes Library/Logs/Builds/Temp and licence files.
set -e
S=${VRRANGE_SCRATCH:?set VRRANGE_SCRATCH to the scratch dir}
SRC=$S/unityproj; DST=/home/user/VR-shooting-range
cd $SRC
for d in Assets ProjectSettings Packages; do
  find "$d" -type f ! -name '*.alf' ! -name '*.ulf' -print0 | while IFS= read -r -d '' f; do
    mkdir -p "$DST/$(dirname "$f")"; cp -p "$f" "$DST/$f"
  done
done
# Unity-written but not wanted in git
rm -f "$DST/ProjectSettings/VRRangeBootstrapState.json"
echo "synced Assets, ProjectSettings, Packages back to the repo"
