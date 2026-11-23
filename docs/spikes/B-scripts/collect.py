
import subprocess, json, re, sys, hashlib
import os
U = os.environ.get('UPSTREAM') or os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..', '..', 'upstream'))
OUT = sys.argv[1]
REF = 'origin/main'
BASE = ['git','-C',U,'-c','diff.renameLimit=50000','-c','core.quotepath=off','log',REF,'--first-parent','--diff-merges=first-parent','-M']
FMT = '--format=@@C\t%H\t%cI\t%aI\t%P\t%an\t%s'
def run(extra):
    return subprocess.run(BASE+extra+[FMT],capture_output=True,check=True).stdout.decode('utf-8','replace')
def split_rename(p):
    m = re.match(r'^(.*)\{(.*) => (.*)\}(.*)$', p)
    if m:
        pre,a,b,post = m.groups()
        old=(pre+a+post).replace('//','/'); new=(pre+b+post).replace('//','/')
        return old,new
    if ' => ' in p:
        a,b = p.split(' => ',1); return a,b
    return None,p
commits = {}; order=[]
cur=None
for line in run(['--numstat']).splitlines():
    if line.startswith('@@C\t'):
        _,h,cd,ad,par,an,subj = line.split('\t',6)
        # No personal data in outputs: authors are kept only as a short hash (for distinct counts), and merge
        # subjects lose their source branch, which often carries a username.
        an = hashlib.sha256(an.encode('utf-8')).hexdigest()[:12]
        subj = re.sub(r'^(Merge pull request #\d+) from \S+', r'\1', subj)
        cur={'h':h,'cdate':cd,'adate':ad,'parents':par.split(),'author':an,'subject':subj,'files':{}}
        commits[h]=cur; order.append(h); continue
    if not line.strip() or cur is None: continue
    a,d,p = line.split('\t',2)
    old,new = split_rename(p)
    binary = (a=='-')
    cur['files'][new]={'path':new,'old':old,'add':0 if binary else int(a),'del':0 if binary else int(d),'binary':binary,'status':None}
cur=None
for line in run(['--name-status']).splitlines():
    if line.startswith('@@C\t'):
        cur=commits[line.split('\t')[1]]; continue
    if not line.strip() or cur is None: continue
    parts=line.split('\t')
    st=parts[0]
    if st[0] in 'RC':
        old,new=parts[1],parts[2]
    else:
        old,new=None,parts[1]
    f=cur['files'].get(new)
    if f is None:
        f={'path':new,'old':old,'add':0,'del':0,'binary':False,'status':None}; cur['files'][new]=f
    f['status']=st; 
    if old: f['old']=old
out=[]
for h in order:
    c=commits[h]; c['files']=list(c['files'].values()); out.append(c)
json.dump(out,open(OUT,'w',encoding='utf-8'))
print(len(out), sum(len(c['files']) for c in out))
