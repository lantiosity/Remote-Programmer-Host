import concurrent.futures, urllib.request, pathlib, xml.etree.ElementTree as ET, json, time
base=(pathlib.Path(__file__).resolve().parents[1]/'portable'); out=base/'packs'; out.mkdir(exist_ok=True)
items=[x.attrib for x in ET.parse(base/'index.pidx').iter('pdsc') if x.get('name','').lower().startswith('stm32') and x.get('name','').lower().endswith('dfp')]
def fetch(x):
 name=f"{x['vendor']}.{x['name']}.{x['version']}.pack"; path=out/name; url=x['url']+name
 for attempt in range(3):
  try:
   if not path.exists(): urllib.request.urlretrieve(url,path.with_suffix('.tmp')); path.with_suffix('.tmp').replace(path)
   print(name,path.stat().st_size,flush=True); return dict(x,file=name,download=url)
  except Exception as e:
   if attempt==2: print('FAILED',name,str(e),flush=True); return dict(x,error=str(e))
   time.sleep(2)
with concurrent.futures.ThreadPoolExecutor(max_workers=4) as pool: results=list(pool.map(fetch,items))
(base/'packs-manifest.json').write_text(json.dumps(results,indent=2),encoding='utf-8')
