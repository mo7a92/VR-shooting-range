#!/bin/bash
# round 2 final: screenshot run of the hand test, then sync -> prefabs -> full PlayMode -> EditMode -> Windows build + zip
S=${VRRANGE_SCRATCH:?set VRRANGE_SCRATCH to the scratch dir}
rm -f $S/unityproj/Logs/hand_report.txt
SKIP_ACTIVATE=1 SKIP_PREFABS=1 $S/round2_gfx.sh
cp $S/unityproj/Logs/hand_report.txt $S/hand_report_gfx.txt 2>/dev/null
SKIP_ACTIVATE=1 $S/round1.sh
$S/build_win.sh
echo "== chain done $(date +%T)"
