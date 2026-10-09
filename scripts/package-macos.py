#!/usr/bin/env python3
"""Build a universal macOS launcher. Xcode Command Line Tools required only to build."""
from pathlib import Path
import hashlib
import importlib.util
import os
import plistlib
import shutil
import subprocess

ROOT = Path(__file__).resolve().parent.parent
spec = importlib.util.spec_from_file_location('release_sources', ROOT / 'scripts/package-windows.py')
source = importlib.util.module_from_spec(spec)
spec.loader.exec_module(source)
OUT = ROOT / 'artifacts'
STAGE = OUT / 'macos-stage'
APP = STAGE / 'Invora.app'
RESOURCES = APP / 'Contents/Resources'


def run(*args):
    subprocess.run(list(map(str, args)), check=True)


def main():
    OUT.mkdir(exist_ok=True)
    # Remove generated staging only; never an installed app or data directory.
    if STAGE.exists():
        shutil.rmtree(STAGE)
    (APP / 'Contents/MacOS').mkdir(parents=True)
    payload = RESOURCES / 'release'
    payload.mkdir(parents=True)
    manifest = []
    for path in sorted(ROOT.rglob('*')):
        if not path.is_file() or not source.included(path):
            continue
        name = path.relative_to(ROOT).as_posix()
        target = payload / name
        target.parent.mkdir(parents=True, exist_ok=True)
        data = path.read_bytes()
        target.write_bytes(data)
        manifest.append(f'{hashlib.sha256(data).hexdigest()}  {name}')
    (payload / 'release-sha256.txt').write_text('\n'.join(manifest) + '\n')
    for arch in ('arm64', 'x86_64'):
        run('xcrun', 'swiftc', '-O', '-swift-version', '5', '-target', f'{arch}-apple-macos14.0',
            '-module-cache-path', OUT / 'swift-module-cache',
            ROOT / 'tools/Invora.Mac/LauncherCore.swift', ROOT / 'tools/Invora.Mac/ShopWindow.swift', ROOT / 'tools/Invora.Mac/main.swift',
            '-o', OUT / f'Invora-{arch}')
    run('xcrun', 'lipo', '-create', OUT / 'Invora-arm64', OUT / 'Invora-x86_64', '-output', APP / 'Contents/MacOS/Invora')
    (APP / 'Contents/MacOS/Invora').chmod(0o755)
    run('xcrun', 'swiftc', '-module-cache-path', OUT / 'swift-module-cache', ROOT / 'tools/Invora.Mac/Icon.swift', '-o', OUT / 'invora-icon-builder')
    run(OUT / 'invora-icon-builder', OUT / 'Invora.iconset')
    run('/usr/bin/iconutil', '-c', 'icns', OUT / 'Invora.iconset', '-o', RESOURCES / 'Invora.icns')
    with (APP / 'Contents/Info.plist').open('wb') as f:
        plistlib.dump({'CFBundleExecutable': 'Invora', 'CFBundleIdentifier': 'app.invora.shop',
                      'CFBundleName': 'Invora', 'CFBundleDisplayName': 'Invora', 'CFBundlePackageType': 'APPL',
                      'CFBundleShortVersionString': '1.1.0', 'CFBundleVersion': '20261008.2',
                      'LSMinimumSystemVersion': '14.0', 'NSHighResolutionCapable': True, 'CFBundleIconFile': 'Invora.icns',
                      'NSPrincipalClass': 'NSApplication'}, f)
    guide = ROOT / 'tools/Invora.Mac/Install Invora.html'
    shutil.copyfile(guide, RESOURCES / guide.name)
    shutil.copyfile(guide, STAGE / guide.name)
    os.symlink('/Applications', STAGE / 'Applications')
    # Ad-hoc signing checks integrity locally; not a Developer ID / notarized release.
    run('/usr/bin/codesign', '--force', '--sign', '-', '--timestamp=none', APP)
    run('/usr/bin/codesign', '--verify', '--deep', '--strict', APP)
    run(APP / 'Contents/MacOS/Invora', '--verify-release')
    archive = OUT / 'Invora-Mac.zip'
    if archive.exists(): archive.unlink()
    run('/usr/bin/ditto', '-c', '-k', '--sequesterRsrc', '--keepParent', APP, archive)
    dmg = OUT / 'Invora-Mac.dmg'
    if dmg.exists(): dmg.unlink()
    run('/usr/bin/hdiutil', 'create', '-volname', 'Invora', '-srcfolder', STAGE, '-ov', '-format', 'UDZO', dmg)
    for artifact in (archive, dmg):
        digest = hashlib.sha256(artifact.read_bytes()).hexdigest()
        artifact.with_suffix(artifact.suffix + '.sha256').write_text(digest + '  ' + artifact.name + '\n')
    print(f'Built universal Mac app, DMG and ZIP with {len(manifest)} verified payload files. No configuration, backup or shop records included.')


if __name__ == '__main__':
    main()
