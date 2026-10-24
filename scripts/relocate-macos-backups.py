#!/usr/bin/env python3
"""Move the scheduled Mac backup destination outside the application source.

Copies and verifies existing files before changing the 9 PM LaunchAgent. Old
backups remain in place until the owner chooses to remove them separately.
"""

import argparse
import hashlib
import os
from pathlib import Path
import shlex
import shutil
import subprocess
import sys
import tempfile


ROOT = Path(__file__).resolve().parent.parent


def digest(path: Path) -> str:
    value = hashlib.sha256()
    with path.open("rb") as source:
        while block := source.read(1024 * 1024):
            value.update(block)
    return value.hexdigest()


def settings(path: Path) -> dict[str, str]:
    result = {}
    for line in path.read_text().splitlines():
        if not line.strip() or line.lstrip().startswith("#"):
            continue
        name, raw = line.split("=", 1)
        parts = shlex.split(raw)
        if len(parts) != 1:
            raise ValueError("Invalid backup schedule configuration.")
        result[name] = parts[0]
    return result


def main() -> None:
    if sys.platform != "darwin":
        raise SystemExit("This backup-location helper is for macOS only.")
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--destination", type=Path, default=Path.home() / "Library/Application Support/Invora Backups")
    args = parser.parse_args()
    config = ROOT / ".private-backups/backup-schedule.env"
    current = settings(config)
    for required in ("BACKUP_PUBLIC_KEY", "BACKUP_SIGNING_KEY", "BACKUP_DESTINATION", "BACKUP_RCLONE_REMOTE"):
        if not current.get(required):
            raise SystemExit(f"Schedule is missing {required}; nothing was changed.")
    if any(name in current for name in ("BACKUP_PORTABLE_KEY_FILE", "BACKUP_RECOVERY_PRIVATE_KEY", "BACKUP_RECOVERY_SIGNING_PUBLIC_KEY")):
        raise SystemExit("Portable recovery is already configured; update its schedule settings together before relocating.")
    old = Path(current["BACKUP_DESTINATION"]).resolve()
    target = args.destination.expanduser()
    if target.is_symlink() or target.resolve() == old:
        raise SystemExit("Choose a different non-linked host backup folder.")
    target.mkdir(parents=True, exist_ok=True, mode=0o700)
    os.chmod(target, 0o700)
    files = [p for p in old.iterdir() if p.is_file() and not p.is_symlink()]
    if not any(p.suffix == ".backup" for p in files):
        raise SystemExit("No completed backup archive exists at the old destination; schedule was not changed.")
    for source in files:
        dest = target / source.name
        if dest.exists():
            if dest.is_symlink() or digest(source) != digest(dest):
                raise SystemExit(f"Different file already exists at new destination: {source.name}")
            continue
        fd, temporary = tempfile.mkstemp(prefix=".invora-copy-", dir=target)
        try:
            os.fchmod(fd, 0o600)
            with os.fdopen(fd, "wb") as output, source.open("rb") as input_file:
                shutil.copyfileobj(input_file, output)
                output.flush(); os.fsync(output.fileno())
            if digest(source) != digest(Path(temporary)):
                raise IOError(f"Backup copy did not verify: {source.name}")
            os.replace(temporary, dest)
        finally:
            if os.path.exists(temporary): os.unlink(temporary)
    subprocess.run([sys.executable, str(ROOT / "scripts/install-macos-backup-schedule.py"),
                    "--public-key", current["BACKUP_PUBLIC_KEY"],
                    "--signing-key", current["BACKUP_SIGNING_KEY"],
                    "--destination", str(target),
                    "--remote", current["BACKUP_RCLONE_REMOTE"]], check=True)
    print(f"Copied and verified {len(files)} files to the Mac's application-support backup folder. The 9 PM job now uses it; old copies remain untouched.")


if __name__ == "__main__":
    main()
