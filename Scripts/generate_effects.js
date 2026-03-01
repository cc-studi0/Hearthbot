const fs = require('fs');
const cards = JSON.parse(fs.readFileSync('h:/桌面/炉石脚本/cards.json', 'utf8'));

const dominated = new Set();
cards.forEach(c => { if (/^(CORE_|VAN_|Core_|LEG_)/.test(c.id)) dominated.add(c.id); });
const valid = cards.filter(c => !dominated.has(c.id));

function strip(t) { return t ? t.replace(/<[^>]+>/g, '').replace(/\[x\]/g, '').replace(/\n/g, ' ').replace(/\s+/g, ' ').trim() : ''; }
function exNum(text, pat) { const m = text.match(pat); return m ? parseInt(m[1]) : null; }
// 去除 Corrupt/Forge/Infuse/Combo 升级文本，只保留基础效果
function stripUpgrade(t) {
  return t.replace(/\s*Corrupt:.*$/i, '')
          .replace(/\s*Forge:.*$/i, '')
          .replace(/\s*Infuse\s*\(\d+\):.*$/i, '')
          .replace(/\s*Combo:.*$/i, '')
          .trim();
}

const R = {
  spell_dmg: [], spell_aoe_all: [], spell_aoe_enemies_heroes: [],
  spell_face: [], spell_random_enemy: [],
  spell_freeze: [], spell_freeze_all: [],
  spell_destroy: [], spell_destroy_all: [],
  spell_silence: [], spell_heal: [], spell_draw: [], spell_armor: [],
  spell_summon: [],
  bc_dmg: [], bc_aoe_all: [], bc_aoe_enemies_heroes: [],
  bc_face: [], bc_random_enemy: [],
  bc_destroy: [], bc_destroy_all: [], bc_destroy_weapon: [],
  bc_silence: [], bc_freeze: [], bc_freeze_all: [],
  bc_heal: [], bc_draw: [], bc_armor: [],
  bc_summon: [],
  dr_dmg_enemy_hero: [], dr_aoe_enemy: [], dr_aoe_all: [],
  dr_draw: [], dr_armor: [], dr_summon: [], dr_freeze: [],
  dr_dmg_random: [],
  eot_dmg_random: [], eot_aoe_enemy: [], eot_aoe_all: [],
  eot_draw: [], eot_armor: [], eot_heal: [],
  eot_summon: [], eot_self_buff: [],
  // 新增类别
  spell_buff: [], spell_bounce: [], spell_equip: [],
  spell_buff_all: [], spell_give_taunt: [], spell_give_ds: [],
  spell_set_hp: [], spell_atk_buff: [], spell_give_hp: [],
  spell_heal_hero: [], spell_heal_all: [],
  spell_give_windfury: [], spell_give_lifesteal: [], spell_give_rush: [],
  spell_add_hand: [],
  bc_buff: [], bc_bounce: [], bc_equip: [], bc_set_hp: [],
  bc_buff_all: [], bc_give_taunt: [], bc_give_ds: [],
  bc_gain_buff: [], bc_atk_buff: [], bc_give_hp: [],
  bc_heal_hero: [], bc_restore_all: [],
  bc_give_windfury: [], bc_give_rush: [], bc_set_hero_hp: [],
  bc_add_hand: [],
  combo_dmg: [], combo_buff: [], combo_atk: [], combo_draw: [], combo_bounce: [],
  dr_equip: [], dr_buff: [], dr_summon_copy: [], dr_buff_hand: [],
  dr_add_hand: [], dr_restore: [], dr_destroy: [],
  eot_buff_friendly: [], eot_add_hand: [], eot_restore: [],
  eot_buff_atk: [], eot_buff_hp: [],
};

for (const c of valid) {
  if (!c.text) continue;
  const raw = c.text;
  const t = strip(raw);
  const id = c.id;
  const hasSP = raw.includes('$');

  // ===== 法术 =====
  if (c.type === 'SPELL') {
    let d;
    let matched = false;
    const t0 = stripUpgrade(t); // 去除升级文本，只用基础效果

    // AOE所有随从
    if ((d = exNum(t0, /Deal \$?(\d+).*?damage to all minions/i)) || (d = exNum(t0, /Deal \$?(\d+).*?damage to all characters/i))) {
      R.spell_aoe_all.push([id, d, hasSP]); matched = true;
    }
    // AOE所有敌方(含英雄)
    if (!matched && (d = exNum(t0, /Deal \$?(\d+).*?damage to all enem/i))) {
      R.spell_aoe_enemies_heroes.push([id, d, hasSP]); matched = true;
    }
    // 对英雄
    if (!matched && (d = exNum(t0, /Deal \$?(\d+).*?damage to the enemy hero/i))) {
      R.spell_face.push([id, d, hasSP]); matched = true;
    }
    // 随机敌方
    if (!matched && (d = exNum(t0, /Deal \$?(\d+).*?damage to a random enem/i))) {
      R.spell_random_enemy.push([id, d, hasSP]); matched = true;
    }
    // 消灭全部
    if (!matched && /Destroy all (?:enemy )?minions/i.test(t0)) {
      R.spell_destroy_all.push([id]); matched = true;
    }
    // 消灭单体（更宽泛）
    if (!matched && /Destroy (?:a|an) (?:\w+ )*(?:enemy )?minion/i.test(t0)) {
      R.spell_destroy.push([id]); matched = true;
    }
    // 冻结全部
    if (!matched && /Freeze all enem/i.test(t0)) {
      R.spell_freeze_all.push([id]); matched = true;
    }
    // 冻结单体（更宽泛）
    if (!matched && /Freeze (?:a |an )?(?:\w+ )*(?:enemy|minion|character)/i.test(t0)) {
      R.spell_freeze.push([id]); matched = true;
    }
    // 沉默
    if (!matched && /Silence/i.test(t0) && /minion/i.test(t0)) {
      R.spell_silence.push([id]); matched = true;
    }
    // 单体伤害（最宽泛，放最后）
    if (!matched && (d = exNum(t0, /Deal \$?(\d+) damage/i))) {
      R.spell_dmg.push([id, d, hasSP]); matched = true;
    }
    // 召唤（解析属性）
    if (!matched && /Summon/i.test(t0)) {
      const sm = t0.match(/Summon (?:a |an? |three |two |four )?(\d+)\/(\d+)/i);
      if (sm) R.spell_summon.push([id, parseInt(sm[1]), parseInt(sm[2])]);
      else {
        const sm2 = t0.match(/(\d+)\/(\d+)/);
        if (sm2) R.spell_summon.push([id, parseInt(sm2[1]), parseInt(sm2[2])]);
        else R.spell_summon.push([id, 1, 1]);
      }
      matched = true;
    }
    // 治疗（支持 #N 格式）
    if (!matched && (d = exNum(t0, /Restore #?(\d+) Health/i))) {
      R.spell_heal.push([id, d]); matched = true;
    }
    // 抽牌（更宽泛）
    if (!matched && /Draw/i.test(t0)) {
      d = exNum(t0, /Draw (\d+)/i) || 1;
      R.spell_draw.push([id, d]); matched = true;
    }
    // 护甲
    if (!matched && (d = exNum(t0, /Gain (\d+) Armor/i))) {
      R.spell_armor.push([id, d]); matched = true;
    }
    // buff: Give all +X/+Y
    if (!matched) {
      const bm = t0.match(/Give (?:your |all |all friendly |all your )?(?:minions?|characters?) \+(\d+)\/\+(\d+)/i);
      if (bm) { R.spell_buff_all.push([id, parseInt(bm[1]), parseInt(bm[2])]); matched = true; }
    }
    // buff: Give +X/+Y (单体，宽泛)
    if (!matched) {
      const bm = t0.match(/Give .*\+(\d+)\/\+(\d+)/i);
      if (bm) { R.spell_buff.push([id, parseInt(bm[1]), parseInt(bm[2])]); matched = true; }
    }
    // Give Taunt
    if (!matched && /Give .*Taunt/i.test(t0)) {
      R.spell_give_taunt.push([id]); matched = true;
    }
    // Give Divine Shield
    if (!matched && /Give .*Divine Shield/i.test(t0)) {
      R.spell_give_ds.push([id]); matched = true;
    }
    // Gain +X/+Y (自身buff)
    if (!matched) {
      const bm = t0.match(/Gain \+(\d+)\/\+(\d+)/i);
      if (bm) { R.spell_buff.push([id, parseInt(bm[1]), parseInt(bm[2])]); matched = true; }
    }
    // +X Attack（用非贪婪匹配第一个出现的数值）
    if (!matched) {
      const bm = t0.match(/Give (?:a )?(?:\w+ )*(?:minion|friendly minion) \+(\d+) Attack/i) || t0.match(/Gain \+(\d+) Attack/i);
      if (bm) { R.spell_atk_buff.push([id, parseInt(bm[1])]); matched = true; }
    }
    // Give +X Health (无攻击力)
    if (!matched) {
      const bm = t0.match(/Give .*\+(\d+) Health/i);
      if (bm) { R.spell_give_hp.push([id, parseInt(bm[1])]); matched = true; }
    }
    // Set Health
    if (!matched) {
      const bm = t0.match(/Set a .*Health to (\d+)/i);
      if (bm) { R.spell_set_hp.push([id, parseInt(bm[1])]); matched = true; }
    }
    // Give Windfury
    if (!matched && /Give .*Windfury/i.test(t0)) {
      R.spell_give_windfury.push([id]); matched = true;
    }
    // Give Lifesteal
    if (!matched && /Give .*Lifesteal/i.test(t0)) {
      R.spell_give_lifesteal.push([id]); matched = true;
    }
    // Give Rush
    if (!matched && /Give .*Rush/i.test(t0)) {
      R.spell_give_rush.push([id]); matched = true;
    }
    // 治疗英雄
    if (!matched && (d = exNum(t0, /Restore (\d+) Health to your hero/i))) {
      R.spell_heal_hero.push([id, d]); matched = true;
    }
    // 治疗所有友方
    if (!matched && (d = exNum(t0, /Restore (\d+) Health to all friendly/i))) {
      R.spell_heal_all.push([id, d]); matched = true;
    }
    // 治疗（更宽泛）
    if (!matched && (d = exNum(t0, /Restore .*(\d+) Health/i))) {
      R.spell_heal.push([id, d]); matched = true;
    }
    // Add to hand (当抽牌处理)
    if (!matched && /Add .* to your hand/i.test(t0)) {
      const cnt = t0.match(/Add (\d+|two|three|four|five)/i);
      let n = 1;
      if (cnt) {
        const w = cnt[1].toLowerCase();
        if (w === 'two') n = 2; else if (w === 'three') n = 3;
        else if (w === 'four') n = 4; else if (w === 'five') n = 5;
        else n = parseInt(w) || 1;
      }
      R.spell_add_hand.push([id, n]); matched = true;
    }
    // 弹回
    if (!matched && /Return (?:a |an )?(?:\w+ )*minion to (?:its owner's |your )?hand/i.test(t0)) {
      R.spell_bounce.push([id]); matched = true;
    }
    // 装备武器
    if (!matched) {
      const wm = t0.match(/Equip a (\d+)\/(\d+)/i);
      if (wm) { R.spell_equip.push([id, parseInt(wm[1]), parseInt(wm[2])]); matched = true; }
    }
  }

  // ===== 随从 =====
  if (c.type === 'MINION' || c.type === 'WEAPON') {
    // --- 战吼 ---
    if (/Battlecry/i.test(t)) {
      const bc = stripUpgrade(t.replace(/^.*?Battlecry:\s*/i, ''));
      let d, matched = false;

      if ((d = exNum(bc, /Deal (\d+).*?damage to all minions/i)) || (d = exNum(bc, /Deal (\d+).*?damage to all characters/i))) {
        R.bc_aoe_all.push([id, d]); matched = true;
      }
      if (!matched && (d = exNum(bc, /Deal (\d+).*?damage to all enem/i))) {
        R.bc_aoe_enemies_heroes.push([id, d]); matched = true;
      }
      if (!matched && (d = exNum(bc, /Deal (\d+).*?damage to the enemy hero/i))) {
        R.bc_face.push([id, d]); matched = true;
      }
      if (!matched && (d = exNum(bc, /Deal (\d+).*?damage to a random enem/i))) {
        R.bc_random_enemy.push([id, d]); matched = true;
      }
      if (!matched && /Destroy all (?:\w+ )*minions/i.test(bc)) {
        R.bc_destroy_all.push([id]); matched = true;
      }
      if (!matched && /Destroy .*(?:enemy )?(?:weapon|opponent's weapon)/i.test(bc)) {
        R.bc_destroy_weapon.push([id]); matched = true;
      }
      if (!matched && /Destroy (?:a|an) (?:\w+ )*(?:enemy )?minion/i.test(bc)) {
        R.bc_destroy.push([id]); matched = true;
      }
      if (!matched && /Freeze all enem/i.test(bc)) {
        R.bc_freeze_all.push([id]); matched = true;
      }
      if (!matched && /Freeze/i.test(bc) && /(?:enemy|minion|character)/i.test(bc)) {
        R.bc_freeze.push([id]); matched = true;
      }
      if (!matched && /Silence/i.test(bc)) {
        R.bc_silence.push([id]); matched = true;
      }
      if (!matched && (d = exNum(bc, /Deal (\d+) damage/i))) {
        R.bc_dmg.push([id, d]); matched = true;
      }
      if (!matched && /Summon/i.test(bc)) {
        const sm = bc.match(/(\d+)\/(\d+)/);
        if (sm) R.bc_summon.push([id, parseInt(sm[1]), parseInt(sm[2])]);
        else R.bc_summon.push([id, 1, 1]);
        matched = true;
      }
      if (!matched && (d = exNum(bc, /Restore (\d+) Health/i))) {
        R.bc_heal.push([id, d]); matched = true;
      }
      if (!matched && /Draw/i.test(bc)) {
        d = exNum(bc, /Draw (\d+)/i) || 1;
        R.bc_draw.push([id, d]); matched = true;
      }
      if (!matched && (d = exNum(bc, /Gain (\d+) Armor/i))) {
        R.bc_armor.push([id, d]); matched = true;
      }
      if (!matched) {
        const bm = bc.match(/Give (?:your |all |all friendly |all your )?(?:minions?|characters?) \+(\d+)\/\+(\d+)/i);
        if (bm) { R.bc_buff_all.push([id, parseInt(bm[1]), parseInt(bm[2])]); matched = true; }
      }
      if (!matched) {
        const bm = bc.match(/Give .*\+(\d+)\/\+(\d+)/i);
        if (bm) { R.bc_buff.push([id, parseInt(bm[1]), parseInt(bm[2])]); matched = true; }
      }
      if (!matched) {
        const bm = bc.match(/Gain \+(\d+)\/\+(\d+)/i);
        if (bm) { R.bc_gain_buff.push([id, parseInt(bm[1]), parseInt(bm[2])]); matched = true; }
      }
      if (!matched && /Give .*Taunt/i.test(bc)) {
        R.bc_give_taunt.push([id]); matched = true;
      }
      if (!matched && /Give .*Divine Shield/i.test(bc)) {
        R.bc_give_ds.push([id]); matched = true;
      }
      if (!matched) {
        const bm = bc.match(/Give .*\+(\d+) Attack/i) || bc.match(/Gain \+(\d+) Attack/i);
        if (bm) { R.bc_atk_buff.push([id, parseInt(bm[1])]); matched = true; }
      }
      // Give +X Health (无攻击力)
      if (!matched) {
        const bm = bc.match(/Give .*\+(\d+) Health/i);
        if (bm) { R.bc_give_hp.push([id, parseInt(bm[1])]); matched = true; }
      }
      if (!matched && (d = exNum(bc, /Restore (\d+) Health to your hero/i))) {
        R.bc_heal_hero.push([id, d]); matched = true;
      }
      // Restore X Health to each hero / all
      if (!matched && (d = exNum(bc, /Restore .*(\d+) Health/i))) {
        R.bc_heal.push([id, d]); matched = true;
      }
      if (!matched && (/Restore all/i.test(bc) || /fully heal/i.test(bc))) {
        R.bc_restore_all.push([id]); matched = true;
      }
      // Give Windfury
      if (!matched && /Give .*Windfury/i.test(bc)) {
        R.bc_give_windfury.push([id]); matched = true;
      }
      // Give Rush
      if (!matched && /Give .*Rush/i.test(bc)) {
        R.bc_give_rush.push([id]); matched = true;
      }
      // Set hero Health
      if (!matched) {
        const hm = bc.match(/Set .*hero.*Health to (\d+)/i);
        if (hm) { R.bc_set_hero_hp.push([id, parseInt(hm[1])]); matched = true; }
      }
      // Add to hand (当抽牌处理)
      if (!matched && /Add .* to your hand/i.test(bc)) {
        const cnt = bc.match(/Add (\d+|two|three|four|five)/i);
        let n = 1;
        if (cnt) {
          const w = cnt[1].toLowerCase();
          if (w === 'two') n = 2; else if (w === 'three') n = 3;
          else if (w === 'four') n = 4; else if (w === 'five') n = 5;
          else n = parseInt(w) || 1;
        }
        R.bc_add_hand.push([id, n]); matched = true;
      }
      if (!matched && /Return/i.test(bc) && /hand/i.test(bc)) {
        R.bc_bounce.push([id]); matched = true;
      }
      if (!matched) {
        const wm = bc.match(/Equip a (\d+)\/(\d+)/i);
        if (wm) { R.bc_equip.push([id, parseInt(wm[1]), parseInt(wm[2])]); matched = true; }
      }
      if (!matched) {
        const sm = bc.match(/Set a (?:\w+ )*(?:minion|character)'?s? Health to (\d+)/i);
        if (sm) { R.bc_set_hp.push([id, parseInt(sm[1])]); matched = true; }
      }
    }

    // --- Combo ---
    if (/Combo:/i.test(t)) {
      const combo = t.replace(/^.*?Combo:\s*/i, '');
      let d, cm;
      if ((d = exNum(combo, /Deal (\d+) damage/i))) {
        R.combo_dmg.push([id, d]);
      }
      else if ((cm = combo.match(/Gain \+(\d+)\/\+(\d+)/i))) {
        R.combo_buff.push([id, parseInt(cm[1]), parseInt(cm[2])]);
      }
      else if ((cm = combo.match(/Gain \+(\d+) Attack/i))) {
        R.combo_atk.push([id, parseInt(cm[1])]);
      }
      else if (/Draw/i.test(combo)) {
        d = exNum(combo, /Draw (\d+)/i) || 1;
        R.combo_draw.push([id, d]);
      }
      else if (/Return/i.test(combo) && /hand/i.test(combo)) {
        R.combo_bounce.push([id]);
      }
      else if ((cm = combo.match(/Give .*\+(\d+)\/\+(\d+)/i))) {
        R.combo_buff.push([id, parseInt(cm[1]), parseInt(cm[2])]);
      }
    }

    // --- 亡语 ---
    if (/Deathrattle/i.test(t)) {
      const dr = stripUpgrade(t.replace(/^.*?Deathrattle:\s*/i, ''));
      let d, matched = false;

      if ((d = exNum(dr, /Deal (\d+).*?damage to all minions/i)) || (d = exNum(dr, /Deal (\d+).*?damage to all characters/i))) {
        R.dr_aoe_all.push([id, d]); matched = true;
      }
      if (!matched && (d = exNum(dr, /Deal (\d+).*?damage to all enem/i))) {
        R.dr_aoe_enemy.push([id, d]); matched = true;
      }
      if (!matched && (d = exNum(dr, /Deal (\d+).*?damage to (?:the )?enemy hero/i))) {
        R.dr_dmg_enemy_hero.push([id, d]); matched = true;
      }
      if (!matched && (d = exNum(dr, /Deal (\d+).*?damage to a random enem/i))) {
        R.dr_dmg_random.push([id, d]); matched = true;
      }
      if (!matched && (d = exNum(dr, /Deal (\d+).*?damage (?:to )?your hero/i))) {
        R.dr_dmg_enemy_hero.push([id, d]); matched = true; // 对自己英雄伤害
      }
      if (!matched && /Freeze/i.test(dr)) {
        R.dr_freeze.push([id]); matched = true;
      }
      if (!matched && /Summon/i.test(dr)) {
        const sm = dr.match(/(\d+)\/(\d+)/);
        if (sm) R.dr_summon.push([id, parseInt(sm[1]), parseInt(sm[2])]);
        else R.dr_summon.push([id, 1, 1]);
        matched = true;
      }
      if (!matched && /Draw/i.test(dr)) {
        d = exNum(dr, /Draw (\d+)/i) || 1;
        R.dr_draw.push([id, d]); matched = true;
      }
      if (!matched && (d = exNum(dr, /Gain (\d+) Armor/i))) {
        R.dr_armor.push([id, d]); matched = true;
      }
      // 亡语装备武器
      if (!matched) {
        const wm = dr.match(/[Ee]quip a (\d+)\/(\d+)/);
        if (wm) { R.dr_equip.push([id, parseInt(wm[1]), parseInt(wm[2])]); matched = true; }
      }
      // 亡语 buff（给手牌中随从+X/+Y）
      if (!matched) {
        const bm = dr.match(/Give .*\+(\d+)\/\+(\d+)/i);
        if (bm) { R.dr_buff_hand.push([id, parseInt(bm[1]), parseInt(bm[2])]); matched = true; }
      }
      // 亡语加牌到手牌
      if (!matched && /Add .* to your hand/i.test(dr)) {
        const cnt = dr.match(/Add (\d+|two|three|four|five)/i);
        let n = 1;
        if (cnt) {
          const w = cnt[1].toLowerCase();
          if (w === 'two') n = 2; else if (w === 'three') n = 3;
          else if (w === 'four') n = 4; else if (w === 'five') n = 5;
          else n = parseInt(w) || 1;
        }
        R.dr_add_hand.push([id, n]); matched = true;
      }
      // 亡语治疗
      if (!matched && (d = exNum(dr, /Restore .*(\d+) Health/i))) {
        R.dr_restore.push([id, d]); matched = true;
      }
      // 亡语消灭随机敌方
      if (!matched && /Destroy a random enemy minion/i.test(dr)) {
        R.dr_destroy.push([id]); matched = true;
      }
    }

    // --- 回合结束 ---
    if (/At the end of (?:your|each) turn/i.test(t)) {
      const eot = stripUpgrade(t.replace(/^.*?At the end of (?:your|each) turn,?\s*/i, ''));
      let d, matched = false;

      if ((d = exNum(eot, /Deal (\d+).*?damage to all minions/i))) {
        R.eot_aoe_all.push([id, d]); matched = true;
      }
      if (!matched && (d = exNum(eot, /Deal (\d+).*?damage to all enem/i))) {
        R.eot_aoe_enemy.push([id, d]); matched = true;
      }
      if (!matched && (d = exNum(eot, /Deal (\d+).*?damage/i))) {
        R.eot_dmg_random.push([id, d]); matched = true;
      }
      if (!matched && /Summon/i.test(eot)) {
        const sm = eot.match(/(\d+)\/(\d+)/);
        if (sm) R.eot_summon.push([id, parseInt(sm[1]), parseInt(sm[2])]);
        else R.eot_summon.push([id, 1, 1]);
        matched = true;
      }
      if (!matched && /\+(\d+)\/\+(\d+)/.test(eot)) {
        const bm = eot.match(/\+(\d+)\/\+(\d+)/);
        R.eot_self_buff.push([id, parseInt(bm[1]), parseInt(bm[2])]); matched = true;
      }
      if (!matched && /Draw/i.test(eot)) {
        d = exNum(eot, /Draw (\d+)/i) || 1;
        R.eot_draw.push([id, d]); matched = true;
      }
      if (!matched && (d = exNum(eot, /Gain (\d+) Armor/i))) {
        R.eot_armor.push([id, d]); matched = true;
      }
      if (!matched && (d = exNum(eot, /Restore (\d+) Health/i))) {
        R.eot_heal.push([id, d]); matched = true;
      }
      // 回合结束加牌
      if (!matched && /Add .* to your hand/i.test(eot)) {
        R.eot_add_hand.push([id, 1]); matched = true;
      }
      // 回合结束治疗（更宽泛）
      if (!matched && (d = exNum(eot, /restore .*(\d+) Health/i))) {
        R.eot_restore.push([id, d]); matched = true;
      }
      // 回合结束给攻击力
      if (!matched) {
        const bm = eot.match(/give .*\+(\d+) Attack/i);
        if (bm) { R.eot_buff_atk.push([id, parseInt(bm[1])]); matched = true; }
      }
      // 回合结束给生命值
      if (!matched) {
        const bm = eot.match(/give .*\+(\d+) Health/i);
        if (bm) { R.eot_buff_hp.push([id, parseInt(bm[1])]); matched = true; }
      }
    }
  }
}

// 统计
let total = 0;
for (const [k, v] of Object.entries(R)) {
  if (v.length > 0) { console.log(`${k}: ${v.length}`); total += v.length; }
}
console.log(`\nTotal: ${total}`);
fs.writeFileSync('h:/桌面/炉石脚本/effects_data.json', JSON.stringify(R, null, 2));
console.log('Saved.');
