#!/bin/bash
# Checks every [HarmonyPatch] target of the framework (and optional dependent-mod DLLs) against a RimWorld version's Assembly-CSharp.
# Usage: tools/api_scan.sh [1.6|1.7] [more patch dlls...]   (build first). Exit 1 if a target is missing. Idea and code from Hearth & Harbor.
# 1.6 is compile-only on this machine: this scan is its only proof that the patch targets exist (Krafs.Rimworld.Ref 1.6.4871).
export PATH="$HOME/.dotnet:$PATH" DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
cd "$(dirname "$0")/.."
v=${1:-1.6}; shift
dotnet build tools/ApiScan/ApiScan.csproj -c Release -o tools/ApiScan/bin -v q -nologo 2>&1 | grep -E " error " | head
case $v in
  1.7) ref="$HOME/Library/Application Support/Steam/steamapps/common/RimWorld/RimWorld.app/Contents/MacOS/Assembly-CSharp.dll";;
  1.6) ref=$(ls ~/.nuget/packages/krafs.rimworld.ref/1.6.*/ref/net472/Assembly-CSharp.dll | tail -1);;
  *) echo "usage: tools/api_scan.sh [1.6|1.7] [dlls...]"; exit 2;;
esac
export JB_SEARCH="$HOME/Library/Application Support/Steam/steamapps/workshop/content/294100/2009463077/$v/Assemblies:$(dirname "$ref")"
echo "== RimWorld $v ($(basename "$(dirname "$(dirname "$(dirname "$ref")")")"))"
dotnet tools/ApiScan/bin/ApiScan.dll "$v/Assemblies/JBFramework.dll" "$ref" "$@"
