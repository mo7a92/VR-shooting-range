#!/bin/bash
# Round 1: Windows x64 PCVR player only (no APK), then zip it.
S=${VRRANGE_SCRATCH:?set VRRANGE_SCRATCH to the scratch dir}
cd $S
echo "== windows start $(date +%T)"
rm -f unityproj/Logs/build_pcvr.log unityproj/Logs/VRRangeBuildReport_Windows.txt
rm -rf unityproj/Builds/Windows
$S/unity_run.sh build_pcvr.log -projectPath /project -buildTarget Win64 -executeMethod VRRange.Editor.PcvrBuildScript.BuildWindowsBatch -outPath Builds/Windows/VRShootingRange.exe -quit | tail -1
cat unityproj/Logs/VRRangeBuildReport_Windows.txt 2>/dev/null
grep -E "error CS|warning CS" unityproj/Logs/build_pcvr.log | sort -u | head -5
echo "== zip $(date +%T)"
cd $S/unityproj/Builds && rm -f VRShootingRange_PCVR_Windows.zip && ( cd Windows && rm -rf *_BackUpThisFolder_ButDontShipItWithYourGame *_BurstDebugInformation_DoNotShip && zip -qr ../VRShootingRange_PCVR_Windows.zip . -x "*DoNotShip*" ) && ls -la $S/unityproj/Builds/*.zip && sha256sum $S/unityproj/Builds/VRShootingRange_PCVR_Windows.zip
echo "== builds done $(date +%T)"
