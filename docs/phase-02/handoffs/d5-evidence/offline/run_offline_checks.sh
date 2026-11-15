#!/usr/bin/env bash
# D5 phase 2 offline checks (NOT Unity, NOT the Unity Test Runner, NOT a build).
# Compiles pure sources with the Editor's Roslyn and runs NUnit tests with the Editor's .NET 6.0.21 runtime
# and the real com.unity.ext.nunit framework through a reflection runner. Writes only to $OUT.
# Usage: UNITY_DATA=<Editor/Data> OUT=<dir> run_offline_checks.sh   (run from the repository root)
set -euo pipefail
UNITY_DATA="${UNITY_DATA:-/mnt/c/Program Files/Unity/Hub/Editor/6000.3.28f1/Editor/Data}"
OUT="${OUT:?set OUT to a scratch directory}"
REPO="$(pwd)"
mkdir -p "$OUT"
DOTNET="$UNITY_DATA/NetCoreRuntime/dotnet.exe"
CSC="$UNITY_DATA/DotNetSdkRoslyn/csc.dll"
NETSTD="$UNITY_DATA/NetStandard/ref/2.1.0/netstandard.dll"
NUNIT="$UNITY_DATA/Resources/PackageManager/BuiltInPackages/com.unity.ext.nunit/net40/unity-custom/nunit.framework.dll"
RT="$UNITY_DATA/NetCoreRuntime/shared/Microsoft.NETCore.App/6.0.21"
P=Assets/_Project
w() { # Windows path for the Editor's Windows executables (wslpath may be unavailable)
  local a; a="$(readlink -f "$1" 2>/dev/null || echo "$1")"
  case "$a" in
    /mnt/[a-z]/*) local d="${a:5:1}"; echo "${d^^}:$(echo "${a:6}" | tr '/' '\\')" ;;
    *) echo "\\\\wsl.localhost\\${WSL_DISTRO_NAME:-Ubuntu}$(echo "$a" | tr '/' '\\')" ;;
  esac; }

rsp() { # rsp <file> <out.dll> <target> ; then sources/refs on stdin
  local file="$1"; shift
  { echo "-nologo"; echo "-langversion:9.0"; echo "-nullable:disable"; echo "-deterministic"; echo "-out:\"$(w "$1")\""; echo "-target:$2"
    while read -r line; do echo "$line"; done; } > "$file"
}

# 1. Purity: Simulation + Application against netstandard 2.1 only, warnings as errors, XML docs checked.
{ echo "-nostdlib+"; echo "-r:\"$(w "$NETSTD")\""; echo "-warnaserror+"; echo "-nowarn:1591,1573"; echo "-doc:\"$(w "$OUT/Pure.xml")\""
  find "$P/Runtime/Simulation" "$P/Runtime/Application" -name '*.cs' | sort | while read -r f; do echo "\"$(w "$REPO/$f")\""; done
} | rsp "$OUT/pure.rsp" "$OUT/Pure.dll" library
echo "== compile Pure (Runtime/Simulation + Runtime/Application, netstandard only)"
"$DOTNET" exec "$(w "$CSC")" "@$(w "$OUT/pure.rsp")" | tee "$OUT/compile-pure.txt"

# Reference set for net6 test code.
RUNTIME_REFS=$(ls "$RT" | grep -E '^(System|Microsoft|mscorlib|netstandard).*\.dll$' | grep -v -E 'Native|DiaSym' )

# 2. D5 tooling + NUnit tests (pure parts only).
{ echo "-nostdlib+"; echo "-nowarn:1701,1702"
  for r in $RUNTIME_REFS; do echo "-r:\"$(w "$RT/$r")\""; done
  echo "-r:\"$(w "$OUT/Pure.dll")\""; echo "-r:\"$(w "$NUNIT")\""
  { find "$P/Editor/Validation/Phase02" -name '*.cs'
    find "$P/Tests/EditMode/Validation/Phase02" -name '*.cs'
    find "$P/Tests/EditMode/Contracts/Editor" -name '*.cs'
    find "$P/Tests/EditMode/Simulation/Editor" -name '*.cs'
    for t in MissionSessionTests MissionOutcomeEvaluatorTests FakeSessionPortTests; do echo "$P/Tests/EditMode/Application/Editor/$t.cs"; done
  } | sort | while read -r f; do echo "\"$(w "$REPO/$f")\""; done
} | rsp "$OUT/tests.rsp" "$OUT/D5Phase02Tests.dll" library
echo "== compile D5Phase02Tests (Editor/Validation/Phase02 + NUnit tests)"
"$DOTNET" exec "$(w "$CSC")" "@$(w "$OUT/tests.rsp")" | tee "$OUT/compile-tests.txt"

# 3. Runner.
{ echo "-nostdlib+"; echo "-noconfig"
  for r in $RUNTIME_REFS; do echo "-r:\"$(w "$RT/$r")\""; done
  echo "-r:\"$(w "$NUNIT")\""; echo "\"$(w "$REPO/docs/phase-02/handoffs/d5-evidence/offline/ReflectionNUnitRunner.cs")\""
} | rsp "$OUT/runner.rsp" "$OUT/Runner.dll" exe
"$DOTNET" exec "$(w "$CSC")" "@$(w "$OUT/runner.rsp")" | tee "$OUT/compile-runner.txt"
cat > "$OUT/Runner.runtimeconfig.json" <<JSON
{ "runtimeOptions": { "tfm": "net6.0", "framework": { "name": "Microsoft.NETCore.App", "version": "6.0.21" } } }
JSON
rm -f "$OUT/nunit.framework.dll"; cp "$NUNIT" "$OUT/"

echo "== run"
set +e
"$DOTNET" exec "$(w "$OUT/Runner.dll")" "$(w "$OUT/D5Phase02Tests.dll")" | tr -d '\r' | tee "$OUT/nunit-results.txt"
code=${PIPESTATUS[0]}
echo "runner exit $code"
exit $code
