#!/usr/bin/env python3
"""Install the owner's daily 21:00 encrypted Backblaze backup LaunchAgent."""

import argparse
import os
from pathlib import Path
import plistlib
import shlex
import subprocess
import sys
import tempfile


LABEL = "com.invora.backup"


def atomic_private_write(path: Path, data: bytes) -> None:
    path.parent.mkdir(mode=0o700, parents=True, exist_ok=True)
    with tempfile.NamedTemporaryFile(dir=path.parent, prefix=f".{path.name}.", delete=False) as handle:
        temporary = Path(handle.name)
        os.chmod(temporary, 0o600)
        handle.write(data)
        handle.flush()
        os.fsync(handle.fileno())
    os.replace(temporary, path)
    os.chmod(path, 0o600)


def main() -> None:
    if sys.platform != "darwin":
        raise SystemExit("This installer is for macOS LaunchAgents only.")
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--public-key", type=Path, required=True)
    parser.add_argument("--signing-key", type=Path, required=True)
    parser.add_argument("--destination", type=Path, required=True)
    parser.add_argument("--remote", required=True, help="Example: invora-b2:invora/shop-01")
    args = parser.parse_args()

    repo = Path(__file__).resolve().parent.parent
    public_key = args.public_key.resolve()
    signing_key = args.signing_key.resolve()
    destination = args.destination.resolve()
    for name, path in (("recipient public key", public_key), ("signing private key", signing_key)):
        if not path.is_file() or not os.access(path, os.R_OK):
            raise SystemExit(f"The {name} is not readable: {path}")
    if ":" not in args.remote or not args.remote.split(":", 1)[1]:
        raise SystemExit("--remote must contain an rclone remote and bucket/path.")

    config_path = repo / ".private-backups" / "backup-schedule.env"
    if config_path.is_symlink():
        raise SystemExit("Refusing to replace a symlinked schedule configuration.")
    config = "\n".join(
        f"{name}={shlex.quote(str(value))}"
        for name, value in (
            ("BACKUP_PUBLIC_KEY", public_key),
            ("BACKUP_SIGNING_KEY", signing_key),
            ("BACKUP_DESTINATION", destination),
            ("BACKUP_RCLONE_REMOTE", args.remote),
        )
    ) + "\n"
    atomic_private_write(config_path, config.encode())

    agent_path = Path.home() / "Library" / "LaunchAgents" / f"{LABEL}.plist"
    wrapper = repo / "scripts" / "run-scheduled-backup.sh"
    if agent_path.is_symlink():
        raise SystemExit("Refusing to replace a symlinked LaunchAgent.")
    if agent_path.exists():
        try:
            existing = plistlib.loads(agent_path.read_bytes())
        except (ValueError, OSError) as error:
            raise SystemExit(f"Existing LaunchAgent cannot be inspected: {error}") from error
        if existing.get("Label") != LABEL or existing.get("ProgramArguments") != ["/bin/bash", str(wrapper)]:
            raise SystemExit("A different LaunchAgent uses this filename; it was not replaced.")
    agent = {
        "Label": LABEL,
        "ProgramArguments": ["/bin/bash", str(wrapper)],
        "WorkingDirectory": str(repo),
        "StartCalendarInterval": {"Hour": 21, "Minute": 0},
        "RunAtLoad": False,
        "StandardOutPath": str(destination / "launchd.out.log"),
        "StandardErrorPath": str(destination / "launchd.err.log"),
        "Umask": 0o077,
    }
    destination.mkdir(mode=0o700, parents=True, exist_ok=True)
    atomic_private_write(agent_path, plistlib.dumps(agent))

    domain = f"gui/{os.getuid()}"
    subprocess.run(["launchctl", "bootout", f"{domain}/{LABEL}"], capture_output=True, check=False)
    started = subprocess.run(["launchctl", "bootstrap", domain, str(agent_path)], capture_output=True, text=True)
    if started.returncode:
        raise SystemExit(f"LaunchAgent was written but could not be loaded: {started.stderr.strip()}")
    subprocess.run(["launchctl", "enable", f"{domain}/{LABEL}"], check=True)
    subprocess.run(["launchctl", "print", f"{domain}/{LABEL}"], capture_output=True, check=True)
    print(f"Daily 21:00 local-time Invora backup schedule installed: {agent_path}")
    print(f"Check latest success: {destination / 'last-success-utc'}")


if __name__ == "__main__":
    main()
