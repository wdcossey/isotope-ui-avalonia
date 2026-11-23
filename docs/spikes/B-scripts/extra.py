
import json, sys, re, subprocess, collections
sys.path.insert(0, sys.argv[2])
from areas import area
cs=json.load(open(sys.argv[1],encoding='utf-8'))
import os
U = os.environ.get('UPSTREAM') or os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..', '..', 'upstream'))
def git(*a): return subprocess.run(['git','-C',U,'-c','core.quotepath=off']+list(a),capture_output=True,check=True).stdout.decode('utf-8','replace')
out=[]
# partial files added in src/Reactor (Name.Part.cs) over history, with whether base Name.cs existed
alive=set(git('ls-tree','-r','--name-only','origin/main','src/Reactor').splitlines())
for c in reversed(cs):
    for f in c['files']:
        p=f['path']
        if (f['status'] or '').startswith('A') and p.startswith('src/Reactor/') and re.search(r'/[A-Za-z0-9]+\.[A-Za-z0-9]+\.cs$',p) and not p.endswith('.g.cs'):
            out.append(f"PARTIAL-ADD {c['cdate'][:10]} {c['h'][:8]} {area(p)} {p} +{f['add']} alive={p in alive} | {c['subject'][:60]}")
# window renames
for c in cs:
    if c['cdate'][:10]>='2026-08-22':
        rs=[f for f in c['files'] if (f['status'] or '').startswith('R')]
        if rs: out.append(f"WIN-RENAME {c['cdate'][:10]} {c['h'][:8]} n={len(rs)} {rs[0]['old']} -> {rs[0]['path']} | {c['subject'][:70]}")
# window: dirs reorganised — directories created/removed in src/Reactor (from file adds/deletes)
pat=re.compile(r'\bMicrosoft\.UI\.(Xaml|Composition|Dispatching|Input|Text|Windowing|Content)\b|\bWindows\.(UI|Foundation|System|Graphics|ApplicationModel|Storage)\b|WinRT|DispatcherQueue|\bUIElement\b|FrameworkElement|DependencyObject|DependencyProperty')
open(sys.argv[3]+'/winui_regex.txt','w').write(pat.pattern)
tot=0; zero=0; zl=0; rows=[]
for p in sorted(alive):
    a=area(p)
    if a not in ('E1','E2','E3','E4','E5'): continue
    txt=git('show',f'origin/main:{p}')
    nl=txt.count('\n'); nw=sum(1 for l in txt.splitlines() if pat.search(l))
    rows.append((a,p,nl,nw)); tot+=nl
    if nw==0: zero+=1; zl+=nl
for r in rows: out.append('WR '+' | '.join(map(str,r)))
out.append(f'WR-SUM files={len(rows)} lines={tot} zero-match files={zero} lines={zl}')
# 73328eba: RenderContext diff stat
out.append(git('show','--stat=200','--format=%h %s','73328eba','--','src/Reactor/Core/RenderContext.cs','src/Reactor/Core/Reconciler.Diagnostics.cs'))
out.append(git('show','--format=','-U0','73328eba','--','src/Reactor/Core/RenderContext.cs')[:3000])
print('\n'.join(out))
