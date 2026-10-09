#!/usr/bin/env bash
set -euo pipefail
invora_root="$(cd "$(dirname "$0")/.." && pwd)"
mkdir -p "$invora_root/artifacts"
xcrun swiftc -swift-version 5 -module-cache-path "$invora_root/artifacts/swift-module-cache" \
  "$invora_root/tools/Invora.Mac/LauncherCore.swift" "$invora_root/tests/Invora.Mac/main.swift" \
  -o "$invora_root/artifacts/invora-mac-tests"
"$invora_root/artifacts/invora-mac-tests"
