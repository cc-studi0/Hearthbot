import json, re

with open('cards.json', 'r', encoding='utf-8') as f:
    cards = json.load(f)

def clean(text):
    if not text: return ''
    return re.sub(r'<[^>]+>|\[x\]|\n|\xa0', ' ', text).strip()

card_map = {c['id']: c for c in cards}

# === 英雄牌战吼效果 ===
hero_effects = []
heroes = [c for c in cards if c['type'] == 'HERO' and c.get('collectible') and c.get('cost', 0) > 0 and c.get('set', '') != 'HERO_SKINS']

for h in sorted(heroes, key=lambda x: x['id']):
    cid = h['id']
    t = clean(h.get('text', ''))
    code = None
    cat = ''

    # Deal X damage to all enemy minions
    m = re.search(r'Deal (\d+) damage to all enemy minion', t)
    if m:
        d = m.group(1)
        code = f'(b,s,t)=>{{foreach(var e in b.EnemyMinions.ToArray())Dmg(b,e,{d});}}'
        cat = 'AoE敌方随从'

    # Deal X damage to all enemies (minions + hero)
    if not code:
        m = re.search(r'Deal (\d+) damage to all enem', t)
        if m:
            d = m.group(1)
            code = f'(b,s,t)=>{{foreach(var e in b.EnemyMinions.ToArray())Dmg(b,e,{d});if(b.EnemyHero!=null)Dmg(b,b.EnemyHero,{d});}}'
            cat = 'AoE敌方'

    # Deal X damage to all minions
    if not code:
        m = re.search(r'Deal (\d+) damage to all minion', t)
        if m:
            d = m.group(1)
            code = f'(b,s,t)=>{{foreach(var e in b.EnemyMinions.ToArray())Dmg(b,e,{d});foreach(var e in b.FriendMinions.ToArray())Dmg(b,e,{d});}}'
            cat = 'AoE全部随从'

    # Summon X/Y with keywords
    if not code:
        m = re.search(r'Summon (?:a |an? |two |2 |three |3 )?([\d]*)\s*(\d+)/(\d+)', t)
        if m:
            count_str = m.group(1)
            a, hp = m.group(2), m.group(3)
            count = int(count_str) if count_str else 1
            if 'two' in t.lower() or '2 ' in t: count = max(count, 2)
            if 'three' in t.lower() or '3 ' in t: count = max(count, 3)
            rush = '.HasRush=true' if 'Rush' in t else ''
            charge = '.HasCharge=true' if 'Charge' in t else ''
            stealth = '.IsStealth=true' if 'Stealth' in t else ''
            taunt = '.IsTaunt=true' if 'Taunt' in t else ''
            parts = []
            for i in range(count):
                p = f'if(b.FriendMinions.Count<7){{var m{i}=new SimEntity{{Atk={a},Health={hp},MaxHealth={hp},IsFriend=true,IsTired=true}};'
                if rush: p += f'm{i}{rush};'
                if charge: p += f'm{i}{charge};m{i}.IsTired=false;'
                if stealth: p += f'm{i}{stealth};'
                if taunt: p += f'm{i}{taunt};'
                p += f'b.FriendMinions.Add(m{i});}}'
                parts.append(p)
            code = f'(b,s,t)=>{{{" ".join(parts)}}}'
            cat = '召唤'

    # Summon a X/Y Water Elemental (no explicit stats in text)
    if not code and 'Summon a 3/6' in t:
        code = '(b,s,t)=>{if(b.FriendMinions.Count<7)b.FriendMinions.Add(new SimEntity{Atk=3,Health=6,MaxHealth=6,IsFriend=true,IsTired=true});}'
        cat = '召唤'

    # Equip weapon
    if not code:
        m = re.search(r'Equip a (\d+)/(\d+)', t)
        if m:
            wa, wh = m.group(1), m.group(2)
            ls = '.IsLifeSteal=true' if 'Lifesteal' in t else ''
            code = f'(b,s,t)=>{{b.FriendWeapon=new SimEntity{{Atk={wa},Health={wh},MaxHealth={wh},IsFriend=true}};if(b.FriendHero!=null)b.FriendHero.Atk={wa};}}'
            cat = '装备武器'

    # Draw X cards
    if not code:
        m = re.search(r'Draw (\d+) card', t)
        if m:
            n = m.group(1)
            code = f'(b,s,t)=>{{b.FriendCardDraw+={n};}}'
            cat = '抽牌'
        elif re.search(r'Draw a card', t):
            code = '(b,s,t)=>{b.FriendCardDraw+=1;}'
            cat = '抽牌'

    # Destroy all minions with 5+ Attack
    if not code and 'Destroy all minions with 5 or more Attack' in t:
        code = '(b,s,t)=>{b.FriendMinions.RemoveAll(m=>m.Atk>=5);b.EnemyMinions.RemoveAll(m=>m.Atk>=5);}'
        cat = '消灭5攻以上'

    # Remove/destroy all enemy minions
    if not code and ('remove all enemy minions' in t.lower() or 'Make all minions disappear' in t):
        code = '(b,s,t)=>{b.FriendMinions.Clear();b.EnemyMinions.Clear();}'
        cat = '消灭全部'

    # Destroy enemy minion with most Attack
    if not code and 'Destroy the enemy minion with the most Attack' in t:
        code = '(b,s,t)=>{if(b.EnemyMinions.Count>0){var mx=b.EnemyMinions.OrderByDescending(m=>m.Atk).First();mx.Health=0;}}'
        cat = '消灭最高攻'

    # Destroy 1 random enemy minion
    if not code and 'Destroy 1 random enemy minion' in t:
        code = '(b,s,t)=>{if(b.EnemyMinions.Count>0)b.EnemyMinions[0].Health=0;}'
        cat = '消灭随机敌方'

    # Return all minions to hands
    if not code and 'Return all minions' in t:
        code = '(b,s,t)=>{b.FriendMinions.Clear();b.EnemyMinions.Clear();}'
        cat = '回手全部'

    # Combined effects: AoE + summon/draw/weapon
    # AV_206: Deal 2 to all enemies + Equip 2/5
    if not code and cid == 'AV_206':
        code = '(b,s,t)=>{foreach(var e in b.EnemyMinions.ToArray())Dmg(b,e,2);if(b.EnemyHero!=null)Dmg(b,b.EnemyHero,2);b.FriendWeapon=new SimEntity{Atk=2,Health=5,MaxHealth=5,IsFriend=true};if(b.FriendHero!=null)b.FriendHero.Atk=2;}'
        cat = 'AoE+武器'

    # AV_316: Deal 3 to all minions + Draw 3
    if not code and cid == 'AV_316':
        code = '(b,s,t)=>{foreach(var e in b.EnemyMinions.ToArray())Dmg(b,e,3);foreach(var e in b.FriendMinions.ToArray())Dmg(b,e,3);b.FriendCardDraw+=3;}'
        cat = 'AoE+抽牌'

    # SC_004: Summon two 2/5 + Deal 3 to all enemies
    if not code and cid == 'SC_004':
        code = '(b,s,t)=>{foreach(var e in b.EnemyMinions.ToArray())Dmg(b,e,3);if(b.EnemyHero!=null)Dmg(b,b.EnemyHero,3);for(int i=0;i<2;i++)if(b.FriendMinions.Count<7)b.FriendMinions.Add(new SimEntity{Atk=2,Health=5,MaxHealth=5,IsFriend=true,IsTired=true});}'
        cat = 'AoE+召唤'

    # WON_092h: Draw 4 cards
    if not code and cid == 'WON_092h':
        code = '(b,s,t)=>{b.FriendCardDraw+=4;}'
        cat = '抽牌'

    # AV_205: Draw a card
    if not code and cid == 'AV_205':
        code = '(b,s,t)=>{b.FriendCardDraw+=1;}'
        cat = '抽牌'

    if code:
        hero_effects.append((cid, cat, code))

# === 地标牌效果 ===
loc_effects = []
locations = [c for c in cards if c['type'] == 'LOCATION' and c.get('collectible')]

for loc in sorted(locations, key=lambda x: x['id']):
    cid = loc['id']
    t = clean(loc.get('text', ''))
    code = None
    cat = ''

    # Give a minion +X/+Y and draw
    m = re.search(r'Give (?:a |your )?(?:friendly )?minion \+(\d+)/\+(\d+)', t)
    if m:
        a, h = m.group(1), m.group(2)
        extra = ''
        if 'draw a card' in t.lower():
            extra = 'b.FriendCardDraw+=1;'
        code = f'(b,s,t)=>{{if(t!=null){{t.Atk+={a};t.Health+={h};t.MaxHealth+={h};}}{extra}}}'
        cat = 'buff'

    # Give +X Attack
    if not code:
        m = re.search(r'Give (?:a |your )?(?:friendly )?minion \+(\d+) Attack', t)
        if m:
            a = m.group(1)
            code = f'(b,s,t)=>{{if(t!=null)t.Atk+={a};}}'
            cat = 'buff攻击'

    # Deal X damage to a minion and give +Y Attack
    if not code:
        m = re.search(r'Deal (\d+) damage to a\s+minion and give it \+(\d+) Attack', t)
        if m:
            d, a = m.group(1), m.group(2)
            code = f'(b,s,t)=>{{if(t!=null){{Dmg(b,t,{d});t.Atk+={a};}}}}'
            cat = '伤害+buff'

    # Deal X damage to all enemies
    if not code:
        m = re.search(r'Deal (\d+) damage to all enem', t)
        if m:
            d = m.group(1)
            code = f'(b,s,t)=>{{foreach(var e in b.EnemyMinions.ToArray())Dmg(b,e,{d});if(b.EnemyHero!=null)Dmg(b,b.EnemyHero,{d});}}'
            cat = 'AoE敌方'

    # Deal X damage to a random enemy minion
    if not code:
        m = re.search(r'Deal (\d+) damage to a random enemy minion', t)
        if m:
            d = m.group(1)
            code = f'(b,s,t)=>{{if(b.EnemyMinions.Count>0)Dmg(b,b.EnemyMinions[0],{d});}}'
            cat = '随机伤害'

    # Deal X damage to a random enemy (including hero)
    if not code:
        m = re.search(r'Deal (\d+) damage to a random enemy', t)
        if m:
            d = m.group(1)
            code = f'(b,s,t)=>{{if(b.EnemyMinions.Count>0)Dmg(b,b.EnemyMinions[0],{d});else if(b.EnemyHero!=null)Dmg(b,b.EnemyHero,{d});}}'
            cat = '随机伤害'

    # Deal X damage randomly split among all enemies
    if not code:
        m = re.search(r'Deal (\d+) damage randomly split among all enem', t)
        if m:
            d = m.group(1)
            code = f'(b,s,t)=>{{var ts=new System.Collections.Generic.List<SimEntity>(b.EnemyMinions);if(b.EnemyHero!=null)ts.Add(b.EnemyHero);for(int i=0;i<{d}&&ts.Count>0;i++)Dmg(b,ts[i%ts.Count],1);}}'
            cat = '分散伤害'

    # Freeze a minion + Summon
    if not code and 'Freeze' in t:
        m = re.search(r'Summon a (\d+)/(\d+)', t)
        if m:
            a, h = m.group(1), m.group(2)
            code = f'(b,s,t)=>{{if(t!=null)t.IsFrozen=true;if(b.FriendMinions.Count<7)b.FriendMinions.Add(new SimEntity{{Atk={a},Health={h},MaxHealth={h},IsFriend=true,IsTired=true}});}}'
            cat = '冰冻+召唤'
        else:
            code = '(b,s,t)=>{if(t!=null)t.IsFrozen=true;}'
            cat = '冰冻'

    # Summon X/Y
    if not code:
        m = re.search(r'Summon (?:a |an? |two |2 )?([\d]*)\s*(\d+)/(\d+)', t)
        if m:
            a, h = m.group(2), m.group(3)
            count = 1
            if 'two' in t.lower() or '2 ' in t: count = 2
            rush = 'HasRush=true,' if 'Rush' in t else ''
            charge = 'HasCharge=true,IsTired=false,' if 'Charge' in t else ''
            parts = []
            for i in range(count):
                parts.append(f'if(b.FriendMinions.Count<7)b.FriendMinions.Add(new SimEntity{{Atk={a},Health={h},MaxHealth={h},{rush}{charge}IsFriend=true,IsTired=true}});')
            code = f'(b,s,t)=>{{{" ".join(parts)}}}'
            if charge:
                code = code.replace('IsTired=true', 'IsTired=false')
            cat = '召唤'

    # Restore X Health to all friendly characters
    if not code:
        m = re.search(r'Restore #?(\d+) Health to all friendly', t)
        if m:
            h = m.group(1)
            code = f'(b,s,t)=>{{foreach(var m in b.FriendMinions)m.Health=System.Math.Min(m.MaxHealth,m.Health+{h});if(b.FriendHero!=null)b.FriendHero.Health=System.Math.Min(b.FriendHero.MaxHealth,b.FriendHero.Health+{h});}}'
            cat = '治疗全体'

    # Set a minion's Attack and Health to 3
    if not code and "Set a minion's Attack and Health to 3" in t:
        code = '(b,s,t)=>{if(t!=null){t.Atk=3;t.Health=3;t.MaxHealth=3;}}'
        cat = '设置属性'

    # Give your minions Rush
    if not code and 'Give your minions Rush' in t:
        code = '(b,s,t)=>{foreach(var m in b.FriendMinions)m.HasRush=true;}'
        cat = 'Rush全体'

    # Draw a card
    if not code:
        m = re.search(r'Draw (?:a |1 )card', t)
        if m:
            code = '(b,s,t)=>{b.FriendCardDraw+=1;}'
            cat = '抽牌'

    # Destroy a friendly minion to summon X/Y with Rush
    if not code:
        m = re.search(r'Destroy a friendly minion to summon a (\d+)/(\d+)', t)
        if m:
            a, h = m.group(1), m.group(2)
            code = f'(b,s,t)=>{{if(t!=null&&t.IsFriend){{t.Health=0;if(b.FriendMinions.Count<7)b.FriendMinions.Add(new SimEntity{{Atk={a},Health={h},MaxHealth={h},HasRush=true,IsFriend=true,IsTired=true}});}}}}'
            cat = '消灭+召唤'

    if code:
        loc_effects.append((cid, cat, code))

# 输出
with open('hero_loc_effects.txt', 'w', encoding='utf-8') as f:
    f.write("// === RegisterHeroes ===\n")
    for cid, cat, code in hero_effects:
        f.write(f"            db.Register(C.{cid},EffectTrigger.Battlecry,{code}); // {cat}\n")
    f.write(f"\n// === RegisterLocations ===\n")
    for cid, cat, code in loc_effects:
        f.write(f"            db.Register(C.{cid},EffectTrigger.Spell,{code}); // {cat}\n")

print(f"英雄牌效果: {len(hero_effects)}/45")
print(f"地标牌效果: {len(loc_effects)}/52")
