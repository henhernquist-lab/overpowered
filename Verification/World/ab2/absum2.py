import re,glob,os,statistics as st
S='/private/tmp/claude-501/-Users-melaniehernquist-Documents-ChatGPT-op/da48ca15-bdf3-4711-bc91-29bca88e21cf/scratchpad/'
def load(path):
    t=open(path).read()
    r={'street':[],'flight':[],'gen':[],'stages':{},'struct':[],'draw':{},'prof':{}}
    for m in re.finditer(r'MEASURED (street|flight) r\d attempt\d: FPS=([\d.]+).*?drawCalls=(\d+).*?staticBatchedDraws=(\d+) staticBatches=(\d+) instancedDraws=(\d+) instancedBatches=(\d+).*?tris=(\d+) visibleRenderers=(\d+).*?(npcLod[^b]*)batchUnities before/after=(\d+)/(\d+)',t):
        v=m.group(1); r[v].append(float(m.group(2))); r['draw'][v]=m.groups()[2:10]
    for m in re.finditer(r'GEN (\d): select->session ready (\d+) ms.*?city build ([\d.]+) ms; total build incl. session ([\d.]+)',t): r['gen'].append((int(m.group(2)),float(m.group(3)),float(m.group(4))))
    for m in re.finditer(r'GEN (\d) STAGE (.*?): ([\d.]+) ms',t): r['stages'].setdefault(m.group(2),[]).append(float(m.group(3)))
    r['struct']=re.findall(r'STRUCTURE (.*)',t)
    for m in re.finditer(r'PROFILE (street|flight) (\S+): ([\d.]+)',t): r['prof'][(m.group(1),m.group(2))]=float(m.group(3))
    return r
runs={}
for tag in ['A','B','S']:
    for i in (1,2,3):
        p=(S+'world-base2' if tag=='A' else S+'wt-world')+f'/Verification/World/ab2/{tag}{i}/results.txt'
        if os.path.exists(p): runs[f'{tag}{i}']=load(p)
for k,r in runs.items(): print(k,'street',r['street'],'flight',r['flight'],'gen',[g[1] for g in r['gen']])
for tag,name in [('A','BASELINE 84c4129+harness'),('B','CANDIDATE BuildingMeshes'),('S','CANDIDATE per-piece StaticBatching')]:
    rs=[r for k,r in runs.items() if k[0]==tag]
    if not rs: continue
    s=[x for r in rs for x in r['street']]; f=[x for r in rs for x in r['flight']]
    g=[x[1] for r in rs for x in r['gen'][1:]]; gs=[x[2] for r in rs for x in r['gen'][1:]]
    print(f"{name}: street mean {st.mean(s):.1f} (min {min(s):.1f} max {max(s):.1f}, n={len(s)}); flight mean {st.mean(f):.1f} (min {min(f):.1f} max {max(f):.1f}); city build (warm gens) {st.mean(g):.0f} ms, incl session {st.mean(gs):.0f} ms")
    print('  per-run street means', [round(st.mean(r['street']),1) for r in rs], 'flight', [round(st.mean(r['flight']),1) for r in rs])
    print('  draw street (draws,staticDraws,staticBatches,instDraws,instBatches,tris,visible):', rs[0]['draw'].get('street'))
    print('  draw flight:', rs[0]['draw'].get('flight'))
    for k,v in rs[0]['stages'].items():
        vals=[x for r in rs for x in r['stages'].get(k,[])[1:]]
        print(f'  stage {k}: {st.mean(vals):.1f} ms' if vals else '')
    for l in rs[0]['struct']: print('  ',l[:300])
    for k in sorted(rs[0]['prof']): print('  prof',k, [r['prof'].get(k) for r in rs])
