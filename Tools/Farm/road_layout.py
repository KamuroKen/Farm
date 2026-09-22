"""Road footprint in whole atlas tiles, with top-left map coordinates.

Based on the supplied route sketch. The lake approach follows the existing
north-west bank; the ring reserves a clear central island.
"""
RECTANGLES = [
    (24, 9, 37, 10), (24, 19, 37, 20),
    (24, 11, 25, 18), (36, 11, 37, 18),
    (21, 13, 23, 14), (38, 13, 58, 14),
    (30, 6, 31, 8), (30, 21, 31, 47),
    (24, 26, 38, 27), (38, 27, 39, 28),
    (39, 28, 40, 29), (40, 29, 41, 34),
    (16, 32, 29, 33),
]


def road_mask(pond=()):
    return {(x, y) for x0, y0, x1, y1 in RECTANGLES
            for x in range(x0, x1 + 1) for y in range(y0, y1 + 1)} - set(pond)


def update_saved_plan(root):
    import json
    path = root / 'Assets/Farm/Design/farm-layout.json'
    plan = json.loads(path.read_text())
    base = next(l for l in plan['tileLayers'] if l['name'] == '01 Grass and water')
    pond = {(t['x'] + 32, 23 - t['y']) for t in base['tiles'] if t['sprite'] == 'ground_water'}
    mask = road_mask(pond)
    tiles = []
    for x, y in sorted(mask):
        for q in range(4):
            right, bottom = q % 2, q // 2
            dx, dy = (1 if right else -1), (1 if bottom else -1)
            horizontal, vertical = (x+dx, y) in mask, (x, y+dy) in mask
            diagonal = (x+dx, y+dy) in mask
            if horizontal and vertical and not diagonal:
                sx, sy = (296 if right else 304), (24 if bottom else 32)
            else:
                sx = (256+right*8) if horizontal else (280 if right else 240)
                sy = (32+bottom*8) if vertical else (56 if bottom else 16)
            tiles.append(dict(sprite=f'path_{sx}_{sy}', x=x*2+right-64, y=47-y*2-bottom))
    layer = next(l for l in plan['tileLayers'] if l['name'] == '03 Earthen paths')
    layer['tiles'] = tiles
    path.write_text(json.dumps(plan, ensure_ascii=False, indent=2)+'\n')
    manifest = root / 'Assets/Farm/Design/road-layout.json'
    manifest.write_text(json.dumps(dict(tiles=tiles), indent=2)+'\n')
    # Validate a single connected route, an open island, and no water tiles.
    seen, todo = set(), [next(iter(mask))]
    while todo:
        p = todo.pop()
        if p in seen: continue
        seen.add(p)
        todo.extend(n for n in [(p[0]+1,p[1]),(p[0]-1,p[1]),(p[0],p[1]+1),(p[0],p[1]-1)] if n in mask and n not in seen)
    assert seen == mask
    assert not (mask & pond)
    assert all((x,y) not in mask for x in range(28,34) for y in range(12,17))
    assert all((root / f"Assets/Farm/Tiles/{t['sprite']}.asset").exists() for t in tiles)
    print(f'{len(mask)} connected road cells; {len(tiles)} quarter tiles; central island and water clear.')


if __name__ == '__main__':
    from pathlib import Path
    update_saved_plan(Path(__file__).resolve().parents[2])
