#!/usr/bin/env bash
set -euo pipefail
project_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
mkdir -p "$project_dir/Logs/main-menu"
exec "$project_dir/Builds/build_linux_Server/SpaceStationBreakdownServer.x86_64" -batchmode -nographics -job-worker-count 4 -trainer-role authority -trainer-bind 0.0.0.0 -trainer-port "${1:-7777}" -logFile "$project_dir/Logs/main-menu/server.log"
