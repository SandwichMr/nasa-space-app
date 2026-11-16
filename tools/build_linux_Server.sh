#!/usr/bin/env bash
set -euo pipefail
project_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
editor_bin="${UNITY_EDITOR:-$HOME/Unity/Hub/Editor/6000.3.28f1/Editor/Unity}"
mkdir -p "$project_dir/Logs/main-menu"
exec "$editor_bin" -batchmode -nographics -job-worker-count 4 -projectPath "$project_dir" -executeMethod Trainer.Editor.MainMenu.MainMenuBuilder.BuildLinuxServer -quit -logFile "$project_dir/Logs/main-menu/server-build.log"
