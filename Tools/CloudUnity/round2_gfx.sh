#!/bin/bash
# activate (if needed) -> sync -> prefab rebuild -> graphics run of the hand penetration test
S=${VRRANGE_SCRATCH:?set VRRANGE_SCRATCH to the scratch dir}
cd /home/user/VR-shooting-range
if [ "$SKIP_ACTIVATE" != "1" ]; then
echo "== activate $(date +%T)"
PASS_CREDS=1 $S/unity_run.sh activate.log -username "$UNITY_EMAIL" -password "$UNITY_PASSWORD" -serial "$UNITY_SERIAL" -quit | tail -1
grep -E "Successfully activated|Error" $S/unityproj/Logs/activate.log | grep -viE "password|serial|username|token" | tail -2; rm -f $S/unityproj/Logs/activate.log
fi
GFX_FILTER="${GFX_FILTER:-VRRange.Tests.HandPenetrationTests}" $S/round1_gfx.sh
