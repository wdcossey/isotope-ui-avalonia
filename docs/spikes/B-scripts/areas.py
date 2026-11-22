
import re
def area(p):
    if p is None: return None
    if p.startswith('src/Reactor/'):
        r=p[len('src/Reactor/'):]
        if re.fullmatch(r'Core/RenderContext(\..+)?\.cs',r): return 'E1'
        if re.fullmatch(r'Core/ChildReconciler(\..+)?\.cs',r) or re.fullmatch(r'Core/Internal/KeyedListDiff(\..+)?\.cs',r): return 'E2'
        if r=='Yoga/FlexPanel.cs' or re.fullmatch(r'Yoga/FlexPanel\..+\.cs',r): return 'W5'
        if r.startswith('Yoga/'): return 'E3'
        if r.startswith('Hooks/'): return 'E4'
        if r.startswith('Data/') or r.startswith('Controls/Validation/'): return 'E5'
        if re.fullmatch(r'Core/Reconciler(\..+)?\.cs',r) or re.fullmatch(r'Core/V1Protocol/Reconciler\..+\.cs',r): return 'W1'
        if r.startswith('Core/V1Protocol/') or r=='Core/V1HandlerRegistry.cs': return 'W2'
        if re.fullmatch(r'Core/Element(\..+)?\.cs',r) or r.startswith('Elements/'): return 'W3'
        if r.startswith('Hosting/'): return 'W4'
        return 'O1'
    if p.startswith('tests/'): return 'T'
    if p.startswith('docs/'): return 'D'
    if p.startswith('src/'): return 'S'
    return 'X'
AREAS=['E1','E2','E3','E4','E5','W1','W2','W3','W4','W5','O1','T','D','S','X']
