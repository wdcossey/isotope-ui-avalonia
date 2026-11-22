
import json, sys, collections
sys.path.insert(0, sys.argv[2])
from areas import area
cs=json.load(open(sys.argv[1],encoding='utf-8'))
for c in cs:
    rs=[f for f in c['files'] if f['status'] and f['status'][0]=='R']
    if len(rs)>=20:
        tops=collections.Counter((f['old'].split('/')[0]+' -> '+'/'.join(f['path'].split('/')[:2])) for f in rs)
        print(c['h'][:8], c['cdate'][:10], len(rs), 'of', len(c['files']), c['subject'][:80], dict(tops.most_common(4)))
# first/last appearance of top-level prefixes
first={}; last={}
for c in reversed(cs):
    for f in c['files']:
        t='/'.join(f['path'].split('/')[:2]) if f['path'].startswith('src/') else f['path'].split('/')[0]
        first.setdefault(t,c['cdate'][:10]); last[t]=c['cdate'][:10]
for t in sorted(first, key=lambda t: first[t]):
    if t in ('Duct','Reactor','Duct.Tests','Reactor.Tests','src/Reactor','tests','docs','samples'):
        print('prefix',t,first[t],last[t])
