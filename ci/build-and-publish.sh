#!/usr/bin/env bash
set -euo pipefail

# Usage:
#   ./ci/build-and-publish.sh <platform> <contentVersion> <unityPath>
# Example:
#   ./ci/build-and-publish.sh Android v42 /Applications/Unity/Hub/Editor/2022.3.0f1/Unity.app/Contents/MacOS/Unity

PLATFORM="${1:?platform required, e.g. Android, StandaloneWindows64}"
VERSION="${2:?contentVersion required, e.g. v42}"
UNITY="${3:?Unity executable path required}"

PROJECT_ROOT="$(pwd)"
LOG_DIR="${PROJECT_ROOT}/Logs"
BUILD_DIR="${PROJECT_ROOT}/GameAssetSystemBuild/Release/${PLATFORM}/${VERSION}"

mkdir -p "${LOG_DIR}"
mkdir -p "${BUILD_DIR}"

LOG="${LOG_DIR}/release-build-${PLATFORM}-${VERSION}.log"

"${UNITY}" \
  -batchmode -nographics \
  -projectPath "${PROJECT_ROOT}" \
  -executeMethod Game.AssetSystem.Editor.Release.ReleaseBuilder.Build \
  -buildPlatform "${PLATFORM}" \
  -contentVersion "${VERSION}" \
  -quit -logFile "${LOG}"

if [ ! -d "${BUILD_DIR}" ]; then
  echo "[ci] build output missing: ${BUILD_DIR}" >&2
  exit 1
fi

sha256sum "${BUILD_DIR}"/* > "${BUILD_DIR}/checksums.sha256"

# TODO: replace with actual uploader.
# aws s3 cp "${BUILD_DIR}/" "s3://your-cdn/${PLATFORM}/${VERSION}/" --recursive

# Append release record.
RECORD="${PROJECT_ROOT}/GameAssetSystemBuild/Release/releases.jsonl"

node -e '
const fs = require("fs");
const path = require("path");
const platform = process.argv[1];
const version = process.argv[2];
const buildDir = process.argv[3];
const checksums = fs.readFileSync(path.join(buildDir, "checksums.sha256"), "utf8")
  .split("\n").filter(Boolean).map(line => {
    const [sha, p] = line.split(/\s+/);
    return { sha256: sha, file: path.basename(p) };
  });
const record = {
  platform: platform,
  version: version,
  uploadedAt: new Date().toISOString(),
  checksums: checksums
};
fs.appendFileSync(process.argv[4], JSON.stringify(record) + "\n");
' "${PLATFORM}" "${VERSION}" "${BUILD_DIR}" "${RECORD}"

echo "[ci] published ${PLATFORM}/${VERSION}"
