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

const reasons = {};
for (const c of valid) {
  if (!c.text) continue;
  const t = strip(c.text);
  const id = c.id.replace(/[^a-zA-Z0-9_]/g,'_');
  if (registered.has(id)) continue;

  if (/Deal \d+ damage/i.test(t) || /Destroy/i.test(t) || /Freeze/i.test(t) ||
      /Silence/i.test(t) || /Restore \d+ Health/i.test(t) || /Draw/i.test(t) ||
      /Gain \d+ Armor/i.test(t) || /Summon/i.test(t)) {
    let reason = 'unknown';
    if (/Combo:/i.test(t)) reason = 'combo';
    else if (/Inspire:/i.test(t)) reason = 'inspire';
    else if (/Secret:/i.test(t)) reason = 'secret';
    else if (/Battlecry.*Summon/i.test(t)) reason = 'bc_summon';
    else if (/Deathrattle.*Summon/i.test(t) && c.type==='MINION') reason = 'dr_summon_missed';
    else if (/Deathrattle.*Freeze/i.test(t)) reason = 'dr_freeze';
    else if (/Deathrattle.*Deal/i.test(t)) reason = 'dr_dmg_missed';
    else if (/Deathrattle.*Draw/i.test(t)) reason = 'dr_draw_missed';
    else if (c.type==='SPELL' && /Summon/i.test(t)) reason = 'spell_summon';
    else if (c.type==='MINION' && /Battlecry.*Deal/i.test(t)) reason = 'bc_dmg_missed';
    else if (c.type==='MINION' && /Battlecry.*Draw/i.test(t)) reason = 'bc_draw_missed';
    else if (c.type==='MINION' && /Battlecry.*Destroy/i.test(t)) reason = 'bc_destroy_missed';
    else if (c.type==='SPELL' && /Deal.*damage/i.test(t)) reason = 'spell_dmg_missed';
    else if (c.type==='SPELL' && /Draw/i.test(t)) reason = 'spell_draw_missed';
    else if (c.type==='SPELL' && /Destroy/i.test(t)) reason = 'spell_destroy_missed';
    else if (c.type==='SPELL' && /Freeze/i.test(t)) reason = 'spell_freeze_missed';
    else if (c.type==='SPELL' && /Restore/i.test(t)) reason = 'spell_heal_missed';
    else if (/When.*draw/i.test(t)) reason = 'when_drawn';
    else if (/Whenever/i.test(t)) reason = 'whenever_trigger';
    else if (/After/i.test(t)) reason = 'after_trigger';
    else if (c.type==='WEAPON') reason = 'weapon_effect';
    else if (c.type==='HERO') reason = 'hero_card';
    reasons[reason] = (reasons[reason]||0)+1;
  }
}
const sorted = Object.entries(reasons).sort((a,b)=>b[1]-a[1]);
sorted.forEach(([k,v]) => console.log(k+':', v));
console.log('Total:', sorted.reduce((s,x)=>s+x[1],0));
