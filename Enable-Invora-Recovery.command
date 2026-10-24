#!/bin/zsh
cd "$(dirname "$0")"
echo "Invora one-file recovery setup"
echo "Use at least 16 characters. Your password stays in this Terminal; save it away from this Mac."
if python3 scripts/configure-portable-recovery.py; then
  echo ""
  echo "Setup finished. Keep your password away from this computer."
else
  echo ""
  echo "Setup needs attention. Nothing was removed from your existing backups."
fi
read -k 1 '?Press any key to close this window…'
