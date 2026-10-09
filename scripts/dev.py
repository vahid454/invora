#!/usr/bin/env python3
"""Initialize private local configuration and run the complete development stack."""
import os,secrets,subprocess,sys,time,urllib.request
from pathlib import Path
root=Path(__file__).resolve().parents[1]
os.chdir(root)
configuration=root/'.env'
if not configuration.exists():
    values={'POSTGRES_DB':'invora','POSTGRES_USER':'invora','POSTGRES_PASSWORD':secrets.token_hex(24),'INVORA_SIGNING_KEY':secrets.token_hex(32),'INVORA_BOOTSTRAP_KEY':secrets.token_hex(32),'INVORA_BROWSER_ORIGIN':'http://127.0.0.1:8080','INVORA_ENVIRONMENT':'Development','INVORA_HTTP_PORT':'8080'}
    configuration.write_text(''.join(f'{k}={v}\n' for k,v in values.items()))
    configuration.chmod(0o600)
    print('Generated private local configuration in .env. Use its setup key on /setup.')
if len(sys.argv)>1 and sys.argv[1]=='init':sys.exit(0)
compose=['docker','compose','--env-file',str(configuration)]
for args in [['build'],['up','-d','db'],['run','--rm','migrate'],['up','-d','api','web']]:subprocess.run(compose+args,check=True)
for attempt in range(30):
    try:
        with urllib.request.urlopen('http://127.0.0.1:8080/health/ready',timeout=3) as response:
            if response.status==200:print('Invora is ready at http://127.0.0.1:8080. Open /setup for first-time registration.');break
    except OSError:time.sleep(2)
else:raise SystemExit('Readiness failed. Inspect docker compose logs.')
