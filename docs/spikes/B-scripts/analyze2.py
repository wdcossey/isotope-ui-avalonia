
import json, sys, re, collections, datetime, statistics, subprocess
sys.path.insert(0, sys.argv[2])
from areas import area, AREAS
cs=json.load(open(sys.argv[1],encoding='utf-8'))
OUT=sys.argv[3]
import os
U = os.environ.get('UPSTREAM') or os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..', '..', 'upstream'))
def git(*a): return subprocess.run(['git','-C',U,'-c','core.quotepath=off','-c','diff.renameLimit=50000']+list(a),capture_output=True,check=True).stdout.decode('utf-8','replace')
EXCL=[r'(^|/)package-lock\.json$', r'/traces/', r'\.(etl|nettrace)$', r'/Monaco/Assets/', r'\.api\.txt$',
      r'(Md4cGenerated|YogaGenerated)/', r'Md4cFixtures/spec\.txt$', r'reactor-search-index\.json$', r'/Baselines/.*\.json$', r'^docs/specs/tasks/.*\.json$']
EX=re.compile('|'.join(EXCL))
ENG=['E1','E2','E3','E4','E5']; WIN=['W1','W2','W3','W4','W5']
for c in cs:
    c['date']=datetime.date.fromisoformat(c['cdate'][:10])
    for f in c['files']:
        f['a']=area(f['path']); f['ex']=bool(EX.search(f['path'])); f['n']=f['add']+f['del']
W0=datetime.date(2026,8,22); W1d=datetime.date(2026,11,22)
win=[c for c in cs if W0<=c['date']<=W1d]
md=[]
def table(hdr,rows):
    md.append('| '+' | '.join(hdr)+' |'); md.append('|'+'---|'*len(hdr))
    for r in rows: md.append('| '+' | '.join(str(x) for x in r)+' |')
    md.append('')
# a) coupling detail
for label,commits in (('window',win),('whole history',cs)):
    co=collections.Counter(); clean=collections.Counter(); tot=collections.Counter(); chlog=0; nE=0
    rows=[]
    for c in commits:
        fs=[f for f in c['files'] if not f['ex']]
        ar=set(f['a'] for f in fs)
        es=ar & set(ENG)
        if not es: continue
        nE+=1
        for a in ar - set(ENG): co[a]+=1
        if any(f['path']=='CHANGELOG.md' for f in fs): chlog+=1
        other_code = ar & (set(WIN)|{'O1','S'})
        for a in list(es)+['E*']:
            tot[a]+=1
            if not other_code: clean[a]+=1
        if label=='window':
            eL=sum(f['n'] for f in fs if f['a'] in ENG); wL=sum(f['n'] for f in fs if f['a'] in WIN); oL=sum(f['n'] for f in fs if f['a']=='O1')
            rows.append([c['date'],c['h'][:8],'+'.join(sorted(es)),eL,wL,oL,len(fs),'+'.join(sorted(other_code)) or '-',c['subject'][:75].replace('|','/')])
    md.append(f'## C co-touched areas in commits that touch E ({label}, {nE} commits; CHANGELOG.md touched in {chlog})')
    table(['area','commits also touching it'],sorted(co.items(),key=lambda kv:-kv[1]))
    table(['engine area','commits','no other src code (no W*, O1, S)','%'],[[a,tot[a],clean[a],f'{100*clean[a]/tot[a]:.0f}%'] for a in ENG+['E*'] if tot[a]])
    if rows:
        md.append('## CL every window commit touching E')
        table(['date','commit','E areas','E lines','W lines','O1 lines','files','other src code areas','subject'],sorted(rows))
# b) landing
def landing(commits,label):
    pr=sum(1 for c in commits if re.search(r'\(#\d+\)\s*$',c['subject']))
    gm=sum(1 for c in commits if c['subject'].startswith('Merge pull request #'))
    ado=sum(1 for c in commits if c['subject'].startswith('Merged PR '))
    merges=sum(1 for c in commits if len(c['parents'])>1)
    sizes=[sum(f['n'] for f in c['files'] if not f['ex']) for c in commits]
    nf=[len([f for f in c['files'] if not f['ex']]) for c in commits]
    srcR=[sum(f['n'] for f in c['files'] if not f['ex'] and f['a'] in ENG+WIN+['O1']) for c in commits]
    srcR_nz=[x for x in srcR if x>0]
    days=collections.Counter(c['date'] for c in commits)
    span=(max(days)-min(days)).days+1
    authors=collections.Counter(c['author'] for c in commits)
    q=lambda xs,p: sorted(xs)[int(p*(len(xs)-1))]
    md.append(f'## L landing, {label}')
    table(['metric','value'],[
      ['first-parent commits',len(commits)],['merge commits on first-parent',merges],['subject ends with (#NNN) (GitHub squash)',pr],['"Merge pull request #" subjects',gm],['"Merged PR NNN" (Azure DevOps) subjects',ado],
      ['median lines changed per commit (excl. bulk)',statistics.median(sizes)],['p75 / p90 lines',f'{q(sizes,.75)} / {q(sizes,.9)}'],['mean lines',round(statistics.mean(sizes))],
      ['median files per commit',statistics.median(nf)],['commits touching src/Reactor',len(srcR_nz)],['median src/Reactor lines in those',statistics.median(srcR_nz) if srcR_nz else '-'],
      ['calendar days',span],['days with >=1 commit',len(days)],['commits per calendar day',round(len(commits)/span,2)],['max commits in a day',max(days.values())],
      ['distinct authors',len(authors)]])
landing(win,'window 2026-08-22..11-22'); landing(cs,'whole history')
# PR numbers range in window
nums=[int(m.group(1)) for c in win for m in [re.search(r'\(#(\d+)\)\s*$',c['subject'])] if m]
md.append(f'PR numbers in window subjects: min #{min(nums)}, max #{max(nums)}, {len(nums)} landed of {max(nums)-min(nums)+1} numbers in range (issues and PRs share the counter)'); md.append('')
# c) O1 breakdown window + T split
o1=collections.defaultdict(lambda:[set(),0]); t=collections.defaultdict(lambda:[set(),0])
for c in win:
    for f in c['files']:
        if f['ex']: continue
        if f['a']=='O1':
            k='/'.join(f['path'].split('/')[2:4]) if f['path'].count('/')>=4 else f['path'].split('/')[2]
            if not k.startswith('Core'): k=f['path'].split('/')[2]
            o1[k][0].add(c['h']); o1[k][1]+=f['n']
        if f['a']=='T':
            k='/'.join(f['path'].split('/')[:2]); t[k][0].add(c['h']); t[k][1]+=f['n']
md.append('## O1 breakdown (window)'); table(['sub-area','commits','lines'],sorted([[k,len(v[0]),v[1]] for k,v in o1.items()],key=lambda r:-r[2]))
md.append('## T breakdown (window)'); table(['test project','commits','lines'],sorted([[k,len(v[0]),v[1]] for k,v in t.items()],key=lambda r:-r[2])[:15])
# top O1/Core files window
of=collections.defaultdict(lambda:[set(),0])
for c in win:
    for f in c['files']:
        if not f['ex'] and f['a']=='O1': of[f['path']][0].add(c['h']); of[f['path']][1]+=f['n']
md.append('## O1 top files (window)'); table(['file','commits','lines'],sorted([[k.replace('src/Reactor/',''),len(v[0]),v[1]] for k,v in of.items()],key=lambda r:-r[1])[:20])
# d) baseline delta
BASE='28a87225'; NEW='14172dce'
ns=git('diff','-M','--numstat',BASE,NEW)
st=git('diff','-M','--name-status',BASE,NEW)
dd=collections.defaultdict(lambda:[0,0,0]); files=[]
for line in ns.splitlines():
    a,d,p=line.split('\t',2)
    if ' => ' in p:
        m=re.match(r'^(.*)\{(.*) => (.*)\}(.*)$',p); p=(m.group(1)+m.group(3)+m.group(4)).replace('//','/') if m else p.split(' => ')[1]
    a=0 if a=='-' else int(a); d=0 if d=='-' else int(d)
    ar=area(p); dd[ar][0]+=1; dd[ar][1]+=a; dd[ar][2]+=d; files.append((ar,p,a,d))
md.append(f'## B baseline delta {BASE}..{NEW}: commits')
md.append('\x60\x60\x60'); md.append(git('log','--first-parent','--format=%h %cs %s',f'{BASE}..{NEW}').strip()); md.append('\x60\x60\x60')
md.append('name-status:'); md.append('\x60\x60\x60'); md.append(st.strip()); md.append('\x60\x60\x60')
table(['area','files','added','deleted'],[[a,*dd[a]] for a in AREAS if a in dd])
table(['area','file','added','deleted'],sorted(files))
# e) weekly pull simulation restricted to E paths
EPATHS=['src/Reactor/Core/RenderContext*.cs','src/Reactor/Core/ChildReconciler*.cs','src/Reactor/Core/Internal/KeyedListDiff*.cs','src/Reactor/Yoga','src/Reactor/Hooks','src/Reactor/Data','src/Reactor/Controls/Validation']
EXC=[':(exclude)src/Reactor/Yoga/FlexPanel.cs']
def rev_before(d):
    return git('rev-list','-1','--first-parent',f'--before={d}T00:00:00-08:00','origin/main').strip()
mon=datetime.date(2026,8,24)
rows=[]; tf=0; tl=0; nz=[]
prev=rev_before(mon.isoformat())
while mon<=datetime.date(2026,11,16):
    nxt=mon+datetime.timedelta(days=7)
    r=rev_before(nxt.isoformat()) if nxt<=datetime.date(2026,11,23) else 'origin/main'
    if nxt==datetime.date(2026,11,23): r=git('rev-parse','origin/main').strip()
    out=git('diff','-M','--numstat',prev,r,'--',*[f':(glob){p}' if '*' in p else p for p in EPATHS],*EXC)
    fl=[l.split('\t') for l in out.splitlines() if l.strip()]
    n=sum(int(a)+int(d) for a,d,_ in fl)
    ncommits=len(git('rev-list','--first-parent',f'{prev}..{r}','--',*[f':(glob){p}' if '*' in p else p for p in EPATHS],*EXC).split())
    y,w,_=mon.isocalendar()
    rows.append([f'{y}-W{w:02d}',prev[:8],r[:8],ncommits,len(fl),sum(int(a) for a,d,_ in fl),sum(int(d) for a,d,_ in fl),'; '.join(f'{p.replace("src/Reactor/","")} ({int(a)+int(d)})' for a,d,p in fl)])
    tf+=len(fl); tl+=n; nz.append(n)
    prev=r; mon=nxt
md.append('## P weekly pull simulation: git diff -M --numstat <last mainline commit before Monday> <last before next Monday> restricted to E paths')
table(['ISO week','from','to','commits touching E','E files changed','added','deleted','files (lines)'],rows)
md.append(f'weeks={len(rows)} weeks with E change={sum(1 for x in nz if x)} median lines/week={statistics.median(nz)} mean={round(statistics.mean(nz))} max={max(nz)} total files-weeks={tf}'); md.append('')
# WinUI refs in E files at origin/main and line counts
pat=r'Microsoft\.UI|Windows\.(UI|Foundation|System|Graphics|ApplicationModel)|WinRT|DispatcherQueue|UIElement|FrameworkElement|DependencyObject|DependencyProperty'
lsf=git('ls-tree','-r','--name-only','origin/main','src/Reactor').splitlines()
ef=[p for p in lsf if area(p) in ENG]
rows=[]
for p in ef:
    txt=git('show',f'origin/main:{p}')
    nl=txt.count('\n'); nw=len([l for l in txt.splitlines() if re.search(pat,l)])
    rows.append([area(p),p.replace('src/Reactor/',''),nl,nw])
md.append('## WR WinUI-reference lines per E file at origin/main (regex in winui_regex.txt)')
table(['area','file','lines','lines matching WinUI regex'],rows)
md.append(f'E files: {len(ef)}, lines {sum(r[2] for r in rows)}, files with 0 matches: {sum(1 for r in rows if r[3]==0)} ({sum(r[2] for r in rows if r[3]==0)} lines)')
open(OUT+'/winui_regex.txt','w').write(pat)
open(OUT+'/results2.md','w',encoding='utf-8').write('\n'.join(str(x) for x in md))
print('ok')
