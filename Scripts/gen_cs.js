const fs = require('fs');
const data = JSON.parse(fs.readFileSync('h:/桌面/炉石脚本/effects_data.json', 'utf8'));
function toEnum(id) { return `C.${id.replace(/[^a-zA-Z0-9_]/g, '_')}`; }
let lines = [];
function L(s) { lines.push(s); }

L('using System;');
L('using System.Collections.Generic;');
L('using System.Linq;');
L('using SmartBot.Plugins.API;');
L('using C = SmartBot.Plugins.API.Card.Cards;');
L('');
L('namespace BotMain.AI');
L('{');
L('    public enum EffectTrigger { Battlecry, Deathrattle, Spell, EndOfTurn, Aura }');
L('');
L('    public class CardEffectDB');
L('    {');
L('        private readonly Dictionary<(Card.Cards, EffectTrigger), Action<SimBoard, SimEntity, SimEntity>> _db = new();');
L('');
L('        public void Register(Card.Cards id, EffectTrigger t, Action<SimBoard, SimEntity, SimEntity> fn)');
L('            => _db[(id, t)] = fn;');
L('');
L('        public bool TryGet(Card.Cards id, EffectTrigger t, out Action<SimBoard, SimEntity, SimEntity> fn)');
L('            => _db.TryGetValue((id, t), out fn);');
L('');
L('        public bool Has(Card.Cards id, EffectTrigger t) => _db.ContainsKey((id, t));');
L('');
L('        public static CardEffectDB BuildDefault()');
L('        {');
L('            var db = new CardEffectDB();');
L('            RegisterSpells(db);');
L('            RegisterBattlecries(db);');
L('            RegisterDeathrattles(db);');
L('            RegisterEndOfTurn(db);');
L('            RegisterHeroes(db);');
L('            RegisterLocations(db);');
L('            RegisterWeapons(db);');
L('            return db;');
L('        }');
L('');
L('        private static int SP(SimBoard b) { int sp=0; foreach(var m in b.FriendMinions) sp+=m.SpellPower; return sp; }');
L('        private static void Dmg(SimBoard b, SimEntity t, int d) { if(t==null||t.IsImmune||d<=0) return; if(t.IsDivineShield){t.IsDivineShield=false;return;} t.Health-=d; }');
L('        private static void Silence(SimEntity t) { if(t==null) return; t.IsTaunt=false;t.IsDivineShield=false;t.HasPoison=false;t.IsLifeSteal=false;t.IsWindfury=false;t.HasReborn=false;t.IsSilenced=true;t.SpellPower=0; }');
L('');

// ===== 法术 =====
L('        private static void RegisterSpells(CardEffectDB db)');
L('        {');

// 单体伤害
L('            // 单体伤害');
for (const [id, dmg, sp] of data.spell_dmg) {
  if (sp) L(`            db.Register(${toEnum(id)},EffectTrigger.Spell,(b,s,t)=>{if(t!=null)Dmg(b,t,${dmg}+SP(b));});`);
  else L(`            db.Register(${toEnum(id)},EffectTrigger.Spell,(b,s,t)=>{if(t!=null)Dmg(b,t,${dmg});});`);
}

// AOE全随从
L('            // AOE全随从');
for (const [id, dmg, sp] of data.spell_aoe_all) {
  if (sp) L(`            db.Register(${toEnum(id)},EffectTrigger.Spell,(b,s,t)=>{int d=${dmg}+SP(b);foreach(var m in b.FriendMinions.ToArray())Dmg(b,m,d);foreach(var m in b.EnemyMinions.ToArray())Dmg(b,m,d);});`);
  else L(`            db.Register(${toEnum(id)},EffectTrigger.Spell,(b,s,t)=>{foreach(var m in b.FriendMinions.ToArray())Dmg(b,m,${dmg});foreach(var m in b.EnemyMinions.ToArray())Dmg(b,m,${dmg});});`);
}

// AOE敌方+英雄
L('            // AOE敌方+英雄');
for (const [id, dmg, sp] of data.spell_aoe_enemies_heroes) {
  if (sp) L(`            db.Register(${toEnum(id)},EffectTrigger.Spell,(b,s,t)=>{int d=${dmg}+SP(b);foreach(var m in b.EnemyMinions.ToArray())Dmg(b,m,d);if(b.EnemyHero!=null)Dmg(b,b.EnemyHero,d);});`);
  else L(`            db.Register(${toEnum(id)},EffectTrigger.Spell,(b,s,t)=>{foreach(var m in b.EnemyMinions.ToArray())Dmg(b,m,${dmg});if(b.EnemyHero!=null)Dmg(b,b.EnemyHero,${dmg});});`);
}

// 对英雄
L('            // 对英雄');
for (const [id, dmg, sp] of data.spell_face) {
  if (sp) L(`            db.Register(${toEnum(id)},EffectTrigger.Spell,(b,s,t)=>{if(b.EnemyHero!=null)Dmg(b,b.EnemyHero,${dmg}+SP(b));});`);
  else L(`            db.Register(${toEnum(id)},EffectTrigger.Spell,(b,s,t)=>{if(b.EnemyHero!=null)Dmg(b,b.EnemyHero,${dmg});});`);
}

// 随机敌方
L('            // 随机敌方');
for (const [id, dmg, sp] of data.spell_random_enemy) {
  if (sp) L(`            db.Register(${toEnum(id)},EffectTrigger.Spell,(b,s,t)=>{var ts=new List<SimEntity>(b.EnemyMinions);if(b.EnemyHero!=null)ts.Add(b.EnemyHero);if(ts.Count>0)Dmg(b,ts[0],${dmg}+SP(b));});`);
  else L(`            db.Register(${toEnum(id)},EffectTrigger.Spell,(b,s,t)=>{var ts=new List<SimEntity>(b.EnemyMinions);if(b.EnemyHero!=null)ts.Add(b.EnemyHero);if(ts.Count>0)Dmg(b,ts[0],${dmg});});`);
}

// 冻结
L('            // 冻结');
for (const [id] of data.spell_freeze) L(`            db.Register(${toEnum(id)},EffectTrigger.Spell,(b,s,t)=>{if(t!=null)t.IsFrozen=true;});`);
for (const [id] of data.spell_freeze_all) L(`            db.Register(${toEnum(id)},EffectTrigger.Spell,(b,s,t)=>{foreach(var m in b.EnemyMinions)m.IsFrozen=true;if(b.EnemyHero!=null)b.EnemyHero.IsFrozen=true;});`);

// 消灭
L('            // 消灭');
for (const [id] of data.spell_destroy) L(`            db.Register(${toEnum(id)},EffectTrigger.Spell,(b,s,t)=>{if(t!=null)t.Health=0;});`);
for (const [id] of data.spell_destroy_all) L(`            db.Register(${toEnum(id)},EffectTrigger.Spell,(b,s,t)=>{foreach(var m in b.FriendMinions.ToArray())m.Health=0;foreach(var m in b.EnemyMinions.ToArray())m.Health=0;});`);

// 沉默
L('            // 沉默');
for (const [id] of data.spell_silence) L(`            db.Register(${toEnum(id)},EffectTrigger.Spell,(b,s,t)=>{Silence(t);});`);

// 治疗
L('            // 治疗');
for (const [id, amt] of data.spell_heal) L(`            db.Register(${toEnum(id)},EffectTrigger.Spell,(b,s,t)=>{if(t!=null)t.Health=Math.Min(t.MaxHealth,t.Health+${amt});});`);

// 抽牌
L('            // 抽牌');
for (const [id, cnt] of data.spell_draw) L(`            db.Register(${toEnum(id)},EffectTrigger.Spell,(b,s,t)=>{b.FriendCardDraw+=${cnt};});`);

// 护甲
L('            // 护甲');
for (const [id, amt] of data.spell_armor) L(`            db.Register(${toEnum(id)},EffectTrigger.Spell,(b,s,t)=>{if(b.FriendHero!=null)b.FriendHero.Armor+=${amt};});`);

// 召唤
L('            // 召唤');
for (const [id, atk, hp] of data.spell_summon) L(`            db.Register(${toEnum(id)},EffectTrigger.Spell,(b,s,t)=>{if(b.FriendMinions.Count<7)b.FriendMinions.Add(new SimEntity{Atk=${atk},Health=${hp},MaxHealth=${hp},IsFriend=true,IsTired=true});});`);

// buff单体
if (data.spell_buff && data.spell_buff.length) {
  L('            // buff单体');
  for (const [id, a, h] of data.spell_buff) L(`            db.Register(${toEnum(id)},EffectTrigger.Spell,(b,s,t)=>{if(t!=null){t.Atk+=${a};t.Health+=${h};t.MaxHealth+=${h};}});`);
}

// buff全体友方
if (data.spell_buff_all && data.spell_buff_all.length) {
  L('            // buff全体友方');
  for (const [id, a, h] of data.spell_buff_all) L(`            db.Register(${toEnum(id)},EffectTrigger.Spell,(b,s,t)=>{foreach(var m in b.FriendMinions){m.Atk+=${a};m.Health+=${h};m.MaxHealth+=${h};}});`);
}

// 给予嘲讽
if (data.spell_give_taunt && data.spell_give_taunt.length) {
  L('            // 给予嘲讽');
  for (const [id] of data.spell_give_taunt) L(`            db.Register(${toEnum(id)},EffectTrigger.Spell,(b,s,t)=>{if(t!=null)t.IsTaunt=true;});`);
}

// 给予圣盾
if (data.spell_give_ds && data.spell_give_ds.length) {
  L('            // 给予圣盾');
  for (const [id] of data.spell_give_ds) L(`            db.Register(${toEnum(id)},EffectTrigger.Spell,(b,s,t)=>{if(t!=null)t.IsDivineShield=true;});`);
}

// +X攻击力
if (data.spell_atk_buff && data.spell_atk_buff.length) {
  L('            // +X攻击力');
  for (const [id, a] of data.spell_atk_buff) L(`            db.Register(${toEnum(id)},EffectTrigger.Spell,(b,s,t)=>{if(t!=null)t.Atk+=${a};});`);
}

// 设置生命值
if (data.spell_set_hp && data.spell_set_hp.length) {
  L('            // 设置生命值');
  for (const [id, hp] of data.spell_set_hp) L(`            db.Register(${toEnum(id)},EffectTrigger.Spell,(b,s,t)=>{if(t!=null){t.Health=${hp};t.MaxHealth=${hp};}});`);
}

// 治疗英雄
if (data.spell_heal_hero && data.spell_heal_hero.length) {
  L('            // 治疗英雄');
  for (const [id, amt] of data.spell_heal_hero) L(`            db.Register(${toEnum(id)},EffectTrigger.Spell,(b,s,t)=>{if(b.FriendHero!=null)b.FriendHero.Health=Math.Min(b.FriendHero.MaxHealth,b.FriendHero.Health+${amt});});`);
}

// 治疗所有友方
if (data.spell_heal_all && data.spell_heal_all.length) {
  L('            // 治疗所有友方');
  for (const [id, amt] of data.spell_heal_all) L(`            db.Register(${toEnum(id)},EffectTrigger.Spell,(b,s,t)=>{foreach(var m in b.FriendMinions)m.Health=Math.Min(m.MaxHealth,m.Health+${amt});if(b.FriendHero!=null)b.FriendHero.Health=Math.Min(b.FriendHero.MaxHealth,b.FriendHero.Health+${amt});});`);
}

// +X生命值
if (data.spell_give_hp && data.spell_give_hp.length) {
  L('            // +X生命值');
  for (const [id, h] of data.spell_give_hp) L(`            db.Register(${toEnum(id)},EffectTrigger.Spell,(b,s,t)=>{if(t!=null){t.Health+=${h};t.MaxHealth+=${h};}});`);
}

// 给予疾风
if (data.spell_give_windfury && data.spell_give_windfury.length) {
  L('            // 给予疾风');
  for (const [id] of data.spell_give_windfury) L(`            db.Register(${toEnum(id)},EffectTrigger.Spell,(b,s,t)=>{if(t!=null)t.IsWindfury=true;});`);
}

// 给予吸血
if (data.spell_give_lifesteal && data.spell_give_lifesteal.length) {
  L('            // 给予吸血');
  for (const [id] of data.spell_give_lifesteal) L(`            db.Register(${toEnum(id)},EffectTrigger.Spell,(b,s,t)=>{if(t!=null)t.IsLifeSteal=true;});`);
}

// 给予突袭
if (data.spell_give_rush && data.spell_give_rush.length) {
  L('            // 给予突袭');
  for (const [id] of data.spell_give_rush) L(`            db.Register(${toEnum(id)},EffectTrigger.Spell,(b,s,t)=>{if(t!=null)t.HasRush=true;});`);
}

// 加牌到手牌
if (data.spell_add_hand && data.spell_add_hand.length) {
  L('            // 加牌到手牌');
  for (const [id, cnt] of data.spell_add_hand) L(`            db.Register(${toEnum(id)},EffectTrigger.Spell,(b,s,t)=>{b.FriendCardDraw+=${cnt};});`);
}

// 弹回
if (data.spell_bounce && data.spell_bounce.length) {
  L('            // 弹回');
  for (const [id] of data.spell_bounce) L(`            db.Register(${toEnum(id)},EffectTrigger.Spell,(b,s,t)=>{if(t!=null){if(t.IsFriend){b.FriendMinions.Remove(t);b.Hand.Add(t);}else{b.EnemyMinions.Remove(t);}}});`);
}

// 装备武器
if (data.spell_equip && data.spell_equip.length) {
  L('            // 装备武器');
  for (const [id, a, h] of data.spell_equip) L(`            db.Register(${toEnum(id)},EffectTrigger.Spell,(b,s,t)=>{b.FriendWeapon=new SimEntity{Atk=${a},Health=${h},MaxHealth=${h},IsFriend=true};if(b.FriendHero!=null)b.FriendHero.Atk=${a};});`);
}

L('        }');
L('');

// ===== 战吼 =====
L('        private static void RegisterBattlecries(CardEffectDB db)');
L('        {');

L('            // 单体伤害');
for (const [id, dmg] of data.bc_dmg) L(`            db.Register(${toEnum(id)},EffectTrigger.Battlecry,(b,s,t)=>{if(t!=null)Dmg(b,t,${dmg});});`);

L('            // AOE全随从');
for (const [id, dmg] of data.bc_aoe_all) L(`            db.Register(${toEnum(id)},EffectTrigger.Battlecry,(b,s,t)=>{foreach(var m in b.FriendMinions.ToArray())if(m!=s)Dmg(b,m,${dmg});foreach(var m in b.EnemyMinions.ToArray())Dmg(b,m,${dmg});});`);

L('            // AOE敌方+英雄');
for (const [id, dmg] of data.bc_aoe_enemies_heroes) L(`            db.Register(${toEnum(id)},EffectTrigger.Battlecry,(b,s,t)=>{foreach(var m in b.EnemyMinions.ToArray())Dmg(b,m,${dmg});if(b.EnemyHero!=null)Dmg(b,b.EnemyHero,${dmg});});`);

L('            // 对英雄');
for (const [id, dmg] of data.bc_face) L(`            db.Register(${toEnum(id)},EffectTrigger.Battlecry,(b,s,t)=>{if(b.EnemyHero!=null)Dmg(b,b.EnemyHero,${dmg});});`);

L('            // 随机敌方');
for (const [id, dmg] of data.bc_random_enemy) L(`            db.Register(${toEnum(id)},EffectTrigger.Battlecry,(b,s,t)=>{var ts=new List<SimEntity>(b.EnemyMinions);if(b.EnemyHero!=null)ts.Add(b.EnemyHero);if(ts.Count>0)Dmg(b,ts[0],${dmg});});`);

L('            // 消灭');
for (const [id] of data.bc_destroy) L(`            db.Register(${toEnum(id)},EffectTrigger.Battlecry,(b,s,t)=>{if(t!=null)t.Health=0;});`);
for (const [id] of data.bc_destroy_all) L(`            db.Register(${toEnum(id)},EffectTrigger.Battlecry,(b,s,t)=>{foreach(var m in b.FriendMinions.ToArray())if(m!=s)m.Health=0;foreach(var m in b.EnemyMinions.ToArray())m.Health=0;});`);
for (const [id] of data.bc_destroy_weapon) L(`            db.Register(${toEnum(id)},EffectTrigger.Battlecry,(b,s,t)=>{b.EnemyWeapon=null;});`);

L('            // 沉默');
for (const [id] of data.bc_silence) L(`            db.Register(${toEnum(id)},EffectTrigger.Battlecry,(b,s,t)=>{Silence(t);});`);

L('            // 冻结');
for (const [id] of data.bc_freeze) L(`            db.Register(${toEnum(id)},EffectTrigger.Battlecry,(b,s,t)=>{if(t!=null)t.IsFrozen=true;});`);
for (const [id] of data.bc_freeze_all) L(`            db.Register(${toEnum(id)},EffectTrigger.Battlecry,(b,s,t)=>{foreach(var m in b.EnemyMinions)m.IsFrozen=true;if(b.EnemyHero!=null)b.EnemyHero.IsFrozen=true;});`);

L('            // 治疗');
for (const [id, amt] of data.bc_heal) L(`            db.Register(${toEnum(id)},EffectTrigger.Battlecry,(b,s,t)=>{if(t!=null)t.Health=Math.Min(t.MaxHealth,t.Health+${amt});});`);

L('            // 抽牌');
for (const [id, cnt] of data.bc_draw) L(`            db.Register(${toEnum(id)},EffectTrigger.Battlecry,(b,s,t)=>{b.FriendCardDraw+=${cnt};});`);

L('            // 护甲');
for (const [id, amt] of data.bc_armor) L(`            db.Register(${toEnum(id)},EffectTrigger.Battlecry,(b,s,t)=>{if(b.FriendHero!=null)b.FriendHero.Armor+=${amt};});`);

L('            // 召唤');
for (const [id, atk, hp] of data.bc_summon) L(`            db.Register(${toEnum(id)},EffectTrigger.Battlecry,(b,s,t)=>{if(b.FriendMinions.Count<7)b.FriendMinions.Add(new SimEntity{Atk=${atk},Health=${hp},MaxHealth=${hp},IsFriend=true,IsTired=true});});`);

L('            // 连击伤害');
for (const [id, dmg] of data.combo_dmg) L(`            db.Register(${toEnum(id)},EffectTrigger.Battlecry,(b,s,t)=>{if(b.CardsPlayedThisTurn>0&&t!=null)Dmg(b,t,${dmg});});`);

// 连击buff
if (data.combo_buff && data.combo_buff.length) {
  L('            // 连击buff');
  for (const [id, a, h] of data.combo_buff) L(`            db.Register(${toEnum(id)},EffectTrigger.Battlecry,(b,s,t)=>{if(b.CardsPlayedThisTurn>0){s.Atk+=${a};s.Health+=${h};s.MaxHealth+=${h};}});`);
}

// 连击攻击力
if (data.combo_atk && data.combo_atk.length) {
  L('            // 连击攻击力');
  for (const [id, a] of data.combo_atk) L(`            db.Register(${toEnum(id)},EffectTrigger.Battlecry,(b,s,t)=>{if(b.CardsPlayedThisTurn>0)s.Atk+=${a};});`);
}

// 连击抽牌
if (data.combo_draw && data.combo_draw.length) {
  L('            // 连击抽牌');
  for (const [id, cnt] of data.combo_draw) L(`            db.Register(${toEnum(id)},EffectTrigger.Battlecry,(b,s,t)=>{if(b.CardsPlayedThisTurn>0)b.FriendCardDraw+=${cnt};});`);
}

// 连击弹回
if (data.combo_bounce && data.combo_bounce.length) {
  L('            // 连击弹回');
  for (const [id] of data.combo_bounce) L(`            db.Register(${toEnum(id)},EffectTrigger.Battlecry,(b,s,t)=>{if(b.CardsPlayedThisTurn>0&&t!=null){if(t.IsFriend){b.FriendMinions.Remove(t);b.Hand.Add(t);}else{b.EnemyMinions.Remove(t);}}});`);
}

// buff单体
if (data.bc_buff && data.bc_buff.length) {
  L('            // buff单体');
  for (const [id, a, h] of data.bc_buff) L(`            db.Register(${toEnum(id)},EffectTrigger.Battlecry,(b,s,t)=>{if(t!=null){t.Atk+=${a};t.Health+=${h};t.MaxHealth+=${h};}});`);
}

// buff全体友方
if (data.bc_buff_all && data.bc_buff_all.length) {
  L('            // buff全体友方');
  for (const [id, a, h] of data.bc_buff_all) L(`            db.Register(${toEnum(id)},EffectTrigger.Battlecry,(b,s,t)=>{foreach(var m in b.FriendMinions)if(m!=s){m.Atk+=${a};m.Health+=${h};m.MaxHealth+=${h};}});`);
}

// 自身增益 Gain +X/+Y
if (data.bc_gain_buff && data.bc_gain_buff.length) {
  L('            // 自身增益');
  for (const [id, a, h] of data.bc_gain_buff) L(`            db.Register(${toEnum(id)},EffectTrigger.Battlecry,(b,s,t)=>{s.Atk+=${a};s.Health+=${h};s.MaxHealth+=${h};});`);
}

// 给予嘲讽
if (data.bc_give_taunt && data.bc_give_taunt.length) {
  L('            // 给予嘲讽');
  for (const [id] of data.bc_give_taunt) L(`            db.Register(${toEnum(id)},EffectTrigger.Battlecry,(b,s,t)=>{if(t!=null)t.IsTaunt=true;});`);
}

// 给予圣盾
if (data.bc_give_ds && data.bc_give_ds.length) {
  L('            // 给予圣盾');
  for (const [id] of data.bc_give_ds) L(`            db.Register(${toEnum(id)},EffectTrigger.Battlecry,(b,s,t)=>{if(t!=null)t.IsDivineShield=true;});`);
}

// +X攻击力
if (data.bc_atk_buff && data.bc_atk_buff.length) {
  L('            // +X攻击力');
  for (const [id, a] of data.bc_atk_buff) L(`            db.Register(${toEnum(id)},EffectTrigger.Battlecry,(b,s,t)=>{if(t!=null)t.Atk+=${a};});`);
}

// +X生命值
if (data.bc_give_hp && data.bc_give_hp.length) {
  L('            // +X生命值');
  for (const [id, h] of data.bc_give_hp) L(`            db.Register(${toEnum(id)},EffectTrigger.Battlecry,(b,s,t)=>{if(t!=null){t.Health+=${h};t.MaxHealth+=${h};}});`);
}

// 给予疾风
if (data.bc_give_windfury && data.bc_give_windfury.length) {
  L('            // 给予疾风');
  for (const [id] of data.bc_give_windfury) L(`            db.Register(${toEnum(id)},EffectTrigger.Battlecry,(b,s,t)=>{if(t!=null)t.IsWindfury=true;});`);
}

// 给予突袭
if (data.bc_give_rush && data.bc_give_rush.length) {
  L('            // 给予突袭');
  for (const [id] of data.bc_give_rush) L(`            db.Register(${toEnum(id)},EffectTrigger.Battlecry,(b,s,t)=>{if(t!=null)t.HasRush=true;});`);
}

// 设置英雄血量
if (data.bc_set_hero_hp && data.bc_set_hero_hp.length) {
  L('            // 设置英雄血量');
  for (const [id, hp] of data.bc_set_hero_hp) L(`            db.Register(${toEnum(id)},EffectTrigger.Battlecry,(b,s,t)=>{if(b.EnemyHero!=null){b.EnemyHero.Health=${hp};b.EnemyHero.MaxHealth=Math.Max(b.EnemyHero.MaxHealth,${hp});}});`);
}

// 加牌到手牌（当抽牌处理）
if (data.bc_add_hand && data.bc_add_hand.length) {
  L('            // 加牌到手牌');
  for (const [id, cnt] of data.bc_add_hand) L(`            db.Register(${toEnum(id)},EffectTrigger.Battlecry,(b,s,t)=>{b.FriendCardDraw+=${cnt};});`);
}

// 治疗英雄
if (data.bc_heal_hero && data.bc_heal_hero.length) {
  L('            // 治疗英雄');
  for (const [id, amt] of data.bc_heal_hero) L(`            db.Register(${toEnum(id)},EffectTrigger.Battlecry,(b,s,t)=>{if(b.FriendHero!=null)b.FriendHero.Health=Math.Min(b.FriendHero.MaxHealth,b.FriendHero.Health+${amt});});`);
}

// 完全恢复
if (data.bc_restore_all && data.bc_restore_all.length) {
  L('            // 完全恢复');
  for (const [id] of data.bc_restore_all) L(`            db.Register(${toEnum(id)},EffectTrigger.Battlecry,(b,s,t)=>{if(t!=null)t.Health=t.MaxHealth;});`);
}

// 弹回
if (data.bc_bounce && data.bc_bounce.length) {
  L('            // 弹回');
  for (const [id] of data.bc_bounce) L(`            db.Register(${toEnum(id)},EffectTrigger.Battlecry,(b,s,t)=>{if(t!=null){if(t.IsFriend){b.FriendMinions.Remove(t);b.Hand.Add(t);}else{b.EnemyMinions.Remove(t);}}});`);
}

// 装备武器
if (data.bc_equip && data.bc_equip.length) {
  L('            // 装备武器');
  for (const [id, a, h] of data.bc_equip) L(`            db.Register(${toEnum(id)},EffectTrigger.Battlecry,(b,s,t)=>{b.FriendWeapon=new SimEntity{Atk=${a},Health=${h},MaxHealth=${h},IsFriend=true};if(b.FriendHero!=null)b.FriendHero.Atk=${a};});`);
}

// 设置生命值
if (data.bc_set_hp && data.bc_set_hp.length) {
  L('            // 设置生命值');
  for (const [id, hp] of data.bc_set_hp) L(`            db.Register(${toEnum(id)},EffectTrigger.Battlecry,(b,s,t)=>{if(t!=null){t.Health=${hp};t.MaxHealth=${hp};}});`);
}

L('        }');
L('');

// ===== 亡语 =====
L('        private static void RegisterDeathrattles(CardEffectDB db)');
L('        {');

L('            // 对敌方英雄');
for (const [id, dmg] of data.dr_dmg_enemy_hero) L(`            db.Register(${toEnum(id)},EffectTrigger.Deathrattle,(b,s,t)=>{if(s.IsFriend&&b.EnemyHero!=null)Dmg(b,b.EnemyHero,${dmg});else if(!s.IsFriend&&b.FriendHero!=null)Dmg(b,b.FriendHero,${dmg});});`);

L('            // 随机敌方');
if (data.dr_dmg_random) {
  for (const [id, dmg] of data.dr_dmg_random) L(`            db.Register(${toEnum(id)},EffectTrigger.Deathrattle,(b,s,t)=>{var es=s.IsFriend?b.EnemyMinions:b.FriendMinions;var hero=s.IsFriend?b.EnemyHero:b.FriendHero;var ts=new List<SimEntity>(es);if(hero!=null)ts.Add(hero);if(ts.Count>0)Dmg(b,ts[0],${dmg});});`);
}

L('            // AOE敌方随从');
for (const [id, dmg] of data.dr_aoe_enemy) L(`            db.Register(${toEnum(id)},EffectTrigger.Deathrattle,(b,s,t)=>{var list=s.IsFriend?b.EnemyMinions:b.FriendMinions;foreach(var m in list.ToArray())Dmg(b,m,${dmg});});`);

L('            // AOE全随从');
for (const [id, dmg] of data.dr_aoe_all) L(`            db.Register(${toEnum(id)},EffectTrigger.Deathrattle,(b,s,t)=>{foreach(var m in b.FriendMinions.ToArray())Dmg(b,m,${dmg});foreach(var m in b.EnemyMinions.ToArray())Dmg(b,m,${dmg});});`);

L('            // 冻结');
if (data.dr_freeze) {
  for (const [id] of data.dr_freeze) L(`            db.Register(${toEnum(id)},EffectTrigger.Deathrattle,(b,s,t)=>{var es=s.IsFriend?b.EnemyMinions:b.FriendMinions;foreach(var m in es)m.IsFrozen=true;});`);
}

L('            // 抽牌');
for (const [id, cnt] of data.dr_draw) L(`            db.Register(${toEnum(id)},EffectTrigger.Deathrattle,(b,s,t)=>{if(s.IsFriend)b.FriendCardDraw+=${cnt};});`);

L('            // 护甲');
for (const [id, amt] of data.dr_armor) L(`            db.Register(${toEnum(id)},EffectTrigger.Deathrattle,(b,s,t)=>{if(s.IsFriend&&b.FriendHero!=null)b.FriendHero.Armor+=${amt};});`);

L('            // 召唤');
for (const [id, atk, hp] of data.dr_summon) L(`            db.Register(${toEnum(id)},EffectTrigger.Deathrattle,(b,s,t)=>{var list=s.IsFriend?b.FriendMinions:b.EnemyMinions;if(list.Count<7)list.Add(new SimEntity{Atk=${atk},Health=${hp},MaxHealth=${hp},IsFriend=s.IsFriend,IsTired=true});});`);

// 亡语装备武器
if (data.dr_equip && data.dr_equip.length) {
  L('            // 亡语装备武器');
  for (const [id, a, h] of data.dr_equip) L(`            db.Register(${toEnum(id)},EffectTrigger.Deathrattle,(b,s,t)=>{if(s.IsFriend){b.FriendWeapon=new SimEntity{Atk=${a},Health=${h},MaxHealth=${h},IsFriend=true};if(b.FriendHero!=null)b.FriendHero.Atk=${a};}});`);
}

// 亡语buff手牌
if (data.dr_buff_hand && data.dr_buff_hand.length) {
  L('            // 亡语buff手牌');
  for (const [id, a, h] of data.dr_buff_hand) L(`            db.Register(${toEnum(id)},EffectTrigger.Deathrattle,(b,s,t)=>{if(s.IsFriend)foreach(var c in b.Hand){c.Atk+=${a};c.Health+=${h};c.MaxHealth+=${h};}});`);
}

// 亡语加牌
if (data.dr_add_hand && data.dr_add_hand.length) {
  L('            // 亡语加牌');
  for (const [id, cnt] of data.dr_add_hand) L(`            db.Register(${toEnum(id)},EffectTrigger.Deathrattle,(b,s,t)=>{if(s.IsFriend)b.FriendCardDraw+=${cnt};});`);
}

// 亡语治疗
if (data.dr_restore && data.dr_restore.length) {
  L('            // 亡语治疗');
  for (const [id, amt] of data.dr_restore) L(`            db.Register(${toEnum(id)},EffectTrigger.Deathrattle,(b,s,t)=>{if(s.IsFriend&&b.FriendHero!=null)b.FriendHero.Health=Math.Min(b.FriendHero.MaxHealth,b.FriendHero.Health+${amt});});`);
}

// 亡语消灭随机敌方
if (data.dr_destroy && data.dr_destroy.length) {
  L('            // 亡语消灭随机敌方');
  for (const [id] of data.dr_destroy) L(`            db.Register(${toEnum(id)},EffectTrigger.Deathrattle,(b,s,t)=>{var es=s.IsFriend?b.EnemyMinions:b.FriendMinions;if(es.Count>0)es[0].Health=0;});`);
}

L('        }');
L('');

// ===== 回合结束 =====
L('        private static void RegisterEndOfTurn(CardEffectDB db)');
L('        {');

L('            // 随机敌方');
for (const [id, dmg] of data.eot_dmg_random) L(`            db.Register(${toEnum(id)},EffectTrigger.EndOfTurn,(b,s,t)=>{var es=s.IsFriend?b.EnemyMinions:b.FriendMinions;var hero=s.IsFriend?b.EnemyHero:b.FriendHero;var ts=new List<SimEntity>(es);if(hero!=null)ts.Add(hero);if(ts.Count>0)Dmg(b,ts[0],${dmg});});`);

L('            // AOE敌方');
for (const [id, dmg] of data.eot_aoe_enemy) L(`            db.Register(${toEnum(id)},EffectTrigger.EndOfTurn,(b,s,t)=>{var es=s.IsFriend?b.EnemyMinions:b.FriendMinions;foreach(var m in es.ToArray())Dmg(b,m,${dmg});});`);

L('            // 抽牌');
for (const [id, cnt] of data.eot_draw) L(`            db.Register(${toEnum(id)},EffectTrigger.EndOfTurn,(b,s,t)=>{if(s.IsFriend)b.FriendCardDraw+=${cnt};});`);

L('            // 护甲');
for (const [id, amt] of data.eot_armor) L(`            db.Register(${toEnum(id)},EffectTrigger.EndOfTurn,(b,s,t)=>{if(s.IsFriend&&b.FriendHero!=null)b.FriendHero.Armor+=${amt};});`);

if (data.eot_heal) {
  L('            // 治疗');
  for (const [id, amt] of data.eot_heal) L(`            db.Register(${toEnum(id)},EffectTrigger.EndOfTurn,(b,s,t)=>{if(s.IsFriend&&b.FriendHero!=null)b.FriendHero.Health=Math.Min(b.FriendHero.MaxHealth,b.FriendHero.Health+${amt});});`);
}

L('            // 召唤');
for (const [id, atk, hp] of data.eot_summon) L(`            db.Register(${toEnum(id)},EffectTrigger.EndOfTurn,(b,s,t)=>{var list=s.IsFriend?b.FriendMinions:b.EnemyMinions;if(list.Count<7)list.Add(new SimEntity{Atk=${atk},Health=${hp},MaxHealth=${hp},IsFriend=s.IsFriend,IsTired=true});});`);

L('            // 自身增益');
for (const [id, a, h] of data.eot_self_buff) L(`            db.Register(${toEnum(id)},EffectTrigger.EndOfTurn,(b,s,t)=>{s.Atk+=${a};s.Health+=${h};s.MaxHealth+=${h};});`);

// 回合结束加牌
if (data.eot_add_hand && data.eot_add_hand.length) {
  L('            // 回合结束加牌');
  for (const [id, cnt] of data.eot_add_hand) L(`            db.Register(${toEnum(id)},EffectTrigger.EndOfTurn,(b,s,t)=>{if(s.IsFriend)b.FriendCardDraw+=${cnt};});`);
}

// 回合结束治疗
if (data.eot_restore && data.eot_restore.length) {
  L('            // 回合结束治疗');
  for (const [id, amt] of data.eot_restore) L(`            db.Register(${toEnum(id)},EffectTrigger.EndOfTurn,(b,s,t)=>{if(s.IsFriend&&b.FriendHero!=null)b.FriendHero.Health=Math.Min(b.FriendHero.MaxHealth,b.FriendHero.Health+${amt});});`);
}

// 回合结束给攻击力
if (data.eot_buff_atk && data.eot_buff_atk.length) {
  L('            // 回合结束给攻击力');
  for (const [id, a] of data.eot_buff_atk) L(`            db.Register(${toEnum(id)},EffectTrigger.EndOfTurn,(b,s,t)=>{if(s.IsFriend){var list=b.FriendMinions;if(list.Count>1){var idx=0;if(list[0]==s&&list.Count>1)idx=1;list[idx].Atk+=${a};}}});`);
}

// 回合结束给生命值
if (data.eot_buff_hp && data.eot_buff_hp.length) {
  L('            // 回合结束给生命值');
  for (const [id, h] of data.eot_buff_hp) L(`            db.Register(${toEnum(id)},EffectTrigger.EndOfTurn,(b,s,t)=>{if(s.IsFriend){var list=b.FriendMinions;if(list.Count>1){var idx=0;if(list[0]==s&&list.Count>1)idx=1;list[idx].Health+=${h};list[idx].MaxHealth+=${h};}}});`);
}

L('            // 末日预言者');
L('            db.Register(C.NEW1_021,EffectTrigger.EndOfTurn,(b,s,t)=>{if(!s.IsFriend){b.FriendMinions.Clear();b.EnemyMinions.Clear();}});');

L('        }');
L('    }');
L('}');

fs.writeFileSync('h:/桌面/炉石脚本/BotMain/AI/CardEffectDB.cs', lines.join('\n'), 'utf8');
console.log(`Generated ${lines.length} lines`);
