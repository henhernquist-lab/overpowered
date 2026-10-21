#!/usr/bin/env python3
"""Validate combat asset topology and requested progression; does not import assets or run Unity."""
from pathlib import Path
import re, subprocess, xml.etree.ElementTree as ET, json
w=Path(__file__).resolve().parents[1]
lines=[]
def check(ok,label):
    line=('PASS ' if ok else 'FAIL ')+label
    lines.append(line);print(line)
    if not ok:raise AssertionError(label)
def read(p):return (w/p).read_text()
def guid(p):return re.search(r'guid: ([a-f0-9]{32})',read(p+'.meta'))[1]
wave=read('Assets/Resources/ModeDirectors/EndlessWaves.asset')
entries=re.findall(r'  - Archetype: .*?guid: ([a-f0-9]+).*?\n    FromWave: (\d+)\n    Weight: (\d+)',wave)
expected=[('Rusher',1,3),('Gunner',2,2),('Brute',3,1),('Skirmisher',3,2),('Bulwark',5,1)]
check(entries==[(guid('Assets/Resources/Enemies/'+n+'.asset'),str(f),str(v)) for n,f,v in expected],'authored wave unlocks and weights match requested progression')
for number in range(1,13):
    eligible=[(n,v) for n,f,v in expected if number>=f]
    scores=[0]*len(eligible);mix=[]
    for index in range(min(12,3+2*(number-1))):
        scores=[s+eligible[i][1] for i,s in enumerate(scores)]
        best=max(range(len(scores)),key=lambda i:scores[i]);scores[best]-=sum(v for _,v in eligible);mix.append(eligible[best][0])
    check(('Skirmisher' in mix)==(number>=3),'Skirmisher present only from wave 3: '+str(number))
    check(('Bulwark' in mix)==(number>=5),'Bulwark present only from wave 5: '+str(number))
    check(set(mix)=={n for n,_ in eligible},'bounded initial mix keeps every eligible type: wave '+str(number))
for name in ['Rusher','Gunner','Brute','Skirmisher','Bulwark']:
    check(guid('Assets/Resources/Enemies/'+name+'.asset') in read('Assets/Resources/CombatLabSettings.asset'),'lab registers '+name)
for name in ['hero','villain','free-play']:
    check('Director: {fileID: 0}' in read('Assets/Resources/Modes/'+name+'.asset'),'no round director added to '+name)
for name in ['endless-fight','endless-fight-villain']:
    check(guid('Assets/Resources/ModeDirectors/EndlessWaves.asset') in read('Assets/Resources/Modes/'+name+'.asset'),'shared expanded director in '+name)
check('MaxAlive: 12' in wave and not re.search(r'^  Events: .*fileID: [1-9]',wave,re.M),'12-alive cap and disabled milestone/miniboss events preserved')
check(guid('Assets/Scripts/EnemyArchetype.cs') in read('Assets/Resources/Enemies/Skirmisher.asset'),'Skirmisher asset references authoritative archetype script')
check(guid('Assets/Scripts/EnemyArchetype.cs') in read('Assets/Resources/Enemies/Bulwark.asset'),'Bulwark asset references authoritative archetype script')
check(guid('Assets/Scripts/CombatLab/CombatLabSettings.cs') in read('Assets/Resources/CombatLabSettings.asset'),'lab settings reference correct script')
check(guid('Assets/Scenes/CombatLab.unity') in read('ProjectSettings/EditorBuildSettings.asset'),'lab scene GUID registered for reliable reset')
root=ET.fromstring(read('Assets/Resources/CombatLabPanel.uxml'));names=[x.attrib['name'] for x in root.iter() if 'name' in x.attrib]
check(len(names)==len(set(names)),'lab UXML is well-formed with unique control names (not a Unity import test)')
for name in ['toggle','enemy','one','group','clear','health','energy','difficulty','reset','exit','status','feedback']:
    check('lab-'+name in names,'lab control authored: '+name)
for forbidden in ['gap:', 'box-shadow:', 'z-index:', 'linear-gradient(', 'style=']:
    check(forbidden not in read('Assets/Resources/CombatLabPanel.uss')+read('Assets/Resources/CombatLabPanel.uxml'),'unsupported web CSS absent: '+forbidden)
# Byte comparison to committed baseline; all original movement/power/signature tuning stays intact.
for folder in ['Assets/Resources/Powers','Assets/Resources/Forge/Signatures','Assets/Scripts/Forge']:
    diff=subprocess.check_output(['git','diff','--name-only','2ed79a9','--',folder],cwd=w,text=True)
    check(not diff,'baseline preserved byte-for-byte: '+folder)
for filename in ['Assets/Scripts/SuperHeroController.cs','Assets/Scripts/CityDistrict.cs','Assets/Scripts/EndlessWaveDirector.cs','Assets/Resources/HumanoidAnimationTuning.asset']:
    original=subprocess.check_output(['git','show','2ed79a9:'+filename],cwd=w)
    check(original==(w/filename).read_bytes(),'baseline preserved byte-for-byte: '+filename)
for filename in ['Assets/Scripts/CombatLab/EnemyEquipment.cs','Assets/Scripts/CombatLab/CityNpc.Evasion.cs','Assets/Scripts/CombatLab/CityNpc.Guard.cs']:
    source=read(filename)
    check('new Mesh(' not in source and 'new Material(' not in source and '.material=' not in source,'no new enemy mesh/material allocation: '+filename)
out=w/'Verification/CombatLab/static';out.mkdir(parents=True,exist_ok=True)
(out/'asset-data-checks.txt').write_text('\n'.join(lines)+'\n')
(out/'asset-data-results.json').write_text(json.dumps(dict(passes=len(lines),failures=0,scope='static data/topology and baseline byte comparisons only',unity_import_verified=False),indent=2)+'\n')
