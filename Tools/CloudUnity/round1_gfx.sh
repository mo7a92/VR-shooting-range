#!/bin/bash
# sync -> prefab rebuild -> graphics PlayMode run of one test filter (screenshots to unityproj/Logs/shots)
S=${VRRANGE_SCRATCH:?set VRRANGE_SCRATCH to the scratch dir}
cd /home/user/VR-shooting-range
echo "== sync $(date +%T)"
git ls-files -z -co --exclude-standard | tar --null --exclude="Assets/Generated*" --exclude="Assets/Scenes*" --exclude="Assets/Settings*" --exclude="Assets/XR*" --exclude="Assets/XRI*" --exclude="ProjectSettings/*.asset" --exclude="Packages/packages-lock.json" -cf - -T - | (cd $S/unityproj && tar -xf -)
if [ "$SKIP_PREFABS" != "1" ]; then
echo "== prefabs $(date +%T)"
$S/unity_run.sh prefabs_r1.log -projectPath /project -executeMethod VRRange.Editor.RangeProjectBootstrap.RunPrefabsBatch -quit | tail -1
grep -E "error CS" $S/unityproj/Logs/prefabs_r1.log | sort -u | head -5
fi
echo "== gfx test $(date +%T)"
rm -rf $S/unityproj/Logs/shots $S/unityproj/Logs/gfx-results.xml
VRRANGE_SHOT_WIDTH=960 VRRANGE_SHOT_HEIGHT=640 timeout 2700 $S/unity_run_gfx.sh gfx_r1.log -projectPath /project -runTests -testPlatform PlayMode -testFilter "${GFX_FILTER:-VRRange.Tests.HeadsetRound1Tests.FingersWrapEveryGripWithoutPenetratingItAndTheThumbOpposesTheFingers}" -testResults /project/Logs/gfx-results.xml | tail -1
echo "== gfx done $(date +%T)"
ls $S/unityproj/Logs/shots 2>/dev/null | head -30
