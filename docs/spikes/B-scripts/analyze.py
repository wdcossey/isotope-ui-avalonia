
import json, sys, re, collections, datetime, statistics, subprocess
sys.path.insert(0, sys.argv[2])
from areas import area, AREAS
cs=json.load(open(sys.argv[1],encoding='utf-8'))  # newest first, first-parent
OUT=sys.argv[3]
U=r'<repo root>/upstream'
def git(*a): return subprocess.run(['git','-C',U,'-c','core.quotepath=off']+list(a),capture_output=True,check=True).stdout.decode('utf-8','replace')
EXCL=[r'(^|/)package-lock\.json$', r'/traces/', r'\.(etl|nettrace)$', r'/Monaco/Assets/', r'\.api\.txt$',
      r'(Md4cGenerated|YogaGenerated)/', r'Md4cFixtures/spec\.txt$', r'reactor-search-index\.json$', r'/Baselines/.*\.json$', r'^docs/specs/tasks/.*\.json$']
EX=re.compile('|'.join(EXCL))
def excluded(p): return bool(EX.search(p))
alive=set(git('ls-tree','-r','--name-only','origin/main').splitlines())
# lineage: walk oldest->newest is reversed(cs)? cs newest first. Walk newest->oldest.
final={}
for c in cs:
    for f in c['files']:
        f['final']=final.get(f['path'],f['path'])
    for f in c['files']:
        st=f['status'] or ''
        if st.startswith('R'):
            final[f['old']]=final.get(f['path'],f['path'])
        elif st.startswith('D'):
            final[f['path']]=f['path']
for c in cs:
    c['date']=datetime.date.fromisoformat(c['cdate'][:10])
    for f in c['files']:
        f['area_t']=area(f['path']); f['area_l']=area(f['final']); f['ex']=excluded(f['path'])
        f['n']=f['add']+f['del']
ENG=['E1','E2','E3','E4','E5']; WIN=['W1','W2','W3','W4','W5']
W0=datetime.date(2026,8,22); W1d=datetime.date(2026,11,22)
win=[c for c in cs if W0<=c['date']<=W1d]
def wk(d): y,w,_=d.isocalendar(); return f'{y}-W{w:02d}'
md=[]
def table(hdr,rows):
    md.append('| '+' | '.join(hdr)+' |'); md.append('|'+'---|'*len(hdr))
    for r in rows: md.append('| '+' | '.join(str(x) for x in r)+' |')
    md.append('')
def agg(commits,key,akey):
    cm=collections.defaultdict(lambda: collections.defaultdict(set)); ad=collections.defaultdict(lambda: collections.defaultdict(lambda:[0,0]))
    for c in commits:
        k=key(c)
        for f in c['files']:
            if f['ex']: continue
            a=f[akey]; cm[k][a].add(c['h']); ad[k][a][0]+=f['add']; ad[k][a][1]+=f['del']
            if a in ENG: cm[k]['E*'].add(c['h']); ad[k]['E*'][0]+=f['add']; ad[k]['E*'][1]+=f['del']
            if a in WIN: cm[k]['W*'].add(c['h']); ad[k]['W*'][0]+=f['add']; ad[k]['W*'][1]+=f['del']
    return cm,ad
COLS=ENG+['E*']+WIN+['W*','O1','T','D','S','X']
# excluded totals
exl=collections.Counter(); exl_w=collections.Counter()
for c in cs:
    for f in c['files']:
        if f['ex']:
            m=EX.search(f['path']).re.pattern if False else next(p for p in EXCL if re.search(p,f['path']))
            exl[m]+=f['n']
            if c in win: pass
for c in win:
    for f in c['files']:
        if f['ex']: exl_w[next(p for p in EXCL if re.search(p,f['path']))]+=f['n']
md.append('## EXCLUDED'); table(['pattern','lines whole history','lines window'],[(f'\x60{p}\x60',exl[p],exl_w[p]) for p in EXCL])
tot_all=sum(f['n'] for c in cs for f in c['files']); tot_w=sum(f['n'] for c in win for f in c['files'])
md.append(f'total lines all={tot_all} excluded={sum(exl.values())}; window total={tot_w} excluded={sum(exl_w.values())}'); md.append('')
# 1 weekly window
cm,ad=agg(win,lambda c:wk(c['date']),'area_t')
weeks=sorted(cm.keys())
allc=collections.Counter(wk(c['date']) for c in win)
md.append('## W1 weekly commits per area (window, path at time)')
table(['week','all']+COLS,[[w,allc[w]]+[len(cm[w].get(a,())) for a in COLS] for w in weeks]+[['total',len(win)]+[len(set().union(*[cm[w].get(a,set()) for w in weeks])) for a in COLS]])
md.append('## W2 weekly lines changed (added+deleted) per area (window)')
table(['week']+COLS,[[w]+[sum(ad[w].get(a,[0,0])) for a in COLS] for w in weeks]+[['total']+[sum(sum(ad[w].get(a,[0,0])) for w in weeks) for a in COLS]])
md.append('## W3 weekly added/deleted engine and W (window)')
table(['week','E* +','E* -','W* +','W* -','O1 +','O1 -'],[[w,ad[w]['E*'][0],ad[w]['E*'][1],ad[w]['W*'][0],ad[w]['W*'][1],ad[w]['O1'][0],ad[w]['O1'][1]] for w in weeks])
# per-area totals window with +/-
md.append('## W4 window totals per area')
tw=collections.defaultdict(lambda:[set(),0,0])
for c in win:
    for f in c['files']:
        if f['ex']: continue
        for a in [f['area_t']]+(['E*'] if f['area_t'] in ENG else [])+(['W*'] if f['area_t'] in WIN else []):
            tw[a][0].add(c['h']); tw[a][1]+=f['add']; tw[a][2]+=f['del']
table(['area','commits','added','deleted','lines/week (13.3 wk)'],[[a,len(tw[a][0]),tw[a][1],tw[a][2],round((tw[a][1]+tw[a][2])/(93/7))] for a in COLS])
# 2 whole history monthly, lineage-aware and path-at-time
for akey,label in (('area_l','lineage (area of file current path)'),('area_t','path at time')):
    cmM,adM=agg(cs,lambda c:c['date'].strftime('%Y-%m'),akey)
    months=sorted(cmM.keys())
    allm=collections.Counter(c['date'].strftime('%Y-%m') for c in cs)
    md.append(f'## H monthly commits per area, {label}')
    table(['month','all']+COLS,[[m,allm[m]]+[len(cmM[m].get(a,())) for a in COLS] for m in months])
    md.append(f'## H monthly lines changed per area, {label}')
    table(['month']+COLS,[[m]+[sum(adM[m].get(a,[0,0])) for a in COLS] for m in months]+[['total']+[sum(sum(adM[m].get(a,[0,0])) for m in months) for a in COLS]])
# weekly engine series whole history (lineage)
cmW,adW=agg(cs,lambda c:wk(c['date']),'area_l')
allw=collections.Counter(wk(c['date']) for c in cs)
md.append('## HW weekly engine vs W vs src/Reactor whole history (lineage)')
rows=[]
for w in sorted(cmW):
    sr=sum(sum(adW[w].get(a,[0,0])) for a in ENG+WIN+['O1'])
    e=sum(adW[w].get('E*',[0,0])); 
    rows.append([w,allw[w],len(cmW[w].get('E*',())),e,len(cmW[w].get('W*',())),sum(adW[w].get('W*',[0,0])),sr, f'{100*e/sr:.0f}%' if sr else '-'])
table(['week','commits','E* commits','E* lines','W* commits','W* lines','src/Reactor lines','E share of src/Reactor'],rows)
# quarters
def per(c):
    d=c['date']
    if d<datetime.date(2026,5,22): return 'P1 03-18..05-21'
    if d<W0: return 'P2 05-22..08-21'
    return 'P3 08-22..11-22'
pdays={'P1 03-18..05-21':(datetime.date(2026,5,21)-datetime.date(2026,3,18)).days+1,'P2 05-22..08-21':(W0-datetime.date(2026,5,22)).days,'P3 08-22..11-22':(W1d-W0).days+1}
cmP,adP=agg(cs,per,'area_l'); allp=collections.Counter(per(c) for c in cs)
md.append('## HP per-period rates (lineage), per 7 days')
rows=[]
for p in sorted(cmP):
    wks=pdays[p]/7
    rows.append([p,pdays[p],round(allp[p]/wks,1)]+[f'{len(cmP[p].get(a,()))/wks:.1f} / {sum(adP[p].get(a,[0,0]))/wks:.0f}' for a in ['E1','E2','E3','E4','E5','E*','W*','O1','T']])
table(['period','days','commits/wk']+[a+' commits/wk / lines/wk' for a in ['E1','E2','E3','E4','E5','E*','W*','O1','T']],rows)
json.dump({'win_hashes':[c['h'] for c in win]},open(OUT+'/win.json','w'))
# 3 per engine file
def per_file(commits,label):
    st=collections.defaultdict(lambda:{'c':set(),'add':0,'del':0,'cw':set(),'cw12':set(),'co':set(),'ct':set(),'only':set()})
    for c in commits:
        fs=[f for f in c['files'] if not f['ex']]
        areas=set(f['area_t'] for f in fs)
        hasW=bool(areas & set(WIN)); hasW12=bool(areas & {'W1','W2'})
        hasO=('O1' in areas); hasT=any(f['path'].startswith('tests/Reactor.Tests/') for f in fs)
        onlyE=areas <= set(ENG)|{'T','D'}
        for f in fs:
            if f['area_t'] in ENG:
                s=st[(f['area_t'],f['path'])]; s['c'].add(c['h']); s['add']+=f['add']; s['del']+=f['del']
                if hasW: s['cw'].add(c['h'])
                if hasW12: s['cw12'].add(c['h'])
                if hasO: s['co'].add(c['h'])
                if hasT: s['ct'].add(c['h'])
                if onlyE: s['only'].add(c['h'])
    rows=[]
    for (a,p),s in sorted(st.items(),key=lambda kv:(kv[0][0],-len(kv[1]['c']))):
        rows.append([a,p.replace('src/Reactor/',''),len(s['c']),s['add'],s['del'],len(s['cw']),len(s['cw12']),len(s['co']),len(s['only']),len(s['ct'])])
    # area-level (distinct commits)
    md.append(f'## F per engine file, {label}')
    table(['area','file (under src/Reactor/)','commits','added','deleted','also touched W*','also touched W1/W2','also touched O1','only E/tests/docs','also touched tests/Reactor.Tests'],rows)
    ar=collections.defaultdict(lambda:[set(),set(),set(),0])
    for c in commits:
        fs=[f for f in c['files'] if not f['ex']]; areas=set(f['area_t'] for f in fs)
        for a in set(areas)&set(ENG):
            ar[a][0].add(c['h']); ar['E*'][0].add(c['h'])
            if areas & set(WIN): ar[a][1].add(c['h']); ar['E*'][1].add(c['h'])
            if areas <= set(ENG)|{'T','D'}: ar[a][2].add(c['h']); ar['E*'][2].add(c['h'])
    md.append(f'## FA per engine area coupling, {label}')
    table(['area','commits','also touched W*','%','only E+tests+docs','%'],[[a,len(ar[a][0]),len(ar[a][1]),f'{100*len(ar[a][1])/max(1,len(ar[a][0])):.0f}%',len(ar[a][2]),f'{100*len(ar[a][2])/max(1,len(ar[a][0])):.0f}%'] for a in ENG+['E*']])
per_file(win,'window 2026-08-22..11-22')
per_file(cs,'whole history (path at time)')
# 4 renames
def ren_list(commits,label):
    rows=[]; cnt=collections.Counter(); cntE=0
    for c in commits:
        for f in c['files']:
            if (f['status'] or '').startswith('R'):
                cnt[c['h']]+=1
                ao,an=area(f['old']),area(f['path'])
                if ao in ENG or an in ENG or f['area_l'] in ENG or ao in WIN or an in WIN:
                    rows.append([c['date'],c['h'][:8],f['status'],f['old'],f['path'],ao,an,c['subject'][:70].replace('|','/')])
    md.append(f'## R renames, {label}: total rename entries {sum(cnt.values())} in {len(cnt)} commits; touching E or W (by old, new or lineage):')
    table(['date','commit','sim','old','new','area old','area new','subject'],sorted(rows))
ren_list(win,'window'); ren_list(cs,'whole history')
# splits: A files with >=40 lines added in E/W areas, with a same-commit file in same area/dir losing >=40 lines
def splits(commits,label):
    rows=[]
    for c in commits:
        fs=c['files']
        adds=[f for f in fs if (f['status'] or '').startswith('A') and f['add']>=40 and (f['area_l'] in ENG+WIN or f['area_t'] in ENG+WIN)]
        for a in adds:
            d=a['path'].rsplit('/',1)[0]
            donors=[f for f in fs if f is not a and not (f['status'] or '').startswith('A') and f['del']>=40 and (f['path'].rsplit('/',1)[0]==d or f['area_t']==a['area_t'])]
            if donors:
                dn=max(donors,key=lambda f:f['del'])
                rows.append([c['date'],c['h'][:8],a['path'].replace('src/Reactor/',''),a['add'],dn['path'].replace('src/Reactor/',''),dn['del'],a['area_t'],c['subject'][:60].replace('|','/')])
    md.append(f'## SP possible splits/extractions into new files (new file >=40 lines added while a same-dir or same-area file lost >=40 lines), {label}')
    table(['date','commit','new file','added','donor file','donor deleted','area','subject'],sorted(rows))
splits(win,'window'); splits(cs,'whole history')
# new/deleted files in E areas
def addel(commits,label):
    rows=[]
    for c in commits:
        for f in c['files']:
            st=(f['status'] or '')[:1]
            if st in 'AD' and f['area_t'] in ENG:
                rows.append([c['date'],c['h'][:8],st,f['path'].replace('src/Reactor/',''),f['add'] if st=='A' else f['del'],c['subject'][:60].replace('|','/')])
    md.append(f'## AD files added/deleted in E areas, {label}')
    table(['date','commit','A/D','file','lines','subject'],sorted(rows))
addel(win,'window'); addel(cs,'whole history (path at time)')
open(OUT+'/results.md','w',encoding='utf-8').write('\n'.join(str(x) for x in md))
print('ok',len(md))
