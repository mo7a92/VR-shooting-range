#!/bin/bash
# Headset round 1: activate -> sync repo into the scratch project -> prefab/settings rebuild (scene + lightmaps untouched)
# -> PlayMode tests -> EditMode tests. Logs in unityproj/Logs.
S=${VRRANGE_SCRATCH:?set VRRANGE_SCRATCH to the scratch dir}
cd /home/user/VR-shooting-range
if [ "$SKIP_ACTIVATE" != "1" ]; then
echo "== activate $(date +%T)"
PASS_CREDS=1 $S/unity_run.sh activate.log -username "$UNITY_EMAIL" -password "$UNITY_PASSWORD" -serial "$UNITY_SERIAL" -quit | tail -1
grep -E "Successfully activated|Error" $S/unityproj/Logs/activate.log | grep -viE "password|serial|username|token" | tail -3; rm -f $S/unityproj/Logs/activate.log
fi
echo "== sync $(date +%T)"
git ls-files -z -co --exclude-standard | tar --null --exclude="Assets/Generated*" --exclude="Assets/Scenes*" --exclude="Assets/Settings*" --exclude="Assets/XR*" --exclude="Assets/XRI*" --exclude="ProjectSettings/*.asset" --exclude="Packages/packages-lock.json" -cf - -T - | (cd $S/unityproj && tar -xf -)
if [ "$SKIP_PREFABS" != "1" ]; then
echo "== prefabs $(date +%T)"
$S/unity_run.sh prefabs_r1.log -projectPath /project -executeMethod VRRange.Editor.RangeProjectBootstrap.RunPrefabsBatch -quit | tail -1
grep -cE "error CS" $S/unityproj/Logs/prefabs_r1.log
fi
if [ "$SKIP_PLAYMODE" != "1" ]; then
echo "== playmode $(date +%T)"
rm -f $S/unityproj/Logs/playmode-results.xml
$S/unity_run.sh playmode_r1.log -projectPath /project -runTests -testPlatform PlayMode ${PLAYMODE_FILTER:+-testFilter "$PLAYMODE_FILTER"} -testResults /project/Logs/playmode-results.xml | tail -1
fi
if [ "$SKIP_EDITMODE" != "1" ]; then
echo "== editmode $(date +%T)"
rm -f $S/unityproj/Logs/editmode-results.xml
$S/unity_run.sh editmode_r1.log -projectPath /project -runTests -testPlatform EditMode -testResults /project/Logs/editmode-results.xml | tail -1
fi
echo "== done $(date +%T)"
