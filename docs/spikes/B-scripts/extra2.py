
import json, sys, re, subprocess, collections
sys.path.insert(0, sys.argv[2])
from areas import area
cs=json.load(open(sys.argv[1],encoding='utf-8'))
import os
U = os.environ.get('UPSTREAM') or os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..', '..', 'upstream'))
def git(*a): return subprocess.run(['git','-C',U,'-c','core.quotepath=off']+list(a),capture_output=True,check=True).stdout.decode('utf-8','replace')
out=[]
for c in reversed(cs):
    adds=[f for f in c['files'] if (f['status'] or '').startswith('A') and area(f['path']) in ('E1','E2','E3','E4','E5')]
    if adds:
        donors=[(f['path'],f['del']) for f in c['files'] if f['del']>=40 and f['path'].startswith(('src/Reactor/','Reactor/','Duct/'))]
        out.append(f"EADD {c['cdate'][:10]} {c['h'][:8]} {[ (a['path'].split('/')[-1],a['add']) for a in adds]} donors={donors[:6]}")
# area sizes at origin/main
sz=collections.Counter(); nf=collections.Counter()
for line in git('grep','-c','','origin/main','--','src/Reactor','tests','docs','src').splitlines():
    m=re.match(r'^origin/main:(.*):(\d+)$',line)
    if not m: continue
    p,n=m.group(1),int(m.group(2)); a=area(p)
    if p.startswith('src/Reactor/') and not p.startswith('src/Reactor/') is False: pass
    sz[a]+=n; nf[a]+=1
out.append('SIZES '+json.dumps({a:[nf[a],sz[a]] for a in sorted(sz)}))
print('\n'.join(out))
