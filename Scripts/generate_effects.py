import json, re

with open('cards.json', 'r', encoding='utf-8') as f:
    cards = json.load(f)
with open('BotMain/AI/CardEffectDB.cs', 'r', encoding='utf-8') as f:
    existing = f.read()

registered = set(re.findall(r'C\.(\w+)', existing))
card_map = {c['id']: c for c in cards}

def clean(text):
    if not text: return ''
    return re.sub(r'<[^>]+>|\[x\]|\n', ' ', text).strip()

def match_spell_effect(text):
    """匹配法术/战吼效果文本，返回 (category, code) 或 None"""
    t = clean(text)

    # 单体伤害
    m = re.search(r'[Dd]eal \$?(\d+) damage to a', t)
    if m and 'all' not in t.lower().split('damage')[0]:
        d = m.group(1)
        return ('单体伤害', f'(b,s,t)=>{{if(t!=null)Dmg(b,t,{d}+SP(b));}}')

    # AoE敌方随从
    m = re.search(r'[Dd]eal \$?(\d+) damage to all enem', t)
    if m:
        d = m.group(1)
        return ('AoE敌方', f'(b,s,t)=>{{foreach(var e in b.EnemyMinions.ToArray())Dmg(b,e,{d}+SP(b));if(b.EnemyHero!=null)Dmg(b,b.EnemyHero,{d}+SP(b));}}')

    # AoE全部随从
    m = re.search(r'[Dd]eal \$?(\d+) damage to all minion', t)
    if m:
        d = m.group(1)
        return ('AoE全部随从', f'(b,s,t)=>{{foreach(var e in b.EnemyMinions.ToArray())Dmg(b,e,{d}+SP(b));foreach(var e in b.FriendMinions.ToArray())Dmg(b,e,{d}+SP(b));}}')

    # AoE全部角色
    m = re.search(r'[Dd]eal \$?(\d+) damage to all characters', t)
    if m:
        d = m.group(1)
        return ('AoE全部角色', f'(b,s,t)=>{{foreach(var e in b.EnemyMinions.ToArray())Dmg(b,e,{d}+SP(b));foreach(var e in b.FriendMinions.ToArray())Dmg(b,e,{d}+SP(b));if(b.EnemyHero!=null)Dmg(b,b.EnemyHero,{d}+SP(b));if(b.FriendHero!=null)Dmg(b,b.FriendHero,{d}+SP(b));}}')

    # 治疗目标
    m = re.search(r'[Rr]estore (\d+) [Hh]ealth', t)
    if m:
        h = m.group(1)
        return ('治疗', f'(b,s,t)=>{{if(t!=null)t.Health=System.Math.Min(t.MaxHealth,t.Health+{h});}}')

    # buff +X/+Y
    m = re.search(r'[Gg]ive (?:a |your |all |an? )?(?:friendly )?(?:minion|character|Beast|Mech|Demon|Dragon|Murloc|Pirate|Elemental|Undead|Totem|Naga)s? \+(\d+)/\+(\d+)', t)
    if m:
        a, h = m.group(1), m.group(2)
        if 'all' in t.lower() or 'your' in t.lower():
            return ('buff全体', f'(b,s,t)=>{{foreach(var m in b.FriendMinions){{m.Atk+={a};m.Health+={h};m.MaxHealth+={h};}}}}')
        return ('buff单体', f'(b,s,t)=>{{if(t!=null){{t.Atk+={a};t.Health+={h};t.MaxHealth+={h};}}}}')

    # buff +X Attack
    m = re.search(r'[Gg]ive (?:a |your |an? )?(?:friendly )?(?:minion|character) \+(\d+) Attack', t)
    if m:
        a = m.group(1)
        return ('buff攻击', f'(b,s,t)=>{{if(t!=null)t.Atk+={a};}}')

    # buff +X Health
    m = re.search(r'[Gg]ive (?:a |your |an? )?(?:friendly )?(?:minion|character) \+(\d+) Health', t)
    if m:
        h = m.group(1)
        return ('buff生命', f'(b,s,t)=>{{if(t!=null){{t.Health+={h};t.MaxHealth+={h};}}}}')

    # 抽牌
    m = re.search(r'[Dd]raw (\d+) card', t)
    if m:
        n = m.group(1)
        return ('抽牌', f'(b,s,t)=>{{b.FriendCardDraw+={n};}}')
    m = re.search(r'[Dd]raw a card', t)
    if m:
        return ('抽牌', '(b,s,t)=>{b.FriendCardDraw+=1;}')

    # 护甲
    m = re.search(r'[Gg]ain (\d+) Armor', t)
    if m:
        a = m.group(1)
        return ('护甲', f'(b,s,t)=>{{if(b.FriendHero!=null)b.FriendHero.Armor+={a};}}')

    # 冰冻
    if re.search(r'[Ff]reeze a', t) or re.search(r'[Ff]reeze an enemy', t):
        return ('冰冻', '(b,s,t)=>{if(t!=null)t.IsFrozen=true;}')

    # 沉默
    if re.search(r'[Ss]ilence a', t) or re.search(r'[Ss]ilence an? ', t):
        return ('沉默', '(b,s,t)=>{if(t!=null)Silence(t);}')

    # 消灭单体
    if re.search(r'[Dd]estroy a minion', t) or re.search(r'[Dd]estroy an enemy minion', t):
        return ('消灭', '(b,s,t)=>{if(t!=null)t.Health=0;}')

    # 消灭所有敌方随从
    if re.search(r'[Dd]estroy all enemy minion', t):
        return ('消灭全部敌方', '(b,s,t)=>{b.EnemyMinions.Clear();}')

    # 消灭所有随从
    if re.search(r'[Dd]estroy all minion', t):
        return ('消灭全部', '(b,s,t)=>{b.FriendMinions.Clear();b.EnemyMinions.Clear();}')

    # 召唤 X/Y
    m = re.search(r'[Ss]ummon (?:a |an? )?(\d+)/(\d+)', t)
    if m:
        a, h = m.group(1), m.group(2)
        return ('召唤', f'(b,s,t)=>{{if(b.FriendMinions.Count<7)b.FriendMinions.Add(new SimEntity{{Atk={a},Health={h},MaxHealth={h},IsFriend=true,IsTired=true}});}}')

    # 给嘲讽
    if re.search(r'[Gg]ive (?:a |your )?(?:friendly )?minion  ?Taunt', t):
        return ('嘲讽', '(b,s,t)=>{if(t!=null)t.IsTaunt=true;}')

    # 给圣盾
    if re.search(r'[Gg]ive (?:a |your )?(?:friendly )?minion  ?Divine Shield', t):
        return ('圣盾', '(b,s,t)=>{if(t!=null)t.IsDivineShield=true;}')

    return None

def match_deathrattle(text):
    t = clean(text)

    # 对敌方英雄伤害
    m = re.search(r'[Dd]eal (\d+) damage to the enemy hero', t)
    if m:
        d = m.group(1)
        return ('亡语对英雄', f'(b,s,t)=>{{if(s.IsFriend&&b.EnemyHero!=null)Dmg(b,b.EnemyHero,{d});else if(!s.IsFriend&&b.FriendHero!=null)Dmg(b,b.FriendHero,{d});}}')

    # 对所有敌方伤害
    m = re.search(r'[Dd]eal (\d+) damage to all enem', t)
    if m:
        d = m.group(1)
        return ('亡语AoE敌方', f'(b,s,t)=>{{var es=s.IsFriend?b.EnemyMinions:b.FriendMinions;foreach(var e in es.ToArray())Dmg(b,e,{d});var hero=s.IsFriend?b.EnemyHero:b.FriendHero;if(hero!=null)Dmg(b,hero,{d});}}')

    # 对所有随从伤害
    m = re.search(r'[Dd]eal (\d+) damage to all minion', t)
    if m:
        d = m.group(1)
        return ('亡语AoE全部', f'(b,s,t)=>{{foreach(var e in b.EnemyMinions.ToArray())Dmg(b,e,{d});foreach(var e in b.FriendMinions.ToArray())Dmg(b,e,{d});}}')

    # 召唤
    m = re.search(r'[Ss]ummon (?:a |an? )?(\d+)/(\d+)', t)
    if m:
        a, h = m.group(1), m.group(2)
        return ('亡语召唤', f'(b,s,t)=>{{var list=s.IsFriend?b.FriendMinions:b.EnemyMinions;if(list.Count<7)list.Add(new SimEntity{{Atk={a},Health={h},MaxHealth={h},IsFriend=s.IsFriend,IsTired=true}});}}')

    # 抽牌
    m = re.search(r'[Dd]raw (\d+) card', t)
    if m:
        n = m.group(1)
        return ('亡语抽牌', f'(b,s,t)=>{{if(s.IsFriend)b.FriendCardDraw+={n};}}')
    if re.search(r'[Dd]raw a card', t):
        return ('亡语抽牌', '(b,s,t)=>{if(s.IsFriend)b.FriendCardDraw+=1;}')

    # 对随机敌方随从伤害
    m = re.search(r'[Dd]eal (\d+) damage to a random enemy minion', t)
    if m:
        d = m.group(1)
        return ('亡语随机敌方', f'(b,s,t)=>{{var es=s.IsFriend?b.EnemyMinions:b.FriendMinions;if(es.Count>0)Dmg(b,es[0],{d});}}')

    # 对随机敌方
    m = re.search(r'[Dd]eal (\d+) damage to a random enemy', t)
    if m:
        d = m.group(1)
        return ('亡语随机敌方', f'(b,s,t)=>{{var es=s.IsFriend?b.EnemyMinions:b.FriendMinions;var hero=s.IsFriend?b.EnemyHero:b.FriendHero;var ts=new System.Collections.Generic.List<SimEntity>(es);if(hero!=null)ts.Add(hero);if(ts.Count>0)Dmg(b,ts[0],{d});}}')

    # 消灭随机敌方随从
    if re.search(r'[Dd]estroy a random enemy minion', t):
        return ('亡语消灭', '(b,s,t)=>{var es=s.IsFriend?b.EnemyMinions:b.FriendMinions;if(es.Count>0)es[0].Health=0;}')

    return None

def match_endofturn(text):
    t = clean(text)

    # 自身成长 +X/+Y
    m = re.search(r'[Gg]ains? \+(\d+)/\+(\d+)', t)
    if m and ('end of' in t.lower() or 'each turn' in t.lower()):
        a, h = m.group(1), m.group(2)
        return ('回合结束成长', f'(b,s,t)=>{{s.Atk+={a};s.Health+={h};s.MaxHealth+={h};}}')

    # 随机敌方伤害
    m = re.search(r'[Dd]eal (\d+) damage to a random enemy', t)
    if m and ('end of' in t.lower() or 'each turn' in t.lower()):
        d = m.group(1)
        return ('回合结束随机伤害', f'(b,s,t)=>{{var es=s.IsFriend?b.EnemyMinions:b.FriendMinions;var hero=s.IsFriend?b.EnemyHero:b.FriendHero;var ts=new System.Collections.Generic.List<SimEntity>(es);if(hero!=null)ts.Add(hero);if(ts.Count>0)Dmg(b,ts[0],{d});}}')

    # 抽牌
    if re.search(r'[Dd]raw a card', t) and ('end of' in t.lower() or 'each turn' in t.lower()):
        return ('回合结束抽牌', '(b,s,t)=>{if(s.IsFriend)b.FriendCardDraw+=1;}')

    # 治疗英雄
    m = re.search(r'[Rr]estore (\d+) [Hh]ealth to your hero', t)
    if m and ('end of' in t.lower() or 'each turn' in t.lower()):
        h = m.group(1)
        return ('回合结束治疗', f'(b,s,t)=>{{if(s.IsFriend&&b.FriendHero!=null)b.FriendHero.Health=System.Math.Min(b.FriendHero.MaxHealth,b.FriendHero.Health+{h});}}')

    return None

# 收集结果
spells = []
battlecries = []
deathrattles = []
endofturns = []
stats = {'spell_matched': 0, 'spell_skipped': 0, 'bc_matched': 0, 'bc_skipped': 0, 'dr_matched': 0, 'dr_skipped': 0, 'eot_matched': 0, 'eot_skipped': 0}

for c in cards:
    if not c.get('collectible') or c['id'] in registered:
        continue
    cid = c['id']
    text = c.get('text', '')
    ctype = c['type']
    mechs = c.get('mechanics', [])

    # 法术效果
    if ctype == 'SPELL':
        result = match_spell_effect(text)
        if result:
            cat, code = result
            spells.append((cid, cat, code))
            stats['spell_matched'] += 1
        else:
            stats['spell_skipped'] += 1

    # 战吼
    if ctype == 'MINION' and 'BATTLECRY' in mechs:
        # 提取战吼部分文本
        bc_text = text
        m_bc = re.search(r'Battlecry:?\s*(.*?)(?:\.|$)', clean(text), re.IGNORECASE)
        if m_bc:
            bc_text = m_bc.group(1)
        result = match_spell_effect(bc_text)
        if result:
            cat, code = result
            battlecries.append((cid, cat, code))
            stats['bc_matched'] += 1
        else:
            stats['bc_skipped'] += 1

    # 亡语
    if ctype == 'MINION' and 'DEATHRATTLE' in mechs:
        dr_text = text
        m_dr = re.search(r'Deathrattle:?\s*(.*?)(?:\.|$)', clean(text), re.IGNORECASE)
        if m_dr:
            dr_text = m_dr.group(1)
        result = match_deathrattle(dr_text)
        if result:
            cat, code = result
            deathrattles.append((cid, cat, code))
            stats['dr_matched'] += 1
        else:
            stats['dr_skipped'] += 1

    # 回合结束
    if ctype == 'MINION' and ('end of' in clean(text).lower() or 'each turn' in clean(text).lower()):
        result = match_endofturn(text)
        if result:
            cat, code = result
            endofturns.append((cid, cat, code))
            stats['eot_matched'] += 1
        else:
            stats['eot_skipped'] += 1

# 输出生成的代码
with open('generated_effects.txt', 'w', encoding='utf-8') as f:
    f.write("// === 新增法术效果 (RegisterSpells) ===\n")
    for cid, cat, code in sorted(spells):
        f.write(f"            db.Register(C.{cid},EffectTrigger.Spell,{code}); // {cat}\n")

    f.write(f"\n// === 新增战吼效果 (RegisterBattlecries) ===\n")
    for cid, cat, code in sorted(battlecries):
        f.write(f"            db.Register(C.{cid},EffectTrigger.Battlecry,{code}); // {cat}\n")

    f.write(f"\n// === 新增亡语效果 (RegisterDeathrattles) ===\n")
    for cid, cat, code in sorted(deathrattles):
        f.write(f"            db.Register(C.{cid},EffectTrigger.Deathrattle,{code}); // {cat}\n")

    f.write(f"\n// === 新增回合结束效果 (RegisterEndOfTurn) ===\n")
    for cid, cat, code in sorted(endofturns):
        f.write(f"            db.Register(C.{cid},EffectTrigger.EndOfTurn,{code}); // {cat}\n")

print(f"法术: 匹配 {stats['spell_matched']}, 跳过 {stats['spell_skipped']}")
print(f"战吼: 匹配 {stats['bc_matched']}, 跳过 {stats['bc_skipped']}")
print(f"亡语: 匹配 {stats['dr_matched']}, 跳过 {stats['dr_skipped']}")
print(f"回合结束: 匹配 {stats['eot_matched']}, 跳过 {stats['eot_skipped']}")
print(f"总计新增: {stats['spell_matched']+stats['bc_matched']+stats['dr_matched']+stats['eot_matched']}")
print("已写入 generated_effects.txt")
