import pathlib,json,time
from pyocd.target.pack.cmsis_pack import CmsisPack
from pyocd.target import normalise_target_type_name
base=(pathlib.Path(__file__).resolve().parents[1]/'portable'); targets=[]; mapping={}; failures=[]
for path in sorted((base/'packs').glob('*.pack')):
 try:
  pack=CmsisPack(str(path)); count=0
  for dev in pack.devices:
   name=normalise_target_type_name(dev.part_number)
   targets.append(dict(name=name,part_number=dev.part_number,vendor=dev.vendor,part_families=dev.families,source='pack'))
   mapping[name]=path.name; count+=1
  print(path.name,count,flush=True)
 except Exception as e: failures.append(dict(pack=path.name,error=str(e))); print('ERROR',path.name,str(e),flush=True)
# Built-in entries remain available alongside exact DFP part numbers.
import subprocess
r=subprocess.run([str((base/'python/python.exe').resolve()),'-m','pyocd','json','--targets','--no-config'],capture_output=True,text=True,encoding='utf-8'); builtins=json.loads(r.stdout[r.stdout.find('{'):])['targets']
(base/'targets.json').write_text(json.dumps(builtins+targets),encoding='utf-8'); (base/'target-packs.json').write_text(json.dumps(mapping),encoding='utf-8'); (base/'catalog-errors.json').write_text(json.dumps(failures),encoding='utf-8')
print('TOTAL',len(targets),'ERRORS',failures,flush=True)
