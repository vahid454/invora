#!/usr/bin/env python3
"""Configure one private Backblaze B2 rclone remote without command-line secrets."""

import argparse
import base64
import getpass
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import tempfile
from urllib.error import HTTPError, URLError
from urllib.request import Request, urlopen


def fail(message: str) -> None:
    print(message, file=sys.stderr)
    raise SystemExit(1)


def run_rclone(*args: str, input_text: str | None = None) -> subprocess.CompletedProcess[str]:
    return subprocess.run(
        ["rclone", *args],
        input=input_text,
        text=True,
        capture_output=True,
        check=False,
    )


def connection_hint(error_output: str) -> str:
    """Return a useful category without echoing any remote response or secret."""
    error = error_output.lower()
    if any(word in error for word in ("401", "403", "forbidden", "not allowed", "capability", "permission", "unauthorized")):
        return "Authentication succeeded, but rclone could not list this bucket. Check its Read and Write access, bucket restriction, and file-name prefix."
    if any(word in error for word in ("404", "bucket not found", "no such bucket", "unknown bucket")):
        return "The bucket name is not available to this key. Check the bucket spelling and the key's bucket restriction."
    if any(word in error for word in ("no such host", "network is unreachable", "connection refused", "timeout")):
        return "The Mac could not reach Backblaze. Check its internet connection and retry."
    return "Check that the new key has Read and Write access to this bucket and that both key values were copied completely."


def authorization_hint(account: str, secret: str, bucket: str) -> str | None:
    """Verify the key pair with B2 v4 before testing rclone's bucket listing.

    Only return fixed diagnostic text; never print the HTTP response or credentials.
    """
    basic = base64.b64encode(f"{account}:{secret}".encode()).decode()
    request = Request(
        "https://api.backblazeb2.com/b2api/v4/b2_authorize_account",
        headers={"Authorization": f"Basic {basic}"},
    )
    try:
        with urlopen(request, timeout=20) as response:
            authorization = json.load(response)
    except HTTPError as error:
        try:
            error_code = json.load(error).get("code")
        except (ValueError, AttributeError):
            error_code = None
        if error.code == 401 and error_code == "unsupported":
            return "Backblaze recognizes this keyID but does not support it with this API version. Check the key's type in Backblaze."
        if error.code == 401:
            return "Backblaze rejected this keyID/applicationKey pair. Reopen the newly created key's saved details and copy both values from that same key. The bucket ID, account ID, and key name are different values."
        if error.code == 403:
            return "Backblaze refused the authorization request. Check account status and the key's expiration in Backblaze."
        return f"Backblaze returned HTTP {error.code} while checking the key pair; retry later or check the Backblaze account."
    except (URLError, TimeoutError):
        return "The Mac could not reach the Backblaze authorization service. Check its internet connection and retry."
    except (ValueError, KeyError, TypeError):
        return "Backblaze returned an unexpected authorization response. Retry later."

    storage = authorization.get("apiInfo", {}).get("storageApi", {})
    allowed = storage.get("allowed", {})
    buckets = allowed.get("buckets")
    if buckets and not any(entry.get("name") == bucket for entry in buckets):
        return f"This key is restricted to another bucket. Select the {bucket!r} bucket when creating a new key."
    capabilities = allowed.get("capabilities", [])
    if capabilities and not {"listFiles", "readFiles", "writeFiles"}.issubset(capabilities):
        return "The key is missing file access. Create a bucket-restricted key with Read and Write access."
    return None


def config_section(remote: str, account: str, secret: str) -> str:
    """B2 expects a verbatim applicationKey, not rclone's obscured password format."""
    return f"[{remote}]\ntype = b2\naccount = {account}\nkey = {secret}\n"


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--bucket", required=True, help="Existing private B2 bucket name")
    parser.add_argument("--remote", default="invora-b2", help="Local rclone remote name")
    args = parser.parse_args()
    if not sys.stdin.isatty():
        fail("Run this in your own interactive Terminal so the application key stays private.")
    if not re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9.-]{4,61}[A-Za-z0-9]", args.bucket):
        fail("Enter the existing Backblaze bucket name (6–63 letters, numbers, dots or hyphens).")
    if not re.fullmatch(r"[A-Za-z0-9_-]+", args.remote):
        fail("Remote name must contain only letters, numbers, underscores or hyphens.")

    location = run_rclone("config", "file")
    if location.returncode:
        fail("Could not find rclone's local configuration location.")
    config_path = Path(location.stdout.strip().splitlines()[-1]).expanduser()
    if not config_path.is_absolute():
        fail("rclone returned an unexpected configuration location.")
    if config_path.is_symlink():
        fail("Refusing to replace a symlinked rclone configuration file.")
    if config_path.exists() and config_path.stat().st_uid != os.getuid():
        fail("The rclone configuration is not owned by this Mac user.")
    previous = config_path.read_text() if config_path.exists() else ""
    if re.search(rf"(?m)^\[{re.escape(args.remote)}\]\s*$", previous):
        fail(f"Remote {args.remote!r} already exists. Use 'rclone config' to edit it.")

    print(f"Connecting to private Backblaze bucket {args.bucket!r} as {args.remote!r}.")
    print("Enter the two values shown when you created the bucket-restricted application key.")
    account = input("Backblaze keyID: ").strip()
    secret = getpass.getpass("Backblaze applicationKey (hidden): ").strip()
    if not re.fullmatch(r"[A-Za-z0-9]+", account) or not secret or any(char in secret for char in "\r\n"):
        fail("Enter a valid keyID and the applicationKey shown by Backblaze.")

    print("Checking the key pair directly with Backblaze; this does not upload business data...")
    hint = authorization_hint(account, secret, args.bucket)
    if hint:
        fail(f"Connection test failed: {hint} No remote was saved and no data was uploaded.")

    # The B2 backend expects the applicationKey verbatim. `rclone obscure` is
    # for options explicitly marked as obscured passwords; using it for B2
    # produces a valid-looking config that Backblaze rejects.
    section = config_section(args.remote, account, secret)
    secret = ""
    config_path.parent.mkdir(mode=0o700, parents=True, exist_ok=True)
    temporary_path: Path | None = None
    try:
        with tempfile.NamedTemporaryFile(
            mode="w", encoding="utf-8", prefix=".rclone-test-", dir=config_path.parent, delete=False
        ) as temporary:
            temporary_path = Path(temporary.name)
            os.chmod(temporary_path, 0o600)
            temporary.write(previous)
            if previous and not previous.endswith("\n"):
                temporary.write("\n")
            if previous:
                temporary.write("\n")
            temporary.write(section)
            temporary.flush()
            os.fsync(temporary.fileno())

        print("Key pair accepted. Testing bucket access; this does not upload business data...")
        tested = run_rclone("--config", str(temporary_path), "lsf", f"{args.remote}:{args.bucket}")
        if tested.returncode:
            fail(f"Connection test failed: {connection_hint(tested.stderr)} No remote was saved and no data was uploaded.")
        os.replace(temporary_path, config_path)
        temporary_path = None
        os.chmod(config_path, 0o600)
        print(f"Connected. Remote {args.remote!r} is saved with owner-only file permissions.")
        print("Tell your Invora assistant 'remote configured'; do not share the key.")
    finally:
        if temporary_path is not None:
            temporary_path.unlink(missing_ok=True)


if __name__ == "__main__":
    main()
