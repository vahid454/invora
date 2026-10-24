#!/usr/bin/env python3
"""Add password-protected one-file recovery to an existing Mac backup schedule.

Run in the owner's Terminal. The password is prompted locally and is never sent
to the assistant, the command line, Backblaze configuration or the shop .env.
"""

import argparse
import os
from pathlib import Path
import shlex
import subprocess
import sys
import tempfile

from cryptography.hazmat.primitives import serialization


ROOT = Path(__file__).resolve().parent.parent


def values(path: Path) -> dict[str, str]:
    result = {}
    for line in path.read_text().splitlines():
        stripped = line.strip()
        if not stripped or stripped.startswith("#"):
            continue
        if "=" not in stripped:
            raise ValueError("Backup schedule configuration has an invalid line.")
        name, raw = stripped.split("=", 1)
        parts = shlex.split(raw)
        if len(parts) != 1:
            raise ValueError("Backup schedule configuration has an invalid value.")
        result[name] = parts[0]
    return result


def public_bytes(key) -> bytes:
    return key.public_key().public_bytes(serialization.Encoding.DER, serialization.PublicFormat.SubjectPublicKeyInfo)


def matching_private(public_path: Path, candidates: list[Path]) -> Path:
    public = serialization.load_pem_public_key(public_path.read_bytes())
    expected = public.public_bytes(serialization.Encoding.DER, serialization.PublicFormat.SubjectPublicKeyInfo)
    matches = []
    for path in candidates:
        if path.is_symlink() or not path.is_file():
            continue
        try:
            private = serialization.load_pem_private_key(path.read_bytes(), password=None)
            if public_bytes(private) == expected:
                matches.append(path.resolve())
        except (ValueError, TypeError):
            continue
    if len(matches) != 1:
        raise ValueError("Expected exactly one matching recipient private key. Use --private-key to select it.")
    return matches[0]


def append_settings(path: Path, settings: dict[str, Path]) -> None:
    if path.is_symlink() or path.stat().st_mode & 0o077:
        raise PermissionError("Schedule configuration must be owner-only and not a symlink.")
    original = path.read_text()
    configured = values(path)
    for name in settings:
        if name in configured:
            raise ValueError("Portable recovery is already configured; existing settings were not replaced.")
    added = "".join(f"{name}={shlex.quote(str(value))}\n" for name, value in settings.items())
    fd, temporary = tempfile.mkstemp(prefix=".backup-schedule-", dir=path.parent)
    try:
        os.fchmod(fd, 0o600)
        with os.fdopen(fd, "w") as output:
            output.write(original.rstrip("\n") + "\n" + added)
            output.flush(); os.fsync(output.fileno())
        os.replace(temporary, path)
    finally:
        if os.path.exists(temporary): os.unlink(temporary)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--schedule-config", type=Path, default=ROOT / ".private-backups/backup-schedule.env")
    parser.add_argument("--key-file", type=Path, default=ROOT / ".private-backups/portable-recovery.key")
    parser.add_argument("--private-key", type=Path, help="Matching recipient private key; auto-detected in this vendor workspace when omitted")
    parser.add_argument("--signing-public-key", type=Path, help="Trusted backup signing public key")
    parser.add_argument("--check", action="store_true", help="Validate schedule and key pairing without asking for a password or changing anything")
    args = parser.parse_args()
    try:
        config = values(args.schedule_config)
        if args.schedule_config.is_symlink() or args.schedule_config.stat().st_mode & 0o077:
            raise PermissionError("Schedule configuration must be owner-only and not a symlink.")
        for required in ("BACKUP_PUBLIC_KEY", "BACKUP_SIGNING_KEY", "BACKUP_DESTINATION", "BACKUP_RCLONE_REMOTE"):
            if not config.get(required): raise ValueError(f"Backup schedule is missing {required}.")
        recipient_public = Path(config["BACKUP_PUBLIC_KEY"])
        private = args.private_key.resolve() if args.private_key else matching_private(
            recipient_public, list((ROOT / ".vendor-private").rglob("recipient-private.pem")))
        if args.private_key:
            private = matching_private(recipient_public, [private])
        signing_public = (args.signing_public_key or recipient_public.parent / "signing-public.pem").resolve()
        signing_private = serialization.load_pem_private_key(Path(config["BACKUP_SIGNING_KEY"]).read_bytes(), password=None)
        trusted_signing = serialization.load_pem_public_key(signing_public.read_bytes())
        if public_bytes(signing_private) != trusted_signing.public_bytes(serialization.Encoding.DER, serialization.PublicFormat.SubjectPublicKeyInfo):
            raise ValueError("Trusted signing public key does not match the scheduled backup signing key.")
        if args.check:
            print("Backup schedule and both recovery key pairs match. No configuration was changed.")
            return
        if any(name in config for name in ("BACKUP_PORTABLE_KEY_FILE", "BACKUP_RECOVERY_PRIVATE_KEY", "BACKUP_RECOVERY_SIGNING_PUBLIC_KEY")):
            raise ValueError("Portable recovery is already configured; existing settings were not replaced.")
        if args.key_file.exists():
            raise FileExistsError("A portable recovery key already exists; refusing to replace it.")
        print("The existing 9 PM local-and-Backblaze backup is ready. Set a recovery password now and save it away from this Mac.")
        subprocess.run([sys.executable, str(ROOT / "scripts/portable-recovery.py"), "setup", "--key-file", str(args.key_file)], check=True)
        append_settings(args.schedule_config, {
            "BACKUP_PORTABLE_KEY_FILE": args.key_file.resolve(),
            "BACKUP_RECOVERY_PRIVATE_KEY": private,
            "BACKUP_RECOVERY_SIGNING_PUBLIC_KEY": signing_public,
        })
        print("Portable recovery is enabled for the 9 PM job. Run one backup now and verify its .invora file before relying on it.")
    except (OSError, ValueError, PermissionError, subprocess.CalledProcessError) as error:
        raise SystemExit(f"Portable recovery setup stopped safely: {error}") from error


if __name__ == "__main__":
    main()
