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

// 找出 "other" 类别中可能还能模拟的
const cats = {};
for (const c of valid) {
  if (!c.text) continue;
  const t = strip(c.text);
  const id = c.id.replace(/[^a-zA-Z0-9_]/g,'_');
  if (registered.has(id)) continue;

  // 跳过已知不可模拟的
  if (/Secret:|Whenever|After |Discover|Transform|Choose One|Adapt|Recruit|Invoke|Corrupt|Tradeable/i.test(t)) continue;
  if (/Costs? \(\d+\) less/i.test(t)) continue;

  // 看看剩下的有什么模式
  let cat = 'other';
  if (/Battlecry/i.test(t)) cat = 'bc_other';
  else if (/Deathrattle/i.test(t)) cat = 'dr_other';
  else if (/At the end of/i.test(t)) cat = 'eot_other';
  else if (c.type === 'SPELL') cat = 'spell_other';
  else if (/Taunt|Rush|Charge|Stealth|Lifesteal|Divine Shield|Windfury|Spell Damage|Reborn|Poisonous/i.test(t)) cat = 'keyword_only';
  else if (c.type === 'HERO') cat = 'hero';
  else if (c.type === 'LOCATION') cat = 'location';
  else if (c.type === 'WEAPON') cat = 'weapon_other';

  if (!cats[cat]) cats[cat] = [];
  cats[cat].push(`${c.type}|${c.id}|${t.substring(0,80)}`);
}

const sorted = Object.entries(cats).sort((a,b)=>b[1].length-a[1].length);
for (const [k,v] of sorted) {
  console.log(`\n=== ${k} (${v.length}) ===`);
  v.slice(0,5).forEach(x => console.log('  '+x));
}
console.log('\nTotal:', sorted.reduce((s,x)=>s+x[1].length,0));
