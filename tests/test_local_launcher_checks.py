"""Exercise the PowerShell launchers on a POSIX host with isolated Docker/HTTP doubles.

Run: INVORA_TEST_PWSH=/path/to/pwsh python3 -m unittest discover -s tests -p 'test_local_launcher_checks.py' -v
This does not replace Windows PowerShell 5.1, ACL, shortcut or Docker Desktop acceptance.
"""
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
import json
import os
import shutil
import subprocess
import tempfile
import threading
import unittest

ROOT = Path(__file__).resolve().parents[1]
PWSH = os.environ.get("INVORA_TEST_PWSH") or shutil.which("pwsh")
SECRET = "synthetic-secret-that-must-never-appear-in-a-report"


class HealthHandler(BaseHTTPRequestHandler):
    def do_GET(self):
        self.server.requests.append(self.path)
        mode = self.server.mode
        self.send_response(302 if mode == "redirect" else 200)
        self.send_header("Content-Type", "text/plain; charset=utf-8")
        if mode == "redirect":
            self.send_header("Location", "/elsewhere")
        self.end_headers()
        self.wfile.write(b"Healthy" if mode == "healthy" else b"An unrelated application")

    def log_message(self, *args):
        pass


@unittest.skipUnless(PWSH and os.name == "posix", "Requires PowerShell and POSIX command doubles")
class LocalLauncherChecks(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="invora-launcher-test-")
        self.root = Path(self.temp.name) / "application with spaces"
        (self.root / "scripts").mkdir(parents=True)
        (self.root / "invora-web").mkdir()
        for name in ("Invora.Local.ps1", "Test-Invora.ps1", "Start-Invora.ps1"):
            shutil.copyfile(ROOT / "scripts" / name, self.root / "scripts" / name)
        for name in ("docker-compose.yml", "Dockerfile", "invora-web/Dockerfile", "Start-Invora.cmd"):
            (self.root / name).write_text("synthetic fixture\n")
        self.server = ThreadingHTTPServer(("127.0.0.1", 0), HealthHandler)
        self.server.mode = "healthy"
        self.server.requests = []
        self.thread = threading.Thread(target=self.server.serve_forever, daemon=True)
        self.thread.start()
        self.port = self.server.server_address[1]
        self.config = self.root / ".env"
        self.configure()
        self.log = Path(self.temp.name) / "docker-calls.jsonl"
        bin_path = Path(self.temp.name) / "commands"
        bin_path.mkdir()
        docker = bin_path / "docker"
        docker.write_text('''#!/usr/bin/env python3
import json, os, sys, time
args=sys.argv[1:]
with open(os.environ['INVORA_FAKE_LOG'], 'a') as log: log.write(json.dumps(args)+'\\n')
mode=os.environ.get('INVORA_FAKE_SCENARIO', 'healthy')
if args[0]=='info':
    if mode=='timeout': time.sleep(30)
    if mode=='offline': sys.exit(1)
    print('windows' if mode=='windows' else 'linux')
elif args[:2]==['volume', 'ls']: print('' if mode=='fresh' else 'invora_database\\ninvora_documents')
elif args[:2]==['compose', 'version']: print('Docker Compose test double')
elif args==['compose', 'config', '--quiet']:
    if mode=='invalid_config':
        sys.stderr.write(os.environ['INVORA_FAKE_SECRET']*4096)
        sys.exit(1)
elif args==['compose', 'ps', '--status', 'running', '--services']:
    print('db' if mode=='stopped' else 'db\\napi\\nweb')
else:
    sys.stderr.write('Unapproved Docker command in read-only test')
    sys.exit(99)
''')
        docker.chmod(0o755)
        self.env = dict(os.environ)
        for key in ("INVORA_HTTP_PORT", "INVORA_BROWSER_ORIGIN"):
            self.env.pop(key, None)
        self.env.update(PATH=str(bin_path) + os.pathsep + self.env["PATH"], INVORA_FAKE_LOG=str(self.log), INVORA_FAKE_SECRET=SECRET)

    def tearDown(self):
        self.server.shutdown()
        self.server.server_close()
        self.thread.join()
        self.temp.cleanup()

    def configure(self, port=None, origin=None, extra=""):
        self.config.write_text(f"POSTGRES_PASSWORD={SECRET}\nINVORA_BOOTSTRAP_KEY={SECRET}\nINVORA_HTTP_PORT={port or self.port}\nINVORA_BROWSER_ORIGIN={origin or f'http://127.0.0.1:{self.port}'}\n{extra}")

    def run_check(self, scenario="healthy", script="Test-Invora.ps1"):
        self.env["INVORA_FAKE_SCENARIO"] = scenario
        before = {p.relative_to(self.root): p.read_bytes() for p in self.root.rglob("*") if p.is_file()}
        args = [PWSH, "-NoLogo", "-NoProfile", "-NonInteractive", "-File", str(self.root / "scripts" / script)]
        if script == "Test-Invora.ps1":
            args.append("-SaveReport")
        result = subprocess.run(args, capture_output=True, text=True, env=self.env, timeout=30)
        self.assertNotIn(SECRET, result.stdout + result.stderr)
        for path, contents in before.items():
            self.assertEqual(contents, (self.root / path).read_bytes(), f"Modified {path}")
        added = {p.relative_to(self.root) for p in self.root.rglob("*") if p.is_file()} - before.keys()
        self.assertTrue(all(p.parent == Path("artifacts") and p.name.startswith("invora-check-") for p in added))
        self.assertEqual(self.config.exists(), Path(".env") in before, "Must not create .env during checks")
        if self.log.exists():
            calls = [json.loads(line) for line in self.log.read_text().splitlines()]
            allowed = {("info", "--format", "{{.OSType}}"), ("volume", "ls", "--filter", "label=com.docker.compose.project=invora", "--format", "{{.Name}}"), ("compose", "version"), ("compose", "config", "--quiet"), ("compose", "ps", "--status", "running", "--services")}
            self.assertTrue(all(tuple(call) in allowed for call in calls), calls)
        reports = list((self.root / "artifacts").glob("invora-check-*.json"))
        if script == "Test-Invora.ps1":
            self.assertEqual(len(reports), 1, result.stdout + result.stderr)
            raw = reports[0].read_text()
            self.assertNotIn(SECRET, raw)
            self.assertNotIn(str(self.root), raw)
            checks = {check["check"]: check["status"] for check in json.loads(raw)["checks"]}
            self.assertNotIn("Diagnostic execution", checks, result.stdout + result.stderr)
            return result.returncode, checks
        return result.returncode, result.stdout + result.stderr

    def test_workspace_opens_desktop_and_keeps_browser_fallback(self):
        desktop = self.root / "desktop" / "Invora.Desktop.exe"
        desktop.parent.mkdir()
        desktop.write_text("synthetic host")
        script = Path(self.temp.name) / "open-workspace.ps1"
        script.write_text("""param([string]$Root, [switch]$Browser)
. (Join-Path $Root 'scripts/Invora.Local.ps1')
function Start-Process { param($FilePath, $ArgumentList, $WorkingDirectory)
  [pscustomobject]@{ File=$FilePath; Arguments=@($ArgumentList); Directory=$WorkingDirectory } | ConvertTo-Json -Compress
}
Open-InvoraWorkspace -Origin 'http://127.0.0.1:8080' -InstallDirectory $Root -Browser:$Browser
""")
        def opened(browser=False):
            args=[PWSH, '-NoProfile', '-File', str(script), '-Root', str(self.root)]
            if browser: args.append('-Browser')
            result=subprocess.run(args,env=self.env,text=True,capture_output=True,check=True)
            return json.loads(result.stdout.strip().splitlines()[-1])
        result=opened()
        self.assertEqual(Path(result['File']),desktop)
        self.assertEqual(result['Arguments'],['--url','http://127.0.0.1:8080'])
        self.assertEqual(opened(True)['File'],'http://127.0.0.1:8080')
        desktop.unlink()
        self.assertEqual(opened()['File'],'http://127.0.0.1:8080')

    def test_ready_report_is_private_and_read_only(self):
        code, checks = self.run_check()
        self.assertEqual(code, 0)
        self.assertTrue(all(value == "PASS" for value in checks.values()), checks)

    def test_quoted_custom_port_and_comments(self):
        self.configure(port=f'"{self.port}" # custom port', origin=f"'http://127.0.0.1:{self.port}' # browser")
        self.assertEqual(self.run_check()[0], 0)

    def test_existing_data_without_configuration_is_blocked(self):
        self.config.unlink()
        code, checks = self.run_check()
        self.assertEqual(code, 1)
        self.assertEqual(checks["Saved data"], "FAIL")

    def test_fresh_install_check_does_not_create_configuration(self):
        self.config.unlink()
        code, checks = self.run_check("fresh")
        self.assertEqual(code, 0)
        self.assertEqual(checks["Configuration"], "WARN")

    def test_stopped_docker_has_actionable_failure(self):
        code, checks = self.run_check("offline")
        self.assertEqual(code, 1)
        self.assertEqual(checks["Docker engine"], "FAIL")

    def test_windows_container_mode_is_rejected(self):
        code, checks = self.run_check("windows")
        self.assertEqual(code, 1)
        self.assertEqual(checks["Docker engine"], "FAIL")

    def test_large_private_command_error_is_drained_and_not_reported(self):
        code, checks = self.run_check("invalid_config")
        self.assertEqual(code, 1)
        self.assertEqual(checks["Compose configuration"], "FAIL")

    def test_stopped_services_and_wrong_http_app(self):
        self.server.mode = "wrong"
        code, checks = self.run_check("stopped")
        self.assertEqual(code, 1)
        self.assertEqual(checks["Running services"], "WARN")
        self.assertEqual(checks["Application readiness"], "FAIL")

    def test_redirect_is_not_followed(self):
        self.server.mode = "redirect"
        code, checks = self.run_check()
        self.assertEqual(code, 1)
        self.assertEqual(checks["Application readiness"], "FAIL")
        self.assertEqual(self.server.requests, ["/health/ready"])

    def test_invalid_port_fails_before_any_startup_mutation(self):
        self.configure(port="not-a-number")
        code, output = self.run_check(script="Start-Invora.ps1")
        self.assertEqual(code, 1)
        self.assertIn("INVORA_HTTP_PORT must be a number", output)

    def test_mismatched_browser_origin_is_reported(self):
        self.configure(origin="http://localhost:9999")
        code, checks = self.run_check()
        self.assertEqual(code, 1)
        self.assertEqual(checks["Local address"], "FAIL")
        self.assertFalse(self.server.requests)

    def test_duplicate_port_is_reported(self):
        self.configure(extra="INVORA_HTTP_PORT=9000\n")
        code, checks = self.run_check()
        self.assertEqual(code, 1)
        self.assertEqual(checks["Local address"], "FAIL")

    def test_environment_override_matches_compose_precedence(self):
        self.env["INVORA_HTTP_PORT"] = "65536"
        code, checks = self.run_check()
        self.assertEqual(code, 1)
        self.assertEqual(checks["Local address"], "FAIL")

    def test_docker_timeout_is_bounded(self):
        code, checks = self.run_check("timeout")
        self.assertEqual(code, 1)
        self.assertEqual(checks["Docker engine"], "FAIL")


if __name__ == "__main__":
    unittest.main()
