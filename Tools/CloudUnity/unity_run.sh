#!/bin/bash
# Run the Unity 6000.3.27f1 editor (GameCI image) against the scratch project copy.
# Usage: unity_run.sh <logname> <unity args...>   (credentials are only passed when PASS_CREDS=1)
S=${VRRANGE_SCRATCH:?set VRRANGE_SCRATCH to the scratch dir}
LOG=$1; shift
CREDS=()
if [ "$PASS_CREDS" = "1" ]; then CREDS=(-e UNITY_EMAIL -e UNITY_PASSWORD -e UNITY_SERIAL); fi
mkdir -p $S/unityhome $S/unityproj/Logs $S/unitycfg
docker run --rm --network host --hostname unityci \
  -e HTTPS_PROXY="$HTTPS_PROXY" -e https_proxy="$HTTPS_PROXY" -e NO_PROXY="$NO_PROXY" -e no_proxy="$NO_PROXY" \
  -e SSL_CERT_FILE=/etc/ssl/certs/ca-certificates.crt "${CREDS[@]}" \
  -v /root/.ccr/ca-bundle.crt:/usr/local/share/ca-certificates/ccr-proxy.crt:ro \
  -v $S/unityhome:/root/.local/share/unity3d \
  -v $S/unitycfg:/root/.config/unity3d \
  -v $S/gradlehome:/root/.gradle \
  -v $S/unityproj:/project \
  -v $S/winmono/root:/opt/unity/Editor/Data/PlaybackEngines/WindowsStandaloneSupport:ro \
  -w /project \
  unityci/editor:ubuntu-6000.3.27f1-android-3.2.2 \
  bash -c 'update-ca-certificates >/dev/null 2>&1; JDK=/opt/unity/Editor/Data/PlaybackEngines/AndroidPlayer/OpenJDK; $JDK/bin/keytool -importcert -noprompt -alias ccr-proxy -file /usr/local/share/ca-certificates/ccr-proxy.crt -keystore $JDK/lib/security/cacerts -storepass changeit >/dev/null 2>&1; xvfb-run -a /opt/unity/Editor/Unity -batchmode -nographics -logFile /project/Logs/'"$LOG"' "$@"; echo "UNITY_EXIT=$?"' _ "$@" 2>&1 | grep -v "IPv4 forwarding"
