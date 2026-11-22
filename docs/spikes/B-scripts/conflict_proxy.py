
import sys, re, subprocess, datetime, json
U=r'<repo root>/upstream'
def git(*a): return subprocess.run(['git','-C',U,'-c','core.quotepath=off']+list(a),capture_output=True,check=True).stdout.decode('utf-8','replace')
pat=re.compile(open(sys.argv[1]+'/winui_regex.txt').read())
EPATHS=['src/Reactor/Core/RenderContext.cs','src/Reactor/Core/ChildReconciler.cs','src/Reactor/Core/Internal/KeyedListDiff.cs','src/Reactor/Yoga','src/Reactor/Hooks','src/Reactor/Data','src/Reactor/Controls/Validation']
EXC=[':(exclude)src/Reactor/Yoga/FlexPanel.cs']
def rev_before(d): return git('rev-list','-1','--first-parent',f'--before={d}T00:00:00-08:00','origin/main').strip()
mon=datetime.date(2026,8,24); prev=rev_before(mon.isoformat()); rows=[]
tot_h=0; tot_near=0
while mon<=datetime.date(2026,11,16):
    nxt=mon+datetime.timedelta(days=7)
    r=git('rev-parse','origin/main').strip() if nxt>datetime.date(2026,11,22) else rev_before(nxt.isoformat())
    files=[l.split('\t')[2] for l in git('diff','-M','--numstat',prev,r,'--',*EPATHS,*EXC).splitlines() if l.strip()]
    wk_h=0; wk_near=0; detail=[]
    for p in files:
        try: old=git('show',f'{prev}:{p}').splitlines()
        except subprocess.CalledProcessError: old=[]
        wl=[i+1 for i,l in enumerate(old) if pat.search(l)]
        d=git('diff','-U0',prev,r,'--',p)
        hs=[(int(m.group(1)),int(m.group(2) or 1)) for m in re.finditer(r'^@@ -(\d+)(?:,(\d+))? \+',d,re.M)]
        near=0
        for s,n in hs:
            lo=s-3; hi=s+max(n,1)+2
            if any(lo<=w<=hi for w in wl): near+=1
        wk_h+=len(hs); wk_near+=near
        if wl: detail.append(f'{p.split("/")[-1]} {near}/{len(hs)}')
    y,w,_=mon.isocalendar()
    rows.append([f'{y}-W{w:02d}',len(files),wk_h,wk_near,'; '.join(detail)])
    tot_h+=wk_h; tot_near+=wk_near; prev=r; mon=nxt
print('| ISO week | E files changed | hunks (-U0) | hunks within 3 lines of a WinUI-matching line | per file with WinUI refs: near/hunks |')
print('|---|---|---|---|---|')
for x in rows: print('| '+' | '.join(map(str,x))+' |')
print(f'\ntotal hunks={tot_h} near WinUI lines={tot_near}')
