#!/usr/bin/env python3
"""Check that the last local encrypted offsite backup run is recent and successful.

This reads status markers only. It does not access the database, secrets or cloud.
"""

import argparse
from datetime import datetime, timedelta, timezone
from pathlib import Path
import sys


def timestamp(path: Path) -> datetime | None:
    if not path.is_file():
        return None
    try:
        return datetime.strptime(path.read_text(encoding="utf-8").strip(), "%Y-%m-%dT%H:%M:%SZ").replace(tzinfo=timezone.utc)
    except (OSError, UnicodeError, ValueError):
        return None


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--destination", type=Path, default=Path(__file__).resolve().parent.parent / ".private-backups/backblaze-invora")
    parser.add_argument("--max-age-hours", type=float, default=30)
    args = parser.parse_args()
    if args.max_age_hours <= 0:
        parser.error("--max-age-hours must be positive")

    success = timestamp(args.destination / "last-success-utc")
    failure = timestamp(args.destination / "last-failure-utc")
    if success is None:
        print("FAIL: No valid successful backup marker. Run the backup and verify its cloud copy.")
        return 1
    age = datetime.now(timezone.utc) - success
    if failure is not None and failure > success:
        print(f"FAIL: A backup failed after the last successful run ({success:%Y-%m-%d %H:%M} UTC). Check the log and retry.")
        return 1
    if age < timedelta(0) or age > timedelta(hours=args.max_age_hours):
        print(f"FAIL: Last successful backup is {success:%Y-%m-%d %H:%M} UTC; verify the schedule and retry.")
        return 1
    print(f"PASS: Last backup run succeeded {success:%Y-%m-%d %H:%M} UTC ({age.total_seconds() / 3600:.1f} hours ago).")
    print("This marker does not prove that cloud data or recovery keys are accessible; periodically verify and restore an archive.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
