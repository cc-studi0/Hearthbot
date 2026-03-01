const fs = require('fs');
const cards = JSON.parse(fs.readFileSync('h:/桌面/炉石脚本/cards.json','utf8'));
const dominated = new Set();
cards.forEach(c => { if (/^(CORE_|VAN_|Core_|LEG_)/.test(c.id)) dominated.add(c.id); });
const valid = cards.filter(c => !dominated.has(c.id));

const cs = fs.readFileSync('h:/桌面/炉石脚本/BotMain/AI/CardEffectDB.cs','utf8');
const registered = new Set();
const re = /C\.([A-Za-z0-9_]+)/g;
let m;
while ((m = re.exec(cs)) !== null) registered.add(m[1]);

function strip(t) { return t ? t.replace(/<[^>]+>/g,'').replace(/\[x\]/g,'').replace(/\n/g,' ').replace(/\s+/g,' ').trim() : ''; }

// 找出有 buff/give 效果但未注册的卡
const missed = {bc_buff2:[], spell_buff2:[], bc_give_taunt:[], bc_give_ds:[],
  spell_give_taunt:[], spell_give_ds:[], bc_gain_buff:[], spell_gain_buff:[],
  bc_atk_buff:[], spell_atk_buff:[], spell_set_hp:[], spell_heal_all:[],
  bc_heal_hero:[], spell_heal_hero:[], bc_restore_all:[]};

for (const c of valid) {
  if (!c.text) continue;
  const t = strip(c.text);
  const id = c.id.replace(/[^a-zA-Z0-9_]/g,'_');
  if (registered.has(id)) continue;

  // 战吼 Give +X/+Y (更宽泛)
  if (c.type==='MINION' && /Battlecry/i.test(t)) {
    const bc = t.replace(/^.*?Battlecry:\s*/i,'');
    let bm;
    if ((bm = bc.match(/Give .*\+(\d+)\/\+(\d+)/i))) {
      missed.bc_buff2.push(`${c.id}|${c.name}|${bc.substring(0,60)}`);
    }
    else if (/Give .*Taunt/i.test(bc)) missed.bc_give_taunt.push(`${c.id}|${bc.substring(0,60)}`);
    else if (/Give .*Divine Shield/i.test(bc)) missed.bc_give_ds.push(`${c.id}|${bc.substring(0,60)}`);
    else if ((bm = bc.match(/Gain \+(\d+)\/\+(\d+)/i))) missed.bc_gain_buff.push(`${c.id}|${bc.substring(0,60)}`);
    else if ((bm = bc.match(/Gain \+(\d+) Attack/i))) missed.bc_atk_buff.push(`${c.id}|${bc.substring(0,60)}`);
    else if (/Restore (\d+) Health to your hero/i.test(bc)) missed.bc_heal_hero.push(`${c.id}|${bc.substring(0,60)}`);
    else if (/Restore all/i.test(bc) || /fully heal/i.test(bc)) missed.bc_restore_all.push(`${c.id}|${bc.substring(0,60)}`);
  }
  // 法术 Give +X/+Y (更宽泛)
  if (c.type==='SPELL') {
    let bm;
    if ((bm = t.match(/Give .*\+(\d+)\/\+(\d+)/i))) {
      missed.spell_buff2.push(`${c.id}|${c.name}|${t.substring(0,60)}`);
    }
    else if (/Give .*Taunt/i.test(t)) missed.spell_give_taunt.push(`${c.id}|${t.substring(0,60)}`);
    else if (/Give .*Divine Shield/i.test(t)) missed.spell_give_ds.push(`${c.id}|${t.substring(0,60)}`);
    else if ((bm = t.match(/Gain \+(\d+)\/\+(\d+)/i))) missed.spell_gain_buff.push(`${c.id}|${t.substring(0,60)}`);
    else if ((bm = t.match(/Gain \+(\d+) Attack/i))) missed.spell_atk_buff.push(`${c.id}|${t.substring(0,60)}`);
    else if (/Set a .*Health to (\d+)/i.test(t)) missed.spell_set_hp.push(`${c.id}|${t.substring(0,60)}`);
    else if (/Restore (\d+) Health to your hero/i.test(t)) missed.spell_heal_hero.push(`${c.id}|${t.substring(0,60)}`);
    else if (/Restore (\d+) Health to all friendly/i.test(t)) missed.spell_heal_all.push(`${c.id}|${t.substring(0,60)}`);
  }
}

for (const [k,v] of Object.entries(missed)) {
  if (v.length === 0) continue;
  console.log(`\n=== ${k} (${v.length}) ===`);
  v.slice(0,5).forEach(x => console.log('  '+x));
}
