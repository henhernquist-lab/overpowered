from pathlib import Path
import re, subprocess, sys
import argparse, hashlib, json, datetime
parser=argparse.ArgumentParser(description="Static compile of isolated sources using an imported reference checkout; never starts Unity.")
parser.add_argument('--reference-project',required=True,type=Path)
args=parser.parse_args()
w=Path(__file__).resolve().parents[1]
origin=args.reference_project.resolve()
out=w/'Verification/CombatLab/static';out.mkdir(parents=True,exist_ok=True)
results=[]
sdk=Path('/Applications/Unity/Hub/Editor/6000.6.0f1/Unity.app/Contents/Resources/Scripting')
for asm in ['Assembly-CSharp','Assembly-CSharp-Editor']:
 src=max((origin/'Library/Bee/artifacts').glob('*/'+asm+'.rsp'),key=lambda p:p.stat().st_mtime); lines=[]; seen=set()
 for line in src.read_text().splitlines():
  if line.startswith(('-out:', '-refout:')):continue
  is_source=bool(re.fullmatch(r'"?Assets/.*\.cs"?',line))
  if is_source:
   rel=line.strip('"');p=w/rel
   if not p.exists():continue
   seen.add(p);line='"'+str(p)+'"'
  else:
   def path(m):
    p=m.group(1)
    if p.endswith('/Assembly-CSharp.dll') or p.endswith('/Assembly-CSharp.ref.dll'):return '"'+str(out/'Assembly-CSharp.dll')+'"'
    if p.startswith(('Library/','Assets/','Packages/')):p=str(origin/p)
    return '"'+p+'"'
   line=re.sub('"([^"\\n]+)"',path,line)
  lines.append(line)
 base='Assets/Editor' if asm.endswith('Editor') else 'Assets/Scripts'
 for p in sorted((w/base).rglob('*.cs')):
  if p not in seen:lines.append('"'+str(p)+'"')
 lines.append('-out:"'+str(out/(asm+'.dll'))+'"')
 rsp=out/(asm+'.rsp');rsp.write_text('\n'.join(lines)+'\n')
 result=subprocess.run([str(sdk/'NetCoreRuntime/dotnet'),str(sdk/'DotNetSdk/sdk/8.0.318/Roslyn/bincore/csc.dll'),'@'+str(rsp)],cwd=origin,text=True,capture_output=True)
 (out/(asm+'.compile.txt')).write_text(result.stdout+result.stderr)
 print(asm, 'PASS' if result.returncode==0 else 'FAIL');print((result.stdout+result.stderr)[-10000:])
 results.append(dict(check=asm,exit=result.returncode))
 if result.returncode:sys.exit(result.returncode)

# Exercise the SAME pure policy the authoritative CityNpc.Damage operation calls.
pack=max((sdk/'DotNetSdk/packs/Microsoft.NETCore.App.Ref').iterdir(),key=lambda p:p.name)/'ref/net8.0'
lines=['-nologo','-target:exe','-out:"'+str(out/'CombatGuardChecks.dll')+'"']
lines += ['-r:"'+str(p)+'"' for p in sorted(pack.glob('*.dll'))]
lines += ['"'+str(w/'Assets/Scripts/CombatLab/EnemyGuardMath.cs')+'"','"'+str(w/'tools/CombatGuardChecks.cs')+'"']
rsp=out/'guard.rsp';rsp.write_text('\n'.join(lines)+'\n')
result=subprocess.run([str(sdk/'NetCoreRuntime/dotnet'),str(sdk/'DotNetSdk/sdk/8.0.318/Roslyn/bincore/csc.dll'),'@'+str(rsp)],capture_output=True,text=True)
if result.returncode:print(result.stdout+result.stderr);sys.exit(result.returncode)
(out/'CombatGuardChecks.runtimeconfig.json').write_text(json.dumps({'runtimeOptions':{'tfm':'net8.0','framework':{'name':'Microsoft.NETCore.App','version':'8.0.21'}}}))
result=subprocess.run([str(sdk/'NetCoreRuntime/dotnet'),str(out/'CombatGuardChecks.dll')],text=True,capture_output=True)
(out/'guard-policy.txt').write_text(result.stdout+result.stderr);print(result.stdout+result.stderr)
results.append(dict(check='pure guard policy',exit=result.returncode,passes=sum(x.startswith('PASS ') for x in result.stdout.splitlines())))
(out/'results.json').write_text(json.dumps(dict(timestamp=datetime.datetime.now(datetime.timezone.utc).isoformat(),reference_project=str(origin),runtime_verified=False,results=results),indent=2)+'\n')
sys.exit(result.returncode)
