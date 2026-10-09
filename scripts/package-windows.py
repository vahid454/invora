#!/usr/bin/env python3
"""Package reviewed application sources, never configuration secrets or shop records."""
from pathlib import Path
import argparse
import hashlib
import zipfile

ROOT = Path(__file__).resolve().parent.parent
OUTPUT = ROOT / "artifacts" / "Invora-Windows-local.zip"
ROOT_FILES = {
    "README.md", "spec.md", "Invora.sln", "global.json", "dotnet-tools.json",
    "Directory.Build.props", "Directory.Packages.props", "Dockerfile",
    ".dockerignore", ".env.example", "docker-compose.yml",
    "docker-compose.production.yml", "docker-compose.dev.yml",
    "Start-Invora.cmd", "Stop-Invora.cmd", "Check-Invora.cmd", "Open-InvoraBrowser.cmd",
}
TREES = {"src", "tests", "invora-web", "docs", "scripts", "deployment"}
EXCLUDED = {
    "bin", "obj", "node_modules", "dist", ".angular", "artifacts",
    "private-files", "test-results", "playwright-report", "__pycache__",
    ".git", ".aws", ".agents", ".codex", ".vendor-private", ".private-backups",
}


def included(path: Path) -> bool:
    relative = path.relative_to(ROOT)
    if path.is_symlink() or any(part in EXCLUDED for part in relative.parts):
        return False
    if path.name.startswith(".env") and relative.as_posix() != ".env.example":
        return False
    if path.suffix.lower() in {".pem", ".key", ".pfx", ".dump", ".backup", ".pyc", ".license"} or path.name.endswith(".sql.gz"):
        return False
    return relative.as_posix() in ROOT_FILES or relative.parts[0] in TREES


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source-only', action='store_true', help='Omit the Windows desktop host; use browser fallback.')
    args = parser.parse_args()
    desktop = ROOT / 'artifacts/windows-desktop/Invora.Desktop.exe'
    if not args.source_only and not desktop.is_file():
        parser.error('Publish tools/Invora.Desktop to artifacts/windows-desktop first, or use --source-only for a browser-only source ZIP.')
    files = sorted(path for path in ROOT.rglob("*") if path.is_file() and included(path))
    OUTPUT.parent.mkdir(exist_ok=True)
    manifest = []
    with zipfile.ZipFile(OUTPUT, "w", zipfile.ZIP_DEFLATED) as archive:
        for path in files:
            name = path.relative_to(ROOT).as_posix()
            contents = path.read_bytes()
            archive.writestr(name, contents)
            manifest.append(f"{hashlib.sha256(contents).hexdigest()}  {name}")
        if not args.source_only:
            contents = desktop.read_bytes()
            name = 'desktop/Invora.Desktop.exe'
            archive.writestr(name, contents)
            manifest.append(f"{hashlib.sha256(contents).hexdigest()}  {name}")
        archive.writestr("release-sha256.txt", "\n".join(manifest) + "\n")
    with zipfile.ZipFile(OUTPUT) as archive:
        assert archive.testzip() is None
        assert ".env" not in archive.namelist()
        assert all(not any(part in EXCLUDED for part in Path(name).parts) for name in archive.namelist())
        assert all(name in archive.namelist() for name in ROOT_FILES)
    print(f"Packaged {len(files)} source/configuration/example files: {OUTPUT.name} ({OUTPUT.stat().st_size:,} bytes)")


if __name__ == "__main__":
    main()
