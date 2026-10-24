"""Portable recovery must round-trip and reject tampering or overwrite."""

from argparse import Namespace
import importlib.util
from pathlib import Path
import secrets
import tempfile
import unittest
from unittest.mock import patch
from cryptography.hazmat.primitives.asymmetric import rsa
from cryptography.hazmat.primitives import serialization


SPEC = importlib.util.spec_from_file_location("portable_recovery", Path(__file__).resolve().parents[1] / "scripts/portable-recovery.py")
recovery = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(recovery)


class PortableRecoveryTests(unittest.TestCase):
    def test_rejects_mismatched_active_backup_keys(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            with patch.object(recovery.getpass, "getpass", side_effect=["strong owner recovery password 2026"] * 2):
                recovery.setup(Namespace(key_file=root / "key"))
            recipient = rsa.generate_private_key(public_exponent=65537, key_size=2048)
            wrong = rsa.generate_private_key(public_exponent=65537, key_size=2048)
            signing = rsa.generate_private_key(public_exponent=65537, key_size=2048)
            private = root / "recipient-private.pem"
            public = root / "recipient-public.pem"
            signing_private = root / "signing-private.pem"
            signing_public = root / "signing-public.pem"
            private.write_bytes(wrong.private_bytes(serialization.Encoding.PEM, serialization.PrivateFormat.PKCS8, serialization.NoEncryption()))
            public.write_bytes(recipient.public_key().public_bytes(serialization.Encoding.PEM, serialization.PublicFormat.SubjectPublicKeyInfo))
            signing_private.write_bytes(signing.private_bytes(serialization.Encoding.PEM, serialization.PrivateFormat.PKCS8, serialization.NoEncryption()))
            signing_public.write_bytes(signing.public_key().public_bytes(serialization.Encoding.PEM, serialization.PublicFormat.SubjectPublicKeyInfo))
            backup = root / "backup"; backup.write_bytes(b"fixture")
            env = root / "env"; env.write_bytes(b"fixture")
            with self.assertRaisesRegex(ValueError, "no longer match"):
                recovery.create(Namespace(backup=backup, env=env, private_key=private,
                    signing_public_key=signing_public, key_file=root / "key", output=root / "out.invora",
                    recipient_public_key=public, signing_private_key=signing_private))
            self.assertFalse((root / "out.invora").exists())

    def test_large_round_trip_and_tamper_rejection(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            key_file = root / "derived.key"
            password = "strong owner recovery password 2026"
            with patch.object(recovery.getpass, "getpass", side_effect=[password, password]):
                recovery.setup(Namespace(key_file=key_file))
            self.assertEqual(key_file.stat().st_mode & 0o077, 0)
            payloads = [secrets.token_bytes(recovery.CHUNK_SIZE + 17), b"PRIVATE_CONFIG=fixture", b"private fixture", b"trusted public fixture"]
            paths = []
            for name, data in zip(("backup", "env", "private", "signing-public"), payloads):
                path = root / name
                path.write_bytes(data)
                paths.append(path)
            package = root / "daily.invora"
            arguments = Namespace(backup=paths[0], env=paths[1], private_key=paths[2], signing_public_key=paths[3], key_file=key_file, output=package)
            recovery.create(arguments)
            recovery.open_package(Namespace(action="verify", package=package, key_file=key_file))
            with patch.object(recovery.getpass, "getpass", return_value=password):
                recovery.open_package(Namespace(action="extract", package=package, key_file=None, output=root / "restored"))
            self.assertEqual((root / "restored/database.backup").read_bytes(), payloads[0])
            self.assertEqual((root / "restored/shop.env").read_bytes(), payloads[1])
            self.assertEqual((root / "restored/shop.env").stat().st_mode & 0o077, 0)
            with self.assertRaises(FileExistsError):
                recovery.create(arguments)
            tampered = root / "damaged.invora"
            bytes_ = bytearray(package.read_bytes())
            bytes_[len(bytes_) // 2] ^= 1
            tampered.write_bytes(bytes_)
            with self.assertRaises(Exception):
                recovery.open_package(Namespace(action="verify", package=tampered, key_file=key_file))
            truncated = root / "short.invora"
            truncated.write_bytes(package.read_bytes()[:-25])
            with self.assertRaises(Exception):
                recovery.open_package(Namespace(action="verify", package=truncated, key_file=key_file))


if __name__ == "__main__":
    unittest.main()
