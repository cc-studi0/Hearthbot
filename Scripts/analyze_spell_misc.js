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

// 看看 spell_misc 里有什么模式
const cats = {};
for (const c of valid) {
  if (!c.text) continue;
  const t = strip(c.text);
  const id = c.id.replace(/[^a-zA-Z0-9_]/g,'_');
  if (registered.has(id)) continue;
  if (c.type !== 'SPELL') continue;

  let cat = 'misc';
  if (/Secret:/i.test(t)) continue;
  if (/Discover/i.test(t)) continue;
  if (/Choose One/i.test(t)) continue;

  if (/Shuffle .* into your deck/i.test(t)) cat = 'spell_shuffle';
  else if (/Gain (\d+) Armor/i.test(t)) cat = 'spell_armor2';
  else if (/Destroy all/i.test(t)) cat = 'spell_destroy_all2';
  else if (/Destroy .*minion/i.test(t)) cat = 'spell_destroy2';
  else if (/Deal \$?(\d+) damage.*Deal \$?(\d+)/i.test(t)) cat = 'spell_multi_dmg';
  else if (/Deal \$?(\d+) damage/i.test(t)) cat = 'spell_dmg2';
  else if (/Freeze/i.test(t)) cat = 'spell_freeze2';
  else if (/Silence/i.test(t)) cat = 'spell_silence2';
  else if (/Summon/i.test(t)) cat = 'spell_summon2';
  else if (/Draw/i.test(t)) cat = 'spell_draw2';
  else if (/Gain Mana|Mana Crystal/i.test(t)) cat = 'spell_mana';
  else if (/Overload/i.test(t)) cat = 'spell_overload';

  if (!cats[cat]) cats[cat] = [];
  cats[cat].push(`${c.id}|${t.substring(0,70)}`);
}

const sorted = Object.entries(cats).sort((a,b)=>b[1].length-a[1].length);
for (const [k,v] of sorted) {
  console.log(`\n=== ${k} (${v.length}) ===`);
  v.slice(0,3).forEach(x => console.log('  '+x));
}
