#!/usr/bin/env python3
"""Create or open one password-protected Invora recovery file.

Requires the Python cryptography package. The recovery password is entered locally;
the scheduled job keeps only a derived key in an owner-readable host file.
"""

import argparse
import base64
from datetime import datetime, timezone
import getpass
import hashlib
import io
import json
import os
from pathlib import Path
import secrets
import shutil
import struct
import sys
import tempfile
import zipfile

from cryptography.hazmat.primitives.ciphers.aead import AESGCM
from cryptography.exceptions import InvalidTag
from cryptography.hazmat.primitives import serialization


os.umask(0o077)


MAGIC = b"INVORA-RECOVERY-1\n"
CHUNK_SIZE = 4 * 1024 * 1024
ITERATIONS = 600_000
NAMES = ("database.backup", "shop.env", "recipient-private.pem", "signing-public.pem")


def encoded(value: bytes) -> str:
    return base64.b64encode(value).decode("ascii")


def decoded(value: str) -> bytes:
    return base64.b64decode(value, validate=True)


def derive(password: str, salt: bytes, iterations: int = ITERATIONS) -> bytes:
    if len(password) < 16:
        raise ValueError("Recovery password must contain at least 16 characters.")
    return hashlib.pbkdf2_hmac("sha256", password.encode("utf-8"), salt, iterations, dklen=32)


def private_write(path: Path, data: bytes) -> None:
    path.parent.mkdir(parents=True, exist_ok=True, mode=0o700)
    if path.is_symlink() or path.exists():
        raise FileExistsError(f"Refusing to replace an existing recovery key file: {path}")
    fd = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
    with os.fdopen(fd, "wb") as output:
        output.write(data)
        output.flush()
        os.fsync(output.fileno())


def setup(args: argparse.Namespace) -> None:
    password = getpass.getpass("Choose a recovery password of at least 16 characters (save it away from this computer): ")
    repeated = getpass.getpass("Enter it again: ")
    if password != repeated:
        raise ValueError("Recovery passwords did not match.")
    salt = secrets.token_bytes(16)
    key = derive(password, salt)
    private_write(args.key_file, json.dumps({"version": 1, "salt": encoded(salt), "key": encoded(key), "iterations": ITERATIONS}, sort_keys=True).encode() + b"\n")
    print("Recovery key configured. Keep your password in protected storage away from this computer; it cannot be recovered from Backblaze.")


def load_key(path: Path) -> tuple[bytes, bytes, int]:
    if path.is_symlink() or path.stat().st_mode & 0o077:
        raise PermissionError("Recovery key file must be owner-only and not a symlink.")
    data = json.loads(path.read_text())
    if data.get("version") != 1 or data.get("iterations") != ITERATIONS:
        raise ValueError("Unsupported recovery key settings.")
    salt, key = decoded(data["salt"]), decoded(data["key"])
    if len(salt) != 16 or len(key) != 32:
        raise ValueError("Invalid recovery key file.")
    return salt, key, ITERATIONS


class EncryptedWriter:
    def __init__(self, output: io.BufferedWriter, key: bytes, aad: bytes, prefix: bytes):
        self.output, self.cipher, self.aad, self.prefix = output, AESGCM(key), aad, prefix
        self.buffer = bytearray()
        self.counter = 0
        self.position = 0

    def writable(self) -> bool:
        return True

    def seekable(self) -> bool:
        return False

    def tell(self) -> int:
        return self.position

    def write(self, data: bytes) -> int:
        self.buffer.extend(data)
        self.position += len(data)
        while len(self.buffer) >= CHUNK_SIZE:
            self._chunk(bytes(self.buffer[:CHUNK_SIZE]))
            del self.buffer[:CHUNK_SIZE]
        return len(data)

    def flush(self) -> None:
        pass

    def _nonce(self) -> bytes:
        return self.prefix + self.counter.to_bytes(8, "big")

    def _chunk(self, plain: bytes) -> None:
        if self.counter >= 2**64 - 1:
            raise ValueError("Recovery archive is too large.")
        ciphertext = self.cipher.encrypt(self._nonce(), plain, self.aad + b"D" + self.counter.to_bytes(8, "big"))
        self.output.write(struct.pack(">I", len(ciphertext)))
        self.output.write(ciphertext)
        self.counter += 1

    def finish(self) -> None:
        if self.buffer:
            self._chunk(bytes(self.buffer))
            self.buffer.clear()
        self.output.write(struct.pack(">I", 0))
        self.output.write(self.cipher.encrypt(self._nonce(), b"", self.aad + b"F" + self.counter.to_bytes(8, "big") + self.position.to_bytes(8, "big")))


def add_file(archive: zipfile.ZipFile, name: str, path: Path) -> dict[str, object]:
    digest = hashlib.sha256()
    size = 0
    with path.open("rb") as source, archive.open(name, "w", force_zip64=True) as target:
        while block := source.read(1024 * 1024):
            digest.update(block)
            target.write(block)
            size += len(block)
    return {"sha256": digest.hexdigest(), "bytes": size}


def create(args: argparse.Namespace) -> None:
    paths = dict(zip(NAMES, (args.backup, args.env, args.private_key, args.signing_public_key)))
    for name, path in paths.items():
        if not path.is_file() or path.is_symlink():
            raise ValueError(f"Missing or linked recovery input: {name}")
    if getattr(args, "recipient_public_key", None) and getattr(args, "signing_private_key", None):
        recipient = serialization.load_pem_public_key(args.recipient_public_key.read_bytes())
        private = serialization.load_pem_private_key(args.private_key.read_bytes(), password=None)
        signing = serialization.load_pem_private_key(args.signing_private_key.read_bytes(), password=None)
        trusted = serialization.load_pem_public_key(args.signing_public_key.read_bytes())
        encode = lambda key: key.public_bytes(serialization.Encoding.DER, serialization.PublicFormat.SubjectPublicKeyInfo)
        if encode(private.public_key()) != encode(recipient) or encode(signing.public_key()) != encode(trusted):
            raise ValueError("Recovery keys no longer match the active backup keys; refusing to create a misleading portable file.")
    salt, key, iterations = load_key(args.key_file)
    header = json.dumps({"version": 1, "salt": encoded(salt), "iterations": iterations,
                         "nonce_prefix": encoded(secrets.token_bytes(4)),
                         "created_utc": datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")}, sort_keys=True, separators=(",", ":")).encode()
    aad = MAGIC + struct.pack(">I", len(header)) + header
    if args.output.exists() or args.output.is_symlink():
        raise FileExistsError("Refusing to overwrite an existing recovery file.")
    args.output.parent.mkdir(parents=True, exist_ok=True, mode=0o700)
    fd, temporary = tempfile.mkstemp(prefix=".invora-recovery-", dir=args.output.parent)
    try:
        with os.fdopen(fd, "wb") as output:
            output.write(aad)
            writer = EncryptedWriter(output, key, aad, decoded(json.loads(header)["nonce_prefix"]))
            with zipfile.ZipFile(writer, "w", compression=zipfile.ZIP_STORED, allowZip64=True) as archive:
                manifest = {name: add_file(archive, name, path) for name, path in paths.items()}
                archive.writestr("manifest.json", json.dumps({"version": 1, "files": manifest}, sort_keys=True))
            writer.finish()
            output.flush()
            os.fsync(output.fileno())
        os.replace(temporary, args.output)
    finally:
        if os.path.exists(temporary):
            os.unlink(temporary)
    print(f"Password-protected one-file recovery package created: {args.output.name}")


def decrypt_package(source: Path, credential: str | bytes, destination: Path) -> bytes:
    with source.open("rb") as input_file, destination.open("wb") as output:
        marker = input_file.read(len(MAGIC))
        if marker != MAGIC:
            raise ValueError("This is not an Invora recovery file.")
        header_size_bytes = input_file.read(4)
        if len(header_size_bytes) != 4:
            raise ValueError("Incomplete recovery header.")
        header_size = struct.unpack(">I", header_size_bytes)[0]
        if header_size > 4096:
            raise ValueError("Invalid recovery header length.")
        header_bytes = input_file.read(header_size)
        if len(header_bytes) != header_size:
            raise ValueError("Incomplete recovery header.")
        header = json.loads(header_bytes)
        if header.get("version") != 1 or header.get("iterations") != ITERATIONS:
            raise ValueError("Unsupported recovery file version.")
        salt, prefix = decoded(header["salt"]), decoded(header["nonce_prefix"])
        if len(salt) != 16 or len(prefix) != 4:
            raise ValueError("Invalid recovery header.")
        cipher = AESGCM(credential if isinstance(credential, bytes) else derive(credential, salt))
        aad = marker + header_size_bytes + header_bytes
        counter = total = 0
        while True:
            length_bytes = input_file.read(4)
            if len(length_bytes) != 4:
                raise ValueError("Truncated recovery file.")
            length = struct.unpack(">I", length_bytes)[0]
            nonce = prefix + counter.to_bytes(8, "big")
            if length == 0:
                tag = input_file.read(16)
                if len(tag) != 16 or input_file.read(1):
                    raise ValueError("Truncated or trailing recovery data.")
                cipher.decrypt(nonce, tag, aad + b"F" + counter.to_bytes(8, "big") + total.to_bytes(8, "big"))
                break
            if length < 17 or length > CHUNK_SIZE + 16:
                raise ValueError("Invalid recovery chunk size.")
            block = input_file.read(length)
            if len(block) != length:
                raise ValueError("Truncated recovery chunk.")
            plain = cipher.decrypt(nonce, block, aad + b"D" + counter.to_bytes(8, "big"))
            output.write(plain)
            total += len(plain)
            counter += 1
        output.flush()
        os.fsync(output.fileno())
        return header_bytes


def open_package(args: argparse.Namespace) -> None:
    if not args.package.is_file() or args.package.is_symlink():
        raise ValueError("Choose a regular recovery file.")
    credential = load_key(args.key_file)[1] if args.key_file else getpass.getpass("Recovery password: ")
    with tempfile.TemporaryDirectory(prefix="invora-recovery-") as folder:
        plain_zip = Path(folder) / "authenticated.zip"
        os.chmod(folder, 0o700)
        header_bytes = decrypt_package(args.package, credential, plain_zip)
        with zipfile.ZipFile(plain_zip) as archive:
            if set(archive.namelist()) != set(NAMES) | {"manifest.json"} or archive.testzip() is not None:
                raise ValueError("Recovery package contents are invalid.")
            manifest = json.loads(archive.read("manifest.json"))
            if manifest.get("version") != 1 or set(manifest.get("files", {})) != set(NAMES):
                raise ValueError("Recovery manifest is invalid.")
            if args.action == "verify":
                for name in NAMES:
                    with archive.open(name) as stream:
                        digest = hashlib.sha256()
                        size = 0
                        while block := stream.read(1024 * 1024):
                            digest.update(block); size += len(block)
                    if manifest["files"][name] != {"sha256": digest.hexdigest(), "bytes": size}:
                        raise ValueError(f"Recovery checksum failed: {name}")
                print("Verified password, authentication and all four file checksums. This package includes database, uploads archive, matching .env and recovery keys.")
                return
            if args.output.exists() or args.output.is_symlink():
                raise FileExistsError("Choose a new empty recovery folder; existing files will not be overwritten.")
            temporary = args.output.with_name(args.output.name + ".partial-" + secrets.token_hex(4))
            temporary.mkdir(mode=0o700, parents=False)
            try:
                for name in NAMES:
                    target = temporary / name
                    digest = hashlib.sha256(); size = 0
                    with archive.open(name) as source:
                        fd = os.open(target, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
                        with os.fdopen(fd, "wb") as output:
                            while block := source.read(1024 * 1024):
                                output.write(block); digest.update(block); size += len(block)
                    if manifest["files"][name] != {"sha256": digest.hexdigest(), "bytes": size}:
                        raise ValueError(f"Recovery checksum failed: {name}")
                os.replace(temporary, args.output)
            finally:
                if temporary.exists():
                    shutil.rmtree(temporary)
    print(f"Verified recovery files extracted to {args.output}. Do not replace a live database without an isolated restore and reconciliation.")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest="action", required=True)
    setup_parser = sub.add_parser("setup", help="Set a recovery password locally for scheduled backups")
    setup_parser.add_argument("--key-file", type=Path, required=True)
    create_parser = sub.add_parser("create", help="Create a password-protected single recovery file")
    for name in ("backup", "env", "private-key", "signing-public-key", "key-file", "output"):
        create_parser.add_argument("--" + name, type=Path, required=True)
    create_parser.add_argument("--recipient-public-key", type=Path)
    create_parser.add_argument("--signing-private-key", type=Path)
    for action in ("verify", "extract"):
        command = sub.add_parser(action)
        command.add_argument("package", type=Path)
        command.add_argument("--key-file", type=Path, help="Use this computer's owner-only derived key instead of prompting")
        if action == "extract":
            command.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    try:
        if args.action == "setup": setup(args)
        elif args.action == "create": create(args)
        else: open_package(args)
    except InvalidTag as error:
        raise SystemExit("Recovery operation stopped safely: wrong password or damaged recovery file.") from error
    except (OSError, ValueError, json.JSONDecodeError, zipfile.BadZipFile) as error:
        raise SystemExit(f"Recovery operation stopped safely: {error}") from error


if __name__ == "__main__":
    main()
