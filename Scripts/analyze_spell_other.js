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

const cats = {};
for (const c of valid) {
  if (!c.text) continue;
  const t = strip(c.text);
  const id = c.id.replace(/[^a-zA-Z0-9_]/g,'_');
  if (registered.has(id)) continue;
  if (c.type !== 'SPELL') continue;

  let cat = 'spell_misc';
  if (/Secret:/i.test(t)) cat = 'spell_secret';
  else if (/Discover/i.test(t)) cat = 'spell_discover';
  else if (/Transform/i.test(t)) cat = 'spell_transform';
  else if (/Choose One/i.test(t)) cat = 'spell_choose';
  else if (/Add .* to your hand/i.test(t)) cat = 'spell_add_hand';
  else if (/Copy/i.test(t)) cat = 'spell_copy';
  else if (/Swap/i.test(t)) cat = 'spell_swap';
  else if (/Costs? \(\d+\) less/i.test(t)) cat = 'spell_cost_reduce';
  else if (/Reduce/i.test(t) && /Cost/i.test(t)) cat = 'spell_cost_reduce2';
  else if (/Give .*Taunt/i.test(t)) cat = 'spell_give_taunt2';
  else if (/Give .*Rush/i.test(t)) cat = 'spell_give_rush';
  else if (/Give .*Windfury/i.test(t)) cat = 'spell_give_windfury';
  else if (/Give .*Lifesteal/i.test(t)) cat = 'spell_give_lifesteal';
  else if (/Give .*\+\d+ Health/i.test(t)) cat = 'spell_give_hp';
  else if (/Gain \+\d+ Attack/i.test(t)) cat = 'spell_gain_atk';
  else if (/Destroy/i.test(t)) cat = 'spell_destroy2';
  else if (/Restore/i.test(t)) cat = 'spell_restore2';
  else if (/Overload/i.test(t)) cat = 'spell_overload';
  else if (/Recruit/i.test(t)) cat = 'spell_recruit';

  if (!cats[cat]) cats[cat] = [];
  cats[cat].push(`${c.id}|${t.substring(0,70)}`);
}

const sorted = Object.entries(cats).sort((a,b)=>b[1].length-a[1].length);
for (const [k,v] of sorted) {
  console.log(`\n=== ${k} (${v.length}) ===`);
  v.slice(0,4).forEach(x => console.log('  '+x));
}
