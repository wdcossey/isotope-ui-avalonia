
import json, sys, collections
sys.path.insert(0, sys.argv[2])
from areas import area
cs=json.load(open(sys.argv[1],encoding='utf-8'))
W='2026-08-22'
tot=collections.Counter(); win=collections.Counter(); bin_=collections.Counter()
for c in cs:
    for f in c['files']:
        n=f['add']+f['del']; tot[f['path']]+=n
        if c['cdate'][:10]>=W: win[f['path']]+=n
        if f['binary']: bin_[f['path'].rsplit('.',1)[-1]]+=1
ext_tot=collections.Counter()
for p,n in tot.items(): ext_tot[p.rsplit('/',1)[-1].rsplit('.',1)[-1] if '.' in p.rsplit('/',1)[-1] else '(none)']+=n
print('ALL total lines', sum(tot.values()), 'window', sum(win.values()))
print('TOP history'); [print(n,area(p),p) for p,n in tot.most_common(40)]
print('TOP window'); [print(n,area(p),p) for p,n in win.most_common(40)]
print('EXT'); [print(n,e) for e,n in ext_tot.most_common(25)]
print('BIN', bin_.most_common(10))
