#!/bin/bash
# Build the managed helper (runtime/helper/) against a game's Managed DLLs into build/helper/.
# usage: build-helper.sh <game Data/Managed dir>
set -euo pipefail
R=$(cd "$(dirname "$0")/../.." && pwd)
GAME_MANAGED="$1" dotnet build -nologo -v q -c Release "$R/runtime/helper/BazaarPlusPlus.Ios.Helper.csproj" -o "$R/build/helper"
