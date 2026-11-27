#!/usr/bin/env bash
# T24: package the Unreal game with everything it needs to run on a machine without .NET or a
# checkout: the self-contained sim-host and the content/ JSON it reads.
#
#   unreal/package.sh [out-dir]          (default: unreal/Build/Package)
#
# UE_ROOT points at the engine (default: the Windows launcher install of 5.8). Produces
# <out>/Windows/RtsBridge.exe (or <out>/Linux/RtsBridge.sh) with
#   <out>/<Platform>/RtsBridge/dist/<rid>/sim-host[.exe]   (found by URtsSimHostBridge)
#   <out>/<Platform>/RtsBridge/rts/content/                (found by RtsContent::ContentRoot)
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
repo="$(cd "$here/.." && pwd)"
out="${1:-$here/Build/Package}"
case "$(uname -s)" in
  MINGW*|MSYS*|CYGWIN*) platform=Win64; staged=Windows; rid=win-x64; uat_ext=bat; exe=.exe
    UE_ROOT="${UE_ROOT:-/c/Program Files/Epic Games/UE_5.8}" ;;
  *) platform=Linux; staged=Linux; rid=linux-x64; uat_ext=sh; exe=
    UE_ROOT="${UE_ROOT:-$HOME/.local/opt/UnrealEngine-5.8.3}" ;;
esac

echo "== host ($rid, self-contained)"
dotnet publish "$repo/tools/sim-host/SimHost.csproj" -c Release -r "$rid" --self-contained true \
  -p:PublishSingleFile=true -o "$here/dist/$rid" -nologo -v q

echo "== BuildCookRun ($platform) -> $out"
proj="$here/RtsBridge.uproject"
if [ "$platform" = Win64 ]; then proj="$(cygpath -w "$proj")"; outarg="$(cygpath -w "$out")"; else outarg="$out"; fi
"$UE_ROOT/Engine/Build/BatchFiles/RunUAT.$uat_ext" BuildCookRun -project="$proj" -platform="$platform" \
  -clientconfig=Development -build -cook -stage -pak -archive -archivedirectory="$outarg" \
  -noP4 -utf8output -unattended

game="$out/$staged/RtsBridge"
echo "== stage host and content into $game"
mkdir -p "$game/dist/$rid" "$game/rts"
cp "$here/dist/$rid/sim-host$exe" "$game/dist/$rid/"
rm -rf "$game/rts/content"
cp -r "$repo/content" "$game/rts/content"
echo "packaged: $out/$staged"
