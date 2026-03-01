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

// 看看 buff_atk 里都是什么
const cats = {buff_atk:[], buff_give:[], buff_gain:[], set_stats:[], bounce:[], equip_weapon:[]};
for (const c of valid) {
  if (!c.text) continue;
  const t = strip(c.text);
  const id = c.id.replace(/[^a-zA-Z0-9_]/g,'_');
  if (registered.has(id)) continue;

  if (/\+\d+ Attack/i.test(t) && !/Whenever|After |Secret/i.test(t)) {
    cats.buff_atk.push(`${c.type}|${c.id}|${t.substring(0,80)}`);
  }
  else if (/Give .+\+\d+\/\+\d+/i.test(t) && !/Whenever|After |Secret/i.test(t)) {
    cats.buff_give.push(`${c.type}|${c.id}|${t.substring(0,80)}`);
  }
  else if (/Gain \+\d+\/\+\d+/i.test(t) && !/Whenever|After |Secret/i.test(t)) {
    cats.buff_gain.push(`${c.type}|${c.id}|${t.substring(0,80)}`);
  }
  else if ((/Set .* Health/i.test(t) || /Set .* Attack/i.test(t)) && !/Whenever|After |Secret/i.test(t)) {
    cats.set_stats.push(`${c.type}|${c.id}|${t.substring(0,80)}`);
  }
  else if (/Return.*hand/i.test(t) && !/Whenever|After |Secret/i.test(t)) {
    cats.bounce.push(`${c.type}|${c.id}|${t.substring(0,80)}`);
  }
  else if (/Equip/i.test(t) && !/Whenever|After |Secret/i.test(t)) {
    cats.equip_weapon.push(`${c.type}|${c.id}|${t.substring(0,80)}`);
  }
}

for (const [k,v] of Object.entries(cats)) {
  if (v.length === 0) continue;
  console.log(`\n=== ${k} (${v.length}) ===`);
  v.slice(0,8).forEach(x => console.log('  '+x));
}
