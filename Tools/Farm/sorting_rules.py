"""Shared farm category orders; preserve foreground composition using dense ranks."""
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]


def normalize(plan):
    rules = json.loads((ROOT / 'Assets/Farm/Design/sorting-rules.json').read_text(encoding='utf-8'))
    orders = {s: c['order'] for c in rules['categories'] for s in c['sprites']}
    foreground = list(plan['groups'])
    grouped = {g['name'] for g in plan['groups']}
    for group in plan['groups']:
        group.setdefault('depthOrder', group['sortingOrder'])
    for obj in plan['objects']:
        if obj['group'] in grouped:
            continue  # Building part orders are local to their SortingGroup.
        order = orders[obj['sprite']]
        if order >= rules['foregroundStart']:
            obj.setdefault('depthOrder', obj['order'])
            foreground.append(obj)
        else:
            obj['order'] = order
    ranks = {old: rules['foregroundStart'] + i for i, old in enumerate(sorted({x['depthOrder'] for x in foreground}))}
    for item in foreground:
        item['sortingOrder' if 'sortingOrder' in item else 'order'] = ranks[item['depthOrder']]
    tile_orders = {x['name']: x['order'] for x in rules['tileLayers']}
    for layer in plan['tileLayers']:
        layer['order'] = tile_orders[layer['name']]
    plan['sortingVersion'] = 1
    return plan


if __name__ == '__main__':
    path = ROOT / 'Assets/Farm/Design/farm-layout.json'
    plan = json.loads(path.read_text(encoding='utf-8'))
    path.write_text(json.dumps(normalize(plan), ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
