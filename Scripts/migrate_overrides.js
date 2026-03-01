/**
 * migrate_overrides.js
 * 将 effects_data.json + CardEffectDB.cs RegisterOverrides 中的卡牌效果
 * 迁移到 CardEffects/ 目录下的独立 JSON 文件
 */
const fs = require('fs');
const path = require('path');

const BASE = 'h:/桌面/炉石脚本';
const EFFECTS_DIR = path.join(BASE, 'CardEffects');

// ── 加载数据源 ──
const cards = JSON.parse(fs.readFileSync(path.join(BASE, 'cards.json'), 'utf8'));
const effectsData = JSON.parse(fs.readFileSync(path.join(BASE, 'effects_data.json'), 'utf8'));
const csCode = fs.readFileSync(path.join(BASE, 'BotMain/AI/CardEffectDB.cs'), 'utf8');

// 构建卡牌名称查找表
const cardMap = {};
cards.forEach(c => { if (c.id) cardMap[c.id] = c; });
function strip(t) { return t ? t.replace(/<[^>]+>/g, '').replace(/\[x\]/g, '').replace(/\n/g, ' ').replace(/\s+/g, ' ').trim() : ''; }

// 结果存储：cardId -> { trigger: { targetType, effects } }
const result = {};

function setEffect(cardId, trigger, targetType, effects) {
    if (!result[cardId]) result[cardId] = {};
    result[cardId][trigger] = { targetType: targetType || 'None', effects };
}

// 不覆盖已有的（让 RegisterOverrides 优先）
function setEffectIfNew(cardId, trigger, targetType, effects) {
    if (!result[cardId]) result[cardId] = {};
    if (!result[cardId][trigger]) {
        result[cardId][trigger] = { targetType: targetType || 'None', effects };
    }
}

// ════════════════════════════════════════════════════════════
// Step 1: 处理 effects_data.json
// ════════════════════════════════════════════════════════════

const catMap = {
    // ── 法术 ──
    spell_dmg: { tr: 'Spell', tt: 'AnyCharacter', fn: (a, b, sp) => [{ type: 'dmg', v: a, ...(sp ? { useSP: true } : {}) }] },
    spell_aoe_all: { tr: 'Spell', tt: 'None', fn: (a, b, sp) => [{ type: 'dmg_all', v: a, ...(sp ? { useSP: true } : {}) }] },
    spell_aoe_enemies_heroes: { tr: 'Spell', tt: 'None', fn: (a, b, sp) => [{ type: 'dmg_enemy', v: a, ...(sp ? { useSP: true } : {}) }] },
    spell_face: { tr: 'Spell', tt: 'None', fn: (a, b, sp) => [{ type: 'dmg_face', v: a, ...(sp ? { useSP: true } : {}) }] },
    spell_random_enemy: { tr: 'Spell', tt: 'None', fn: (a, b, sp) => [{ type: 'dmg_random', v: a, ...(sp ? { useSP: true } : {}) }] },
    spell_destroy: { tr: 'Spell', tt: 'AnyMinion', fn: () => [{ type: 'destroy' }] },
    spell_destroy_all: { tr: 'Spell', tt: 'None', fn: () => [{ type: 'destroy_all' }] },
    spell_freeze: { tr: 'Spell', tt: 'EnemyOnly', fn: () => [{ type: 'freeze' }] },
    spell_freeze_all: { tr: 'Spell', tt: 'None', fn: () => [{ type: 'freeze_all' }] },
    spell_silence: { tr: 'Spell', tt: 'AnyMinion', fn: () => [{ type: 'silence' }] },
    spell_heal: { tr: 'Spell', tt: 'AnyCharacter', fn: (a) => [{ type: 'heal', v: a }] },
    spell_draw: { tr: 'Spell', tt: 'None', fn: (a) => [{ type: 'draw', n: a }] },
    spell_armor: { tr: 'Spell', tt: 'None', fn: (a) => [{ type: 'armor', v: a }] },
    spell_summon: { tr: 'Spell', tt: 'None', fn: (a, b) => [{ type: 'summon', atk: a, hp: b }] },
    spell_buff: { tr: 'Spell', tt: 'FriendlyMinion', fn: (a, b) => [{ type: 'buff', atk: a, hp: b }] },
    spell_buff_all: { tr: 'Spell', tt: 'None', fn: (a, b) => [{ type: 'buff_all', atk: a, hp: b }] },
    spell_give_taunt: { tr: 'Spell', tt: 'FriendlyMinion', fn: () => [{ type: 'give_taunt' }] },
    spell_give_ds: { tr: 'Spell', tt: 'FriendlyMinion', fn: () => [{ type: 'give_ds' }] },
    spell_set_hp: { tr: 'Spell', tt: 'AnyMinion', fn: (a) => [{ type: 'set_hp', v: a }] },
    spell_atk_buff: { tr: 'Spell', tt: 'FriendlyMinion', fn: (a) => [{ type: 'buff', atk: a, hp: 0 }] },
    spell_give_hp: { tr: 'Spell', tt: 'FriendlyMinion', fn: (a) => [{ type: 'buff', atk: 0, hp: a }] },
    spell_heal_hero: { tr: 'Spell', tt: 'None', fn: (a) => [{ type: 'heal_hero', v: a }] },
    spell_heal_all: { tr: 'Spell', tt: 'None', fn: (a) => [{ type: 'heal_all', v: a }] },
    spell_give_windfury: { tr: 'Spell', tt: 'FriendlyMinion', fn: () => [{ type: 'give_windfury' }] },
    spell_give_lifesteal: { tr: 'Spell', tt: 'FriendlyMinion', fn: () => [{ type: 'give_lifesteal' }] },
    spell_give_rush: { tr: 'Spell', tt: 'FriendlyMinion', fn: () => [{ type: 'give_rush' }] },
    spell_add_hand: { tr: 'Spell', tt: 'None', fn: (a) => [{ type: 'draw', n: a }] },
    spell_bounce: { tr: 'Spell', tt: 'AnyMinion', fn: () => [{ type: 'bounce' }] },
    spell_equip: { tr: 'Spell', tt: 'None', fn: (a, b) => [{ type: 'equip', atk: a, dur: b }] },
    // ── 战吼 ──
    bc_dmg: { tr: 'Battlecry', tt: 'AnyCharacter', fn: (a) => [{ type: 'dmg', v: a }] },
    bc_aoe_all: { tr: 'Battlecry', tt: 'None', fn: (a) => [{ type: 'dmg_all', v: a }] },
    bc_aoe_enemies_heroes: { tr: 'Battlecry', tt: 'None', fn: (a) => [{ type: 'dmg_enemy', v: a }] },
    bc_face: { tr: 'Battlecry', tt: 'None', fn: (a) => [{ type: 'dmg_face', v: a }] },
    bc_random_enemy: { tr: 'Battlecry', tt: 'None', fn: (a) => [{ type: 'dmg_random', v: a }] },
    bc_destroy: { tr: 'Battlecry', tt: 'AnyMinion', fn: () => [{ type: 'destroy' }] },
    bc_destroy_all: { tr: 'Battlecry', tt: 'None', fn: () => [{ type: 'destroy_all' }] },
    bc_destroy_weapon: { tr: 'Battlecry', tt: 'None', fn: () => [{ type: 'destroy_weapon' }] },
    bc_silence: { tr: 'Battlecry', tt: 'AnyMinion', fn: () => [{ type: 'silence' }] },
    bc_freeze: { tr: 'Battlecry', tt: 'EnemyOnly', fn: () => [{ type: 'freeze' }] },
    bc_freeze_all: { tr: 'Battlecry', tt: 'None', fn: () => [{ type: 'freeze_all' }] },
    bc_heal: { tr: 'Battlecry', tt: 'AnyCharacter', fn: (a) => [{ type: 'heal', v: a }] },
    bc_draw: { tr: 'Battlecry', tt: 'None', fn: (a) => [{ type: 'draw', n: a }] },
    bc_armor: { tr: 'Battlecry', tt: 'None', fn: (a) => [{ type: 'armor', v: a }] },
    bc_summon: { tr: 'Battlecry', tt: 'None', fn: (a, b) => [{ type: 'summon', atk: a, hp: b }] },
    bc_buff: { tr: 'Battlecry', tt: 'FriendlyMinion', fn: (a, b) => [{ type: 'buff', atk: a, hp: b }] },
    bc_buff_all: { tr: 'Battlecry', tt: 'None', fn: (a, b) => [{ type: 'buff_all', atk: a, hp: b }] },
    bc_gain_buff: { tr: 'Battlecry', tt: 'None', fn: (a, b) => [{ type: 'buff_self', atk: a, hp: b }] },
    bc_give_taunt: { tr: 'Battlecry', tt: 'FriendlyMinion', fn: () => [{ type: 'give_taunt' }] },
    bc_give_ds: { tr: 'Battlecry', tt: 'FriendlyMinion', fn: () => [{ type: 'give_ds' }] },
    bc_atk_buff: { tr: 'Battlecry', tt: 'FriendlyMinion', fn: (a) => [{ type: 'buff', atk: a, hp: 0 }] },
    bc_give_hp: { tr: 'Battlecry', tt: 'FriendlyMinion', fn: (a) => [{ type: 'buff', atk: 0, hp: a }] },
    bc_set_hp: { tr: 'Battlecry', tt: 'AnyMinion', fn: (a) => [{ type: 'set_hp', v: a }] },
    bc_heal_hero: { tr: 'Battlecry', tt: 'None', fn: (a) => [{ type: 'heal_hero', v: a }] },
    bc_restore_all: { tr: 'Battlecry', tt: 'AnyCharacter', fn: () => [{ type: 'heal', v: 99 }] },
    bc_give_windfury: { tr: 'Battlecry', tt: 'FriendlyMinion', fn: () => [{ type: 'give_windfury' }] },
    bc_give_rush: { tr: 'Battlecry', tt: 'FriendlyMinion', fn: () => [{ type: 'give_rush' }] },
    bc_set_hero_hp: { tr: 'Battlecry', tt: 'None', fn: (a) => [{ type: 'noop' }] },
    bc_add_hand: { tr: 'Battlecry', tt: 'None', fn: (a) => [{ type: 'draw', n: a }] },
    bc_bounce: { tr: 'Battlecry', tt: 'AnyMinion', fn: () => [{ type: 'bounce' }] },
    bc_equip: { tr: 'Battlecry', tt: 'None', fn: (a, b) => [{ type: 'equip', atk: a, dur: b }] },
    // ── 连击 ──
    combo_dmg: { tr: 'Battlecry', tt: 'AnyCharacter', fn: (a) => [{ type: 'dmg', v: a }] },
    combo_buff: { tr: 'Battlecry', tt: 'None', fn: (a, b) => [{ type: 'buff_self', atk: a, hp: b }] },
    combo_atk: { tr: 'Battlecry', tt: 'None', fn: (a) => [{ type: 'buff_self', atk: a, hp: 0 }] },
    combo_draw: { tr: 'Battlecry', tt: 'None', fn: (a) => [{ type: 'draw', n: a }] },
    combo_bounce: { tr: 'Battlecry', tt: 'AnyMinion', fn: () => [{ type: 'bounce' }] },
    // ── 亡语 ──
    dr_dmg_enemy_hero: { tr: 'Deathrattle', tt: 'None', fn: (a) => [{ type: 'dmg_face', v: a }] },
    dr_aoe_enemy: { tr: 'Deathrattle', tt: 'None', fn: (a) => [{ type: 'dmg_enemy', v: a }] },
    dr_aoe_all: { tr: 'Deathrattle', tt: 'None', fn: (a) => [{ type: 'dmg_all', v: a }] },
    dr_dmg_random: { tr: 'Deathrattle', tt: 'None', fn: (a) => [{ type: 'dmg_random', v: a }] },
    dr_draw: { tr: 'Deathrattle', tt: 'None', fn: (a) => [{ type: 'draw', n: a }] },
    dr_armor: { tr: 'Deathrattle', tt: 'None', fn: (a) => [{ type: 'armor', v: a }] },
    dr_summon: { tr: 'Deathrattle', tt: 'None', fn: (a, b) => [{ type: 'summon', atk: a, hp: b }] },
    dr_freeze: { tr: 'Deathrattle', tt: 'None', fn: () => [{ type: 'freeze_all' }] },
    dr_equip: { tr: 'Deathrattle', tt: 'None', fn: (a, b) => [{ type: 'equip', atk: a, dur: b }] },
    dr_buff: { tr: 'Deathrattle', tt: 'None', fn: (a, b) => [{ type: 'buff_all', atk: a, hp: b }] },
    dr_buff_hand: { tr: 'Deathrattle', tt: 'None', fn: (a, b) => [{ type: 'buff_all', atk: a, hp: b }] },
    dr_add_hand: { tr: 'Deathrattle', tt: 'None', fn: (a) => [{ type: 'draw', n: a }] },
    dr_restore: { tr: 'Deathrattle', tt: 'None', fn: (a) => [{ type: 'heal_hero', v: a }] },
    dr_destroy: { tr: 'Deathrattle', tt: 'None', fn: () => [{ type: 'destroy' }] },
    dr_summon_copy: { tr: 'Deathrattle', tt: 'None', fn: (a, b) => [{ type: 'summon', atk: a || 1, hp: b || 1 }] },
    // ── 回合结束 ──
    eot_dmg_random: { tr: 'EndOfTurn', tt: 'None', fn: (a) => [{ type: 'dmg_random', v: a }] },
    eot_aoe_enemy: { tr: 'EndOfTurn', tt: 'None', fn: (a) => [{ type: 'dmg_enemy', v: a }] },
    eot_aoe_all: { tr: 'EndOfTurn', tt: 'None', fn: (a) => [{ type: 'dmg_all', v: a }] },
    eot_draw: { tr: 'EndOfTurn', tt: 'None', fn: (a) => [{ type: 'draw', n: a }] },
    eot_armor: { tr: 'EndOfTurn', tt: 'None', fn: (a) => [{ type: 'armor', v: a }] },
    eot_heal: { tr: 'EndOfTurn', tt: 'None', fn: (a) => [{ type: 'heal_hero', v: a }] },
    eot_summon: { tr: 'EndOfTurn', tt: 'None', fn: (a, b) => [{ type: 'summon', atk: a, hp: b }] },
    eot_self_buff: { tr: 'EndOfTurn', tt: 'None', fn: (a, b) => [{ type: 'buff_self', atk: a, hp: b }] },
    eot_add_hand: { tr: 'EndOfTurn', tt: 'None', fn: (a) => [{ type: 'draw', n: a }] },
    eot_restore: { tr: 'EndOfTurn', tt: 'None', fn: (a) => [{ type: 'heal_hero', v: a }] },
    eot_buff_atk: { tr: 'EndOfTurn', tt: 'None', fn: (a) => [{ type: 'buff', atk: a, hp: 0 }] },
    eot_buff_hp: { tr: 'EndOfTurn', tt: 'None', fn: (a) => [{ type: 'buff', atk: 0, hp: a }] },
    eot_buff_friendly: { tr: 'EndOfTurn', tt: 'None', fn: (a, b) => [{ type: 'buff_all', atk: a, hp: b }] },
};

for (const [cat, entries] of Object.entries(effectsData)) {
    const m = catMap[cat];
    if (!m || !entries || entries.length === 0) continue;
    for (const entry of entries) {
        let id, v1 = 0, v2 = 0, sp = false;
        if (Array.isArray(entry)) {
            id = entry[0];
            if (entry.length > 1) v1 = entry[1];
            if (entry.length > 2) {
                if (typeof entry[2] === 'boolean') sp = entry[2];
                else v2 = entry[2];
            }
        } else { id = entry; }
        if (!id) continue;
        const effects = m.fn(v1, v2, sp);
        setEffectIfNew(id, m.tr, m.tt, effects);
    }
}
console.log(`Step 1: effects_data.json → ${Object.keys(result).length} cards`);

// ════════════════════════════════════════════════════════════
// Step 2: 解析 RegisterOverrides
// ════════════════════════════════════════════════════════════

// 提取 RegisterOverrides 方法体
const roMatch = csCode.match(/private static void RegisterOverrides\(CardEffectDB db\)\s*\{([\s\S]*?)\n        \}/);
if (!roMatch) { console.error('未找到 RegisterOverrides'); process.exit(1); }
const roBody = roMatch[1];

// 提取所有 db.Register 和 db.RegisterTargetType 调用
const regCalls = [];
const lines = roBody.split('\n');
for (const line of lines) {
    const trimmed = line.trim();
    if (trimmed.startsWith('db.Register') || trimmed.startsWith('//')) {
        regCalls.push(trimmed);
    }
}

// 解析每个 db.Register 调用
function parseRegisterCall(line) {
    // db.Register(C.XXX, EffectTrigger.YYY, (b,s,t)=>{...});
    const m = line.match(/^db\.Register\(C\.(\w+)\s*,\s*EffectTrigger\.(\w+)\s*,\s*\(b,s,t\)=>\{(.*)\}\);$/);
    if (!m) return null;
    return { id: m[1], trigger: m[2], body: m[3] };
}

function parseTargetTypeCall(line) {
    const m = line.match(/^db\.RegisterTargetType\(C\.(\w+)\s*,\s*EffectTrigger\.(\w+)\s*,\s*BattlecryTargetType\.(\w+)\);$/);
    if (!m) return null;
    return { id: m[1], trigger: m[2], targetType: m[3] };
}

// 模式匹配 lambda body → effects 数组
function parseLambdaBody(body, trigger) {
    const b = body.trim();
    if (!b || b === '') return { effects: [{ type: 'noop' }], tt: 'None' };

    let m;
    // 单体伤害 + SP
    if ((m = b.match(/^if\(t!=null\)Dmg\(b,t,(\d+)\+SP\(b\)\);$/)))
        return { effects: [{ type: 'dmg', v: +m[1], useSP: true }], tt: 'AnyCharacter' };
    if ((m = b.match(/^if\(t!=null\)Dmg\(b,t,(\d+)\);$/)))
        return { effects: [{ type: 'dmg', v: +m[1] }], tt: 'AnyCharacter' };

    // AOE全体 + SP
    if ((m = b.match(/^int d=(\d+)\+SP\(b\);foreach\(var m in b\.FriendMinions\.ToArray\(\)\)Dmg\(b,m,d\);foreach\(var m in b\.EnemyMinions\.ToArray\(\)\)Dmg\(b,m,d\);$/)))
        return { effects: [{ type: 'dmg_all', v: +m[1], useSP: true }], tt: 'None' };
    if ((m = b.match(/^foreach\(var m in b\.FriendMinions\.ToArray\(\)\)Dmg\(b,m,(\d+)\);foreach\(var m in b\.EnemyMinions\.ToArray\(\)\)Dmg\(b,m,\1\);$/)))
        return { effects: [{ type: 'dmg_all', v: +m[1] }], tt: 'None' };
    // BC AOE all (excludes self)
    if ((m = b.match(/^foreach\(var m in b\.FriendMinions\.ToArray\(\)\)if\(m!=s\)Dmg\(b,m,(\d+)\);foreach\(var m in b\.EnemyMinions\.ToArray\(\)\)Dmg\(b,m,\d+\);$/)))
        return { effects: [{ type: 'dmg_all', v: +m[1] }], tt: 'None' };

    // AOE敌方 + SP
    if ((m = b.match(/^int d=(\d+)\+SP\(b\);foreach\(var m in b\.EnemyMinions\.ToArray\(\)\)Dmg\(b,m,d\);if\(b\.EnemyHero!=null\)Dmg\(b,b\.EnemyHero,d\);$/)))
        return { effects: [{ type: 'dmg_enemy', v: +m[1], useSP: true }], tt: 'None' };
    if ((m = b.match(/^foreach\(var (?:m|e) in b\.EnemyMinions\.ToArray\(\)\)Dmg\(b,(?:m|e),(\d+)\);if\(b\.EnemyHero!=null\)Dmg\(b,b\.EnemyHero,\d+\);$/)))
        return { effects: [{ type: 'dmg_enemy', v: +m[1] }], tt: 'None' };
    if ((m = b.match(/^foreach\(var (?:m|e) in b\.EnemyMinions\.ToArray\(\)\)Dmg\(b,(?:m|e),(\d+)\);$/)))
        return { effects: [{ type: 'dmg_enemy', v: +m[1] }], tt: 'None' };

    // 打脸
    if ((m = b.match(/^if\(b\.EnemyHero!=null\)Dmg\(b,b\.EnemyHero,(\d+)\+SP\(b\)\);$/)))
        return { effects: [{ type: 'dmg_face', v: +m[1], useSP: true }], tt: 'None' };
    if ((m = b.match(/^if\(b\.EnemyHero!=null\)Dmg\(b,b\.EnemyHero,(\d+)\);$/)))
        return { effects: [{ type: 'dmg_face', v: +m[1] }], tt: 'None' };

    // 随机敌方
    if (b.includes('var ts=new List<SimEntity>(b.EnemyMinions)')) {
        m = b.match(/Dmg\(b,ts\[0\],(\d+)(?:\+SP\(b\))?\)/);
        const sp = b.includes('+SP(b)');
        return { effects: [{ type: 'dmg_random', v: m ? +m[1] : 1, ...(sp ? { useSP: true } : {}) }], tt: 'None' };
    }

    // 冻结
    if (b === 'if(t!=null)t.IsFrozen=true;')
        return { effects: [{ type: 'freeze' }], tt: 'EnemyOnly' };
    if (b.includes('m.IsFrozen=true') && b.includes('EnemyMinions'))
        return { effects: [{ type: 'freeze_all' }], tt: 'None' };

    // 消灭
    if (b === 'if(t!=null)t.Health=0;')
        return { effects: [{ type: 'destroy' }], tt: 'AnyMinion' };
    if (b.includes('m.Health=0') && b.includes('FriendMinions') && b.includes('EnemyMinions'))
        return { effects: [{ type: 'destroy_all' }], tt: 'None' };
    // 消灭武器
    if (b === 'b.EnemyWeapon=null;')
        return { effects: [{ type: 'destroy_weapon' }], tt: 'None' };

    // 沉默
    if (b === 'Silence(t);' || b === 'CardEffectDB.DoSilence(t);')
        return { effects: [{ type: 'silence' }], tt: 'AnyMinion' };

    // 治疗
    if ((m = b.match(/^if\(t!=null\)t\.Health=Math\.Min\(t\.MaxHealth,t\.Health\+(\d+)\);$/)))
        return { effects: [{ type: 'heal', v: +m[1] }], tt: 'AnyCharacter' };
    if (b === 'if(t!=null)t.Health=t.MaxHealth;')
        return { effects: [{ type: 'heal', v: 99 }], tt: 'AnyCharacter' };

    // 治疗英雄
    if ((m = b.match(/FriendHero\.Health=Math\.Min\(.*?FriendHero\.Health\+(\d+)\)/)))
        return { effects: [{ type: 'heal_hero', v: +m[1] }], tt: 'None' };

    // 抽牌
    if ((m = b.match(/^b\.FriendCardDraw\+=(\d+);$/)))
        return { effects: [{ type: 'draw', n: +m[1] }], tt: 'None' };
    if ((m = b.match(/^if\(s\.IsFriend\)b\.FriendCardDraw\+=(\d+);$/)))
        return { effects: [{ type: 'draw', n: +m[1] }], tt: 'None' };

    // 护甲
    if ((m = b.match(/FriendHero\.Armor\+=(\d+)/)))
        return { effects: [{ type: 'armor', v: +m[1] }], tt: 'None' };

    // 召唤
    if ((m = b.match(/FriendMinions\.Add\(new SimEntity\{Atk=(\d+),Health=(\d+)/))) {
        const effects = [{ type: 'summon', atk: +m[1], hp: +m[2] }];
        // 多个召唤
        const allSummons = [...b.matchAll(/FriendMinions\.Add\(new SimEntity\{Atk=(\d+),Health=(\d+)/g)];
        if (allSummons.length > 1) {
            effects.length = 0;
            for (const s of allSummons) {
                const e = { type: 'summon', atk: +s[1], hp: +s[2] };
                // 检查该召唤物是否有 HasRush/HasCharge/IsStealth
                effects.push(e);
            }
        }
        // 检查是否还有其他效果
        if (b.includes('Dmg(b,') && b.includes('EnemyMinions')) {
            const dm = b.match(/Dmg\(b,(?:m|e),(\d+)\)/);
            if (dm) effects.unshift({ type: 'dmg_enemy', v: +dm[1] });
        }
        if (b.includes('FriendCardDraw+=')) {
            const dm = b.match(/FriendCardDraw\+=(\d+)/);
            if (dm) effects.push({ type: 'draw', n: +dm[1] });
        }
        const tt = 'None';
        return { effects, tt };
    }
    // DR/EOT 召唤
    if ((m = b.match(/list\.Add\(new SimEntity\{Atk=(\d+),Health=(\d+)/)))
        return { effects: [{ type: 'summon', atk: +m[1], hp: +m[2] }], tt: 'None' };

    // buff目标
    if ((m = b.match(/^if\(t!=null\)\{t\.Atk\+=(\d+);t\.Health\+=(\d+);t\.MaxHealth\+=\d+;\}$/)))
        return { effects: [{ type: 'buff', atk: +m[1], hp: +m[2] }], tt: 'FriendlyMinion' };
    // buff + 抽牌
    if ((m = b.match(/if\(t!=null\)\{t\.Atk\+=(\d+);t\.Health\+=(\d+);t\.MaxHealth\+=\d+;\}b\.FriendCardDraw\+=(\d+);/)))
        return { effects: [{ type: 'buff', atk: +m[1], hp: +m[2] }, { type: 'draw', n: +m[3] }], tt: 'FriendlyMinion' };

    // buff_self
    if ((m = b.match(/^s\.Atk\+=(\d+);s\.Health\+=(\d+);s\.MaxHealth\+=\d+;$/)))
        return { effects: [{ type: 'buff_self', atk: +m[1], hp: +m[2] }], tt: 'None' };
    // combo buff_self
    if ((m = b.match(/CardsPlayedThisTurn>0\)\{s\.Atk\+=(\d+);s\.Health\+=(\d+)/)))
        return { effects: [{ type: 'buff_self', atk: +m[1], hp: +m[2] }], tt: 'None' };

    // combo draw
    if ((m = b.match(/CardsPlayedThisTurn>0\)b\.FriendCardDraw\+=(\d+)/)))
        return { effects: [{ type: 'draw', n: +m[1] }], tt: 'None' };

    // combo dmg
    if ((m = b.match(/CardsPlayedThisTurn>0&&t!=null\)Dmg\(b,t,(\d+)\)/)))
        return { effects: [{ type: 'dmg', v: +m[1] }], tt: 'AnyCharacter' };

    // give keywords
    if (b === 'if(t!=null)t.IsTaunt=true;')
        return { effects: [{ type: 'give_taunt' }], tt: 'FriendlyMinion' };
    if (b === 'if(t!=null)t.IsDivineShield=true;')
        return { effects: [{ type: 'give_ds' }], tt: 'FriendlyMinion' };
    if (b === 'if(t!=null)t.IsWindfury=true;')
        return { effects: [{ type: 'give_windfury' }], tt: 'FriendlyMinion' };
    if (b === 'if(t!=null)t.IsLifeSteal=true;')
        return { effects: [{ type: 'give_lifesteal' }], tt: 'FriendlyMinion' };
    if (b === 'if(t!=null)t.HasRush=true;')
        return { effects: [{ type: 'give_rush' }], tt: 'FriendlyMinion' };
    if (b === 'if(t!=null)t.HasReborn=true;')
        return { effects: [{ type: 'give_reborn' }], tt: 'FriendlyMinion' };

    // 攻击力 buff
    if ((m = b.match(/^if\(t!=null\)t\.Atk\+=(\d+);$/)))
        return { effects: [{ type: 'buff', atk: +m[1], hp: 0 }], tt: 'FriendlyMinion' };
    // buff + rush
    if ((m = b.match(/if\(t!=null\)\{t\.Atk\+=(\d+);t\.HasRush=true;\}/)))
        return { effects: [{ type: 'buff', atk: +m[1], hp: 0 }, { type: 'give_rush' }], tt: 'FriendlyMinion' };

    // 生命值 buff
    if ((m = b.match(/^if\(t!=null\)\{t\.Health\+=(\d+);t\.MaxHealth\+=\d+;\}$/)))
        return { effects: [{ type: 'buff', atk: 0, hp: +m[1] }], tt: 'FriendlyMinion' };

    // 弹回
    if (b.includes('FriendMinions.Remove(t)') && b.includes('Hand.Add(t)'))
        return { effects: [{ type: 'bounce' }], tt: 'AnyMinion' };

    // 装备武器
    if ((m = b.match(/FriendWeapon=new SimEntity\{Atk=(\d+),Health=(\d+)/)))
        return { effects: [{ type: 'equip', atk: +m[1], dur: +m[2] }], tt: 'None' };

    // 英雄免疫
    if (b.includes('FriendHero.IsImmune=true'))
        return { effects: [{ type: 'give_immune' }], tt: 'None' };

    // 清场
    if (b.includes('FriendMinions.Clear()') && b.includes('EnemyMinions.Clear()'))
        return { effects: [{ type: 'clear_board' }], tt: 'None' };
    // RemoveAll 变种
    if (b.includes('RemoveAll'))
        return { effects: [{ type: 'destroy_all' }], tt: 'None' };

    // set_hp
    if ((m = b.match(/if\(t!=null\)\{t\.Atk=(\d+);t\.Health=(\d+);t\.MaxHealth=\d+;\}/)))
        return { effects: [{ type: 'set_hp', v: +m[2] }, { type: 'buff', atk: +m[1], hp: 0 }], tt: 'AnyMinion' };
    if ((m = b.match(/if\(t!=null\)\{t\.Health=(\d+);t\.MaxHealth=\d+;\}/)))
        return { effects: [{ type: 'set_hp', v: +m[1] }], tt: 'AnyMinion' };

    // 给所有友方 rush
    if (b.includes('foreach(var m in b.FriendMinions)m.HasRush=true'))
        return { effects: [{ type: 'noop' }], tt: 'None' };

    // 给所有友方 DS
    if (b.includes('foreach(var m in b.FriendMinions)m.IsDivineShield=true'))
        return { effects: [{ type: 'noop' }], tt: 'None' };

    // 冻结 + 召唤
    if (b.includes('IsFrozen=true') && b.includes('FriendMinions.Add')) {
        m = b.match(/FriendMinions\.Add\(new SimEntity\{Atk=(\d+),Health=(\d+)/);
        return { effects: [{ type: 'freeze' }, { type: 'summon', atk: m ? +m[1] : 1, hp: m ? +m[2] : 1 }], tt: 'EnemyOnly' };
    }

    // dmg + buff
    if ((m = b.match(/if\(t!=null\)\{Dmg\(b,t,(\d+)\);t\.Atk\+=(\d+);\}/)))
        return { effects: [{ type: 'dmg', v: +m[1] }, { type: 'buff', atk: +m[2], hp: 0 }], tt: 'AnyCharacter' };

    // Mana set (b.Mana=...)
    if (b.includes('b.Mana'))
        return { effects: [{ type: 'noop' }], tt: 'None' };

    // DR: 各种 s.IsFriend 变体
    if (b.includes('s.IsFriend')) {
        // DR/EOT 随机敌方伤害
        if (b.includes('Dmg(b,') && b.includes('ts[0]')) {
            m = b.match(/Dmg\(b,ts\[0\],(\d+)\)/);
            return { effects: [{ type: 'dmg_random', v: m ? +m[1] : 1 }], tt: 'None' };
        }
        // DR/EOT AOE 敌方
        if (b.includes('foreach') && b.includes('Dmg(b,m,')) {
            m = b.match(/Dmg\(b,m,(\d+)\)/);
            return { effects: [{ type: 'dmg_enemy', v: m ? +m[1] : 1 }], tt: 'None' };
        }
        // DR 召唤
        if ((m = b.match(/list\.Add\(new SimEntity\{Atk=(\d+),Health=(\d+)/)))
            return { effects: [{ type: 'summon', atk: +m[1], hp: +m[2] }], tt: 'None' };
        // DR 抽牌
        if ((m = b.match(/FriendCardDraw\+=(\d+)/)))
            return { effects: [{ type: 'draw', n: +m[1] }], tt: 'None' };
        // DR 护甲
        if ((m = b.match(/Armor\+=(\d+)/)))
            return { effects: [{ type: 'armor', v: +m[1] }], tt: 'None' };
        // DR 治疗英雄
        if ((m = b.match(/Health=Math\.Min.*Health\+(\d+)/)))
            return { effects: [{ type: 'heal_hero', v: +m[1] }], tt: 'None' };
        // DR 消灭敌方随从
        if (b.includes('es[0].Health=0') || b.includes('enemies[0].Health=0'))
            return { effects: [{ type: 'destroy' }], tt: 'None' };
        // DR buff手牌
        if ((m = b.match(/c\.Atk\+=(\d+);c\.Health\+=(\d+)/)))
            return { effects: [{ type: 'buff_all', atk: +m[1], hp: +m[2] }], tt: 'None' };
        // DR 装备武器
        if ((m = b.match(/FriendWeapon=new SimEntity\{Atk=(\d+),Health=(\d+)/)))
            return { effects: [{ type: 'equip', atk: +m[1], dur: +m[2] }], tt: 'None' };
        // EOT buff other
        if ((m = b.match(/list\[idx\]\.Atk\+=(\d+)/)))
            return { effects: [{ type: 'buff', atk: +m[1], hp: 0 }], tt: 'None' };
        if ((m = b.match(/list\[idx\]\.Health\+=(\d+)/)))
            return { effects: [{ type: 'buff', atk: 0, hp: +m[1] }], tt: 'None' };
    }

    // 英雄攻击力
    if ((m = b.match(/FriendHero\.Atk=(\d+)/)) && !b.includes('FriendWeapon'))
        return { effects: [{ type: 'hero_atk', v: +m[1] }], tt: 'None' };

    // SpellPower 随从
    if (b.includes('SpellPower=1'))
        return { effects: [{ type: 'summon', atk: 0, hp: 1 }], tt: 'None' };

    // 返回 noop 作为兜底
    return { effects: [{ type: 'noop' }], tt: 'None' };
}

// 收集 RegisterTargetType 调用
const targetTypeOverrides = {};
for (const line of regCalls) {
    const tt = parseTargetTypeCall(line);
    if (tt) {
        const key = `${tt.id}|${tt.trigger}`;
        targetTypeOverrides[key] = tt.targetType;
    }
}

// 处理 db.Register 调用
let parsed = 0, unparsed = 0;
for (const line of regCalls) {
    const reg = parseRegisterCall(line);
    if (!reg) continue;

    const { effects, tt } = parseLambdaBody(reg.body, reg.trigger);
    const key = `${reg.id}|${reg.trigger}`;
    const targetType = targetTypeOverrides[key] || tt;

    setEffect(reg.id, reg.trigger, targetType, effects);
    parsed++;
}

console.log(`Step 2: RegisterOverrides → ${parsed} effects parsed`);

// ════════════════════════════════════════════════════════════
// Step 3: 写入 JSON 文件
// ════════════════════════════════════════════════════════════

// 获取已存在的 JSON 文件（不覆盖）
const existingFiles = new Set();
if (fs.existsSync(EFFECTS_DIR)) {
    for (const f of fs.readdirSync(EFFECTS_DIR)) {
        if (f.endsWith('.json')) existingFiles.add(f.replace('.json', ''));
    }
}

let written = 0, skipped = 0;
for (const [cardId, triggers] of Object.entries(result)) {
    if (existingFiles.has(cardId)) { skipped++; continue; }

    const json = {};
    const card = cardMap[cardId];
    if (card) {
        json._name = card.name || cardId;
        json._effect = strip(card.text || '');
    } else {
        json._name = cardId;
    }

    for (const [trigger, data] of Object.entries(triggers)) {
        json[trigger] = {
            targetType: data.targetType,
            effects: data.effects
        };
    }

    const filePath = path.join(EFFECTS_DIR, `${cardId}.json`);
    fs.writeFileSync(filePath, JSON.stringify(json, null, 4) + '\n');
    written++;
}

console.log(`\nSummary:`);
console.log(`  Written: ${written} new JSON files`);
console.log(`  Skipped: ${skipped} (already exist)`);
console.log(`  Total cards: ${Object.keys(result).length}`);
console.log(`  Existing files preserved: ${existingFiles.size}`);
