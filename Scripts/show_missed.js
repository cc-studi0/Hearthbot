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

function strip(t) { return t ? t.replace(/<[^>]+>/g,'').replace(/\[x\]/g,'').replace(/\n/g,' ').trim() : ''; }

// 看各类遗漏的具体例子
const categories = {
  spell_summon: [], bc_summon: [], combo: [], bc_draw_missed: [],
  spell_draw_missed: [], bc_destroy_missed: [], spell_destroy_missed: [],
  dr_draw_missed: [], dr_dmg_missed: [], spell_dmg_missed: [], dr_freeze: [],
  spell_freeze_missed: [], bc_dmg_missed: []
};

for (const c of valid) {
  if (!c.text) continue;
  const t = strip(c.text);
  const id = c.id.replace(/[^a-zA-Z0-9_]/g,'_');
  if (registered.has(id)) continue;

  const entry = `${c.id} | ${c.name} | ${t.substring(0,100)}`;

  if (/Combo:.*Deal \d+/i.test(t)) { categories.combo.push(entry); continue; }
  if (c.type==='SPELL' && /Summon/i.test(t)) { categories.spell_summon.push(entry); continue; }
  if (/Battlecry.*Summon/i.test(t)) { categories.bc_summon.push(entry); continue; }
  if (c.type==='MINION' && /Battlecry.*Draw/i.test(t)) { categories.bc_draw_missed.push(entry); continue; }
  if (c.type==='SPELL' && /Draw/i.test(t)) { categories.spell_draw_missed.push(entry); continue; }
  if (c.type==='MINION' && /Battlecry.*Destroy/i.test(t)) { categories.bc_destroy_missed.push(entry); continue; }
  if (c.type==='SPELL' && /Destroy/i.test(t)) { categories.spell_destroy_missed.push(entry); continue; }
  if (/Deathrattle.*Draw/i.test(t)) { categories.dr_draw_missed.push(entry); continue; }
  if (/Deathrattle.*Deal/i.test(t)) { categories.dr_dmg_missed.push(entry); continue; }
  if (c.type==='SPELL' && /Deal.*damage/i.test(t)) { categories.spell_dmg_missed.push(entry); continue; }
  if (/Deathrattle.*Freeze/i.test(t)) { categories.dr_freeze.push(entry); continue; }
  if (c.type==='SPELL' && /Freeze/i.test(t)) { categories.spell_freeze_missed.push(entry); continue; }
}

for (const [cat, items] of Object.entries(categories)) {
  if (items.length === 0) continue;
  console.log(`\n=== ${cat} (${items.length}) ===`);
  items.slice(0,8).forEach(x => console.log('  ' + x));
}
