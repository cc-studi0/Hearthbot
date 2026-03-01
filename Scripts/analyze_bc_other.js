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

// bc_other 细分
const cats = {};
for (const c of valid) {
  if (!c.text) continue;
  const t = strip(c.text);
  const id = c.id.replace(/[^a-zA-Z0-9_]/g,'_');
  if (registered.has(id)) continue;
  if (!/Battlecry/i.test(t)) continue;

  const bc = t.replace(/^.*?Battlecry:\s*/i, '');
  let cat = 'bc_misc';

  if (/Give .*\+\d+ Health/i.test(bc)) cat = 'bc_give_hp';
  else if (/Give .*\+\d+ Attack/i.test(bc)) cat = 'bc_give_atk';
  else if (/Gain \+\d+ Attack/i.test(bc)) cat = 'bc_gain_atk';
  else if (/Give .*Taunt/i.test(bc)) cat = 'bc_give_taunt2';
  else if (/Give .*Rush/i.test(bc)) cat = 'bc_give_rush';
  else if (/Give .*Lifesteal/i.test(bc)) cat = 'bc_give_lifesteal';
  else if (/Give .*Windfury/i.test(bc)) cat = 'bc_give_windfury';
  else if (/Add .* to your hand/i.test(bc)) cat = 'bc_add_hand';
  else if (/Discover/i.test(bc)) cat = 'bc_discover';
  else if (/Transform/i.test(bc)) cat = 'bc_transform';
  else if (/Gain Mana|empty Mana/i.test(bc)) cat = 'bc_mana';
  else if (/Swap/i.test(bc)) cat = 'bc_swap';
  else if (/Copy/i.test(bc)) cat = 'bc_copy';
  else if (/Set .* hero.*Health to (\d+)/i.test(bc)) cat = 'bc_set_hero_hp';
  else if (/Reduce/i.test(bc) && /Cost/i.test(bc)) cat = 'bc_cost_reduce';
  else if (/Steal/i.test(bc)) cat = 'bc_steal';
  else if (/Put .* into/i.test(bc)) cat = 'bc_put';
  else if (/Destroy/i.test(bc)) cat = 'bc_destroy2';
  else if (/Restore/i.test(bc)) cat = 'bc_restore';
  else if (/If .* holding/i.test(bc) || /If .* played/i.test(bc)) cat = 'bc_conditional';

  if (!cats[cat]) cats[cat] = [];
  cats[cat].push(`${c.id}|${bc.substring(0,70)}`);
}

const sorted = Object.entries(cats).sort((a,b)=>b[1].length-a[1].length);
for (const [k,v] of sorted) {
  console.log(`\n=== ${k} (${v.length}) ===`);
  v.slice(0,4).forEach(x => console.log('  '+x));
}
