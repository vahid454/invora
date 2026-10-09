#!/usr/bin/env python3
"""Native WebKit acceptance with synthetic downloads; optional live login preview is read-only."""
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
import os
import subprocess
import tempfile
import threading

ROOT=Path(__file__).resolve().parent.parent
class Fixture(BaseHTTPRequestHandler):
    def do_GET(self):
        self.send_response(200);self.send_header('Content-Type','text/html');self.end_headers()
        self.wfile.write(b'''<!doctype html><title>Invora desktop fixture</title><h1>Desktop fixture</h1><input aria-label="Customer search"><script>
function downloadFixture(name,type,body){const a=document.createElement('a');a.href=URL.createObjectURL(new Blob([body],{type}));a.download=name;document.body.append(a);a.click();}
</script>''')
    def log_message(self,*args): pass

def main():
    binary=ROOT/'artifacts/invora-webview-tests'
    subprocess.run(['xcrun','swiftc','-swift-version','5','-module-cache-path',str(ROOT/'artifacts/swift-module-cache'),str(ROOT/'tools/Invora.Mac/ShopWindow.swift'),str(ROOT/'tests/Invora.Mac.WebView/main.swift'),'-o',str(binary)],check=True)
    server=ThreadingHTTPServer(('127.0.0.1',0),Fixture)
    thread=threading.Thread(target=server.serve_forever,daemon=True);thread.start()
    try:
        with tempfile.TemporaryDirectory(prefix='invora-webview-',dir='/private/tmp') as folder:
            subprocess.run([str(binary),f'http://127.0.0.1:{server.server_port}',folder],check=True,timeout=60)
    finally:
        server.shutdown();server.server_close();thread.join()
    if os.environ.get('INVORA_DESKTOP_PREVIEW_URL'):
        subprocess.run([str(binary),os.environ['INVORA_DESKTOP_PREVIEW_URL'],str(ROOT/'artifacts'),'--live-preview'],check=True,timeout=60)
if __name__=='__main__': main()
