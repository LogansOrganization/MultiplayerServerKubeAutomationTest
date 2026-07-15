#!/usr/bin/env bash
set -euo pipefail

echo "--- Fetching commit $GIT_REF ---"
mkdir -p /workspace
git -C /workspace init --quiet
git -C /workspace remote add origin \
  "https://x-access-token:${GITHUB_TOKEN}@github.com/LogansOrganization/MultiplayerServerKubeAutomationTest.git"
git -C /workspace fetch --quiet --depth 1 origin "$GIT_REF"
git -C /workspace checkout --quiet FETCH_HEAD

echo "--- Activating Unity license ---"
unity-editor \
  -batchmode -quit -nographics \
  -serial "$UNITY_SERIAL" \
  -username "$UNITY_EMAIL" \
  -password "$UNITY_PASSWORD" \
  -projectPath /workspace \
  -logFile /dev/stdout

echo "--- Building server (Build.Server) ---"
set +e
unity-editor \
  -batchmode -quit -nographics \
  -projectPath /workspace \
  -executeMethod Build.Server \
  -logFile /dev/stdout
BUILD_EXIT_CODE=$?
set -e

echo "--- Returning Unity license ---"
unity-editor \
  -batchmode -quit -nographics \
  -returnlicense \
  -projectPath /workspace \
  -logFile /dev/stdout

echo "--- Build exit code: $BUILD_EXIT_CODE ---"
exit $BUILD_EXIT_CODE
