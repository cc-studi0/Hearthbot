import json, re

with open('cards.json', 'r', encoding='utf-8') as f:
    cards = json.load(f)
with open('BotMain/AI/CardEffectDB.cs', 'r', encoding='utf-8') as f:
    existing = f.read()

registered = set(re.findall(r'C\.(\w+)', existing))

def clean(text):
    if not text: return ''
    return re.sub(r'<[^>]+>|\[x\]|\n|\xa0', ' ', text).strip()

out = open('hero_loc_data.txt', 'w', encoding='utf-8')

heroes = [c for c in cards if c['type'] == 'HERO' and c.get('collectible') and c.get('cost', 0) > 0 and c.get('set', '') != 'HERO_SKINS']
out.write(f'=== 可打出英雄牌: {len(heroes)} ===\n')
for h in sorted(heroes, key=lambda x: x['id']):
    reg = 'Y' if h['id'] in registered else 'N'
    text = clean(h.get('text', ''))
    mechs = h.get('mechanics', [])
    out.write(f"  {h['id']} cost={h.get('cost',0)} armor={h.get('armor',0)} reg={reg} mechs={mechs}\n")
    out.write(f"    {text[:120]}\n")

locations = [c for c in cards if c['type'] == 'LOCATION' and c.get('collectible')]
out.write(f'\n=== 地标牌: {len(locations)} ===\n')
for loc in sorted(locations, key=lambda x: x['id']):
    reg = 'Y' if loc['id'] in registered else 'N'
    text = clean(loc.get('text', ''))
    out.write(f"  {loc['id']} cost={loc.get('cost',0)} health={loc.get('health',0)} reg={reg}\n")
    out.write(f"    {text[:120]}\n")

out.close()
print('done')
