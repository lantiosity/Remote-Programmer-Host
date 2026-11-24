import pathlib, subprocess, json, time
base=(pathlib.Path(__file__).resolve().parents[1]/'portable').resolve(); args=[str(base/'python/python.exe'),'-m','pyocd','json','--targets','--no-config']
for p in sorted((base/'packs').glob('*.pack')): args+=['--pack',str(p)]
r=subprocess.run(args,capture_output=True,text=True,encoding='utf-8'); (base/'catalog-output.txt').write_text(r.stdout+r.stderr,encoding='utf-8')
start=r.stdout.find('{'); data=json.loads(r.stdout[start:]); print('EXIT',r.returncode,'STATUS',data.get('status'),'TARGETS',len(data.get('targets',[]))); print(data.get('targets',[])[:2]); (base/'targets.json').write_text(json.dumps(data['targets']),encoding='utf-8')
