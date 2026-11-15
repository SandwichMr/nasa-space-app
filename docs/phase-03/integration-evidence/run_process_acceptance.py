from pathlib import Path
import concurrent.futures, subprocess, time, json, hashlib
root=Path(__file__).resolve().parents[3]
player=root/'Builds/Phase3Mission/StandaloneLinux64/Phase3Mission.x86_64'
logs=root/'Logs/phase03-integration'
evidence=root/'docs/phase-03/integration-evidence'

def flow_run(flow,port):
 d=logs/f'acceptance-{flow}';d.mkdir(parents=True,exist_ok=True)
 children=[]
 def start(args,name):
  p=subprocess.Popen([str(player),'-batchmode','-nographics',*args,'-logFile',str(d/f'{name}.log')],cwd=root,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
  children.append(p);return p
 try:
  host=start(['-trainer-role','authority','-trainer-bind','127.0.0.1','-trainer-port',str(port),'-trainer-timescale','2'],'authority')
  for _ in range(100):
   if host.poll() is not None:raise RuntimeError(f'authority exited {host.returncode}')
   if (d/'authority.log').exists() and 'Listening' in (d/'authority.log').read_text(errors='replace'):break
   time.sleep(.15)
  else:raise RuntimeError('authority not listening')
  print(f'{flow}: authority ready on loopback {port}',flush=True)
  players={role:start(['-trainer-role','client','-trainer-server','127.0.0.1','-trainer-port',str(port),'-trainer-connect','-trainer-autopilot',flow,'-trainer-autopilot-role',role,'-trainer-autopilot-timeout','240'],role) for role in ['explorer','support']}
  began=time.monotonic();sockets=False
  while any(p.poll() is None for p in players.values()):
   if time.monotonic()-began>260:raise RuntimeError('client wall-clock timeout')
   if not sockets and time.monotonic()-began>4:
    all_sockets=subprocess.check_output(['ss','-tn'],text=True)
    (d/'sockets.txt').write_text('\n'.join(x for x in all_sockets.splitlines() if f':{port}' in x)+'\n')
    sockets=True
   if host.poll() is not None:raise RuntimeError(f'authority died {host.returncode}')
   time.sleep(.5)
  result={'flow':flow,'port':port,'authority_timescale':2,'build_info':(player.parent/'build-info.txt').read_text(),'player_sha256':hashlib.sha256(player.read_bytes()).hexdigest(),'clients':{}}
  for role,p in players.items():
   text=(d/f'{role}.log').read_text(errors='replace')
   verdict=[x for x in text.splitlines() if '[Autopilot] RESULT' in x]
   result['clients'][role]={'exit':p.returncode,'verdict':verdict}
  result['passed']=all(x['exit']==0 and any('RESULT PASS' in v for v in x['verdict']) for x in result['clients'].values())
  (d/'summary.json').write_text(json.dumps(result,indent=2)+'\n');print(json.dumps(result),flush=True)
  return result
 finally:
  for p in reversed(children):
   if p.poll() is None:
    p.terminate()
    try:p.wait(timeout=10)
    except subprocess.TimeoutExpired:p.kill();p.wait()

with concurrent.futures.ThreadPoolExecutor(max_workers=2) as pool:
 futures=[pool.submit(flow_run,'A',23931),pool.submit(flow_run,'B',23932)]
 results=[f.result() for f in futures]
(evidence/'process-acceptance.json').write_text(json.dumps(results,indent=2)+'\n')
raise SystemExit(0 if all(x['passed'] for x in results) else 1)
