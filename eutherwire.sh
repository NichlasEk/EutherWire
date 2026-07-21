#!/usr/bin/env bash

set -Eeuo pipefail

EW_REPO_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
EW_DOTNET="${DOTNET:-dotnet}"
EW_EXAMPLE_PROJECT="$EW_REPO_DIR/examples/garage.eutherwire"
EW_WORK_DIR="$EW_REPO_DIR/.eutherwire-work"
EW_DEMO_PROJECT="$EW_WORK_DIR/garage-demo.eutherwire"
EW_3D_DEMO_PROJECT="$EW_WORK_DIR/garage-3d-demo.eutherwire"
EW_WALL_DEMO_PROJECT="$EW_WORK_DIR/garage-wall-demo.eutherwire"
EW_MOBILE_APK="$EW_REPO_DIR/src/EutherWire.Mobile/bin/Release/net10.0-android/android-arm64/publish/se.eutherwire.mobile-Signed.apk"
EW_ANDROID_KEYSTORE="${EUTHERWIRE_ANDROID_KEYSTORE:-${HOME}/.android/debug.keystore}"

say() {
    printf '\n==> %s\n' "$*"
}

die() {
    printf 'EutherWire: %s\n' "$*" >&2
    exit 1
}

usage() {
    cat <<'EOF'
EutherWire helper

Usage:
  ./eutherwire.sh                 Build and open a safe, writable Garage Draft copy
  ./eutherwire.sh demo            Same as above; keeps edits between runs
  ./eutherwire.sh 3d              Open a safe Garage Draft directly in 3D
  ./eutherwire.sh wall            Open a safe Garage Draft in wall elevation view
  ./eutherwire.sh run PROJECT     Open an explicit .eutherwire project
  ./eutherwire.sh check           Build, run document checks, and analyze Garage Draft
  ./eutherwire.sh report [PROJECT]
  ./eutherwire.sh properties [PROJECT]
  ./eutherwire.sh tasks [PROJECT]
  ./eutherwire.sh snapshot [PROJECT] [OUTPUT.eutherwire-snapshot]
  ./eutherwire.sh import-snapshot SNAPSHOT NEW_PROJECT
  ./eutherwire.sh mobile-build
  ./eutherwire.sh mobile-install
  ./eutherwire.sh export [PROJECT] [OUTPUT.svg]
  ./eutherwire.sh png [PROJECT] [OUTPUT.png]
  ./eutherwire.sh help

The default demo is stored under .eutherwire-work/ and never modifies the
tracked example. Delete that directory manually if you want a completely new
demo project.
EOF
}

prepare() {
    command -v "$EW_DOTNET" >/dev/null 2>&1 || die "dotnet was not found in PATH. EutherWire requires .NET 10."
    cd -- "$EW_REPO_DIR"

    if [[ ! -f vendor/WaylandForge/src/SystemRegisIII.WaylandForge.App/SystemRegisIII.WaylandForge.App.csproj ]]; then
        command -v git >/dev/null 2>&1 || die "git is required to initialize WaylandForge."
        say "Initializing pinned WaylandForge submodule"
        git submodule update --init --recursive
    fi
}

build() {
    say "Restoring dependencies"
    "$EW_DOTNET" restore EutherWire.slnx --nologo --disable-build-servers -m:1
    say "Building EutherWire"
    "$EW_DOTNET" build EutherWire.slnx --nologo --no-restore --disable-build-servers -m:1
}

build_mobile() {
    [[ -f "$EW_ANDROID_KEYSTORE" ]] || die "Android signing keystore was not found: $EW_ANDROID_KEYSTORE"
    "$EW_DOTNET" build-server shutdown >/dev/null 2>&1 || true
    say "Building signed ARM64 Android package"
    "$EW_DOTNET" publish src/EutherWire.Mobile/EutherWire.Mobile.csproj \
        --configuration Release \
        --framework net10.0-android \
        --runtime android-arm64 \
        -m:1 \
        -nodeReuse:false \
        -p:PublishTrimmed=false \
        -p:RunAOTCompilation=false \
        -p:AndroidEnableProfiledAot=false \
        -p:AndroidPackageFormat=apk \
        -p:AndroidKeyStore=true \
        -p:AndroidSigningKeyStore="$EW_ANDROID_KEYSTORE" \
        -p:AndroidSigningStorePass=android \
        -p:AndroidSigningKeyAlias=androiddebugkey \
        -p:AndroidSigningKeyPass=android
    [[ -f "$EW_MOBILE_APK" ]] || die "Android APK was not produced at: $EW_MOBILE_APK"

    local sdk_root="${ANDROID_HOME:-${ANDROID_SDK_ROOT:-/opt/android-sdk}}"
    local apksigner
    apksigner="$(find "$sdk_root/build-tools" -mindepth 2 -maxdepth 2 -type f -name apksigner -print 2>/dev/null | sort -V | tail -n 1)"
    [[ -x "$apksigner" ]] || die "Android apksigner was not found under: $sdk_root/build-tools"
    "$apksigner" verify --verbose "$EW_MOBILE_APK" || die "Android rejected the generated APK signature"
}

require_project() {
    local project="$1"
    [[ -f "$project/project.toml" ]] || die "project.toml was not found under: $project"
}

validate_project() {
    local project="$1"
    require_project "$project"
    "$EW_DOTNET" run --project src/EutherWire.Cli/EutherWire.Cli.csproj --no-build -- validate "$project"
}

require_wayland() {
    if [[ -z "${WAYLAND_DISPLAY:-}" ]]; then
        die "WAYLAND_DISPLAY is not set. Run this from an active Wayland desktop session."
    fi
}

open_project() {
    local project="$1"
    local mode="${2:-}"
    validate_project "$project"
    require_wayland
    say "Opening $project"
    if [[ "$mode" == "--3d" ]]; then
        exec "$EW_DOTNET" run --project src/EutherWire.App/EutherWire.App.csproj --no-build -- "$project" --3d
    fi
    if [[ "$mode" == "--wall" ]]; then
        exec "$EW_DOTNET" run --project src/EutherWire.App/EutherWire.App.csproj --no-build -- "$project" --wall
    fi
    exec "$EW_DOTNET" run --project src/EutherWire.App/EutherWire.App.csproj --no-build -- "$project"
}

EW_COMMAND="${1:-demo}"
if [[ "$EW_COMMAND" == "help" || "$EW_COMMAND" == "-h" || "$EW_COMMAND" == "--help" ]]; then
    usage
    exit 0
fi

prepare

case "$EW_COMMAND" in
    demo)
        build
        if [[ ! -f "$EW_DEMO_PROJECT/project.toml" ]]; then
            say "Creating writable Garage Draft demo"
            mkdir -p -- "$EW_WORK_DIR"
            "$EW_DOTNET" run --project src/EutherWire.Cli/EutherWire.Cli.csproj --no-build -- create-demo "$EW_DEMO_PROJECT"
        fi
        open_project "$EW_DEMO_PROJECT"
        ;;
    3d)
        build
        if [[ ! -f "$EW_3D_DEMO_PROJECT/project.toml" ]]; then
            say "Creating writable 3D Garage Draft demo"
            mkdir -p -- "$EW_WORK_DIR"
            "$EW_DOTNET" run --project src/EutherWire.Cli/EutherWire.Cli.csproj --no-build -- create-demo "$EW_3D_DEMO_PROJECT"
        fi
        open_project "$EW_3D_DEMO_PROJECT" --3d
        ;;
    wall)
        build
        if [[ ! -f "$EW_WALL_DEMO_PROJECT/project.toml" ]]; then
            say "Creating writable wall-elevation Garage Draft demo"
            mkdir -p -- "$EW_WORK_DIR"
            "$EW_DOTNET" run --project src/EutherWire.Cli/EutherWire.Cli.csproj --no-build -- create-demo "$EW_WALL_DEMO_PROJECT"
        fi
        open_project "$EW_WALL_DEMO_PROJECT" --wall
        ;;
    run)
        [[ $# -ge 2 ]] || die "run requires a .eutherwire project path"
        EW_PROJECT="$2"
        build
        open_project "$EW_PROJECT"
        ;;
    check)
        build
        say "Running document checks"
        "$EW_DOTNET" run --project tests/EutherWire.Document.Checks/EutherWire.Document.Checks.csproj --no-build
        say "Analyzing Garage Draft"
        "$EW_DOTNET" run --project src/EutherWire.Cli/EutherWire.Cli.csproj --no-build -- report "$EW_EXAMPLE_PROJECT"
        ;;
    report)
        EW_PROJECT="${2:-$EW_EXAMPLE_PROJECT}"
        build
        require_project "$EW_PROJECT"
        "$EW_DOTNET" run --project src/EutherWire.Cli/EutherWire.Cli.csproj --no-build -- report "$EW_PROJECT"
        ;;
    properties)
        EW_PROJECT="${2:-$EW_EXAMPLE_PROJECT}"
        build
        require_project "$EW_PROJECT"
        "$EW_DOTNET" run --project src/EutherWire.Cli/EutherWire.Cli.csproj --no-build -- properties "$EW_PROJECT"
        ;;
    tasks)
        EW_PROJECT="${2:-$EW_EXAMPLE_PROJECT}"
        build
        require_project "$EW_PROJECT"
        "$EW_DOTNET" run --project src/EutherWire.Cli/EutherWire.Cli.csproj --no-build -- tasks "$EW_PROJECT"
        ;;
    snapshot)
        EW_PROJECT="${2:-$EW_EXAMPLE_PROJECT}"
        EW_OUTPUT="${3:-$EW_WORK_DIR/garage.eutherwire-snapshot}"
        build
        require_project "$EW_PROJECT"
        "$EW_DOTNET" run --project src/EutherWire.Cli/EutherWire.Cli.csproj --no-build -- snapshot-export "$EW_PROJECT" "$EW_OUTPUT"
        ;;
    import-snapshot)
        [[ $# -ge 3 ]] || die "import-snapshot requires a snapshot path and a new .eutherwire project path"
        build
        "$EW_DOTNET" run --project src/EutherWire.Cli/EutherWire.Cli.csproj --no-build -- snapshot-import "$2" "$3"
        ;;
    mobile-build)
        build_mobile
        say "Android APK ready"
        printf '%s\n' "$EW_MOBILE_APK"
        ;;
    mobile-install)
        command -v adb >/dev/null 2>&1 || die "adb was not found in PATH"
        build_mobile
        say "Installing EutherWire on the connected Android device"
        adb install -r "$EW_MOBILE_APK"
        adb shell monkey -p se.eutherwire.mobile 1 >/dev/null 2>&1 || true
        ;;
    export)
        EW_PROJECT="${2:-$EW_EXAMPLE_PROJECT}"
        EW_OUTPUT="${3:-$EW_WORK_DIR/garage.svg}"
        build
        require_project "$EW_PROJECT"
        "$EW_DOTNET" run --project src/EutherWire.Cli/EutherWire.Cli.csproj --no-build -- export-svg "$EW_PROJECT" "$EW_OUTPUT"
        ;;
    png)
        EW_PROJECT="${2:-$EW_EXAMPLE_PROJECT}"
        EW_OUTPUT="${3:-$EW_WORK_DIR/garage.png}"
        build
        require_project "$EW_PROJECT"
        "$EW_DOTNET" run --project src/EutherWire.Cli/EutherWire.Cli.csproj --no-build -- export-png "$EW_PROJECT" "$EW_OUTPUT"
        ;;
    *)
        usage >&2
        die "unknown command: $EW_COMMAND"
        ;;
esac
