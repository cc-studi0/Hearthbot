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

// dr_other 和 eot_other 细分
const cats = {};
for (const c of valid) {
  if (!c.text) continue;
  const t = strip(c.text);
  const id = c.id.replace(/[^a-zA-Z0-9_]/g,'_');
  if (registered.has(id)) continue;

  // 亡语
  if (/Deathrattle/i.test(t)) {
    const dr = t.replace(/^.*?Deathrattle:\s*/i, '');
    let cat = 'dr_misc';
    if (/Add .* to your hand/i.test(dr)) cat = 'dr_add_hand';
    else if (/Return .* hand/i.test(dr)) cat = 'dr_bounce';
    else if (/Destroy/i.test(dr)) cat = 'dr_destroy';
    else if (/Restore/i.test(dr)) cat = 'dr_restore';
    else if (/Discover/i.test(dr)) cat = 'dr_discover';
    else if (/Reduce .* Cost/i.test(dr)) cat = 'dr_cost_reduce';
    else if (/Shuffle/i.test(dr)) cat = 'dr_shuffle';
    else if (/Copy/i.test(dr)) cat = 'dr_copy';
    if (!cats[cat]) cats[cat] = [];
    cats[cat].push(`${c.id}|${dr.substring(0,70)}`);
  }

  // 回合结束
  if (/At the end of (?:your|each) turn/i.test(t)) {
    const eot = t.replace(/^.*?At the end of (?:your|each) turn,?\s*/i, '');
    const eid = c.id.replace(/[^a-zA-Z0-9_]/g,'_');
    if (registered.has(eid)) continue;
    let cat = 'eot_misc';
    if (/Give .*\+\d+\/\+\d+/i.test(eot)) cat = 'eot_buff_friendly';
    else if (/Give .*\+\d+ Attack/i.test(eot)) cat = 'eot_buff_atk';
    else if (/Give .*\+\d+ Health/i.test(eot)) cat = 'eot_buff_hp';
    else if (/Restore/i.test(eot)) cat = 'eot_restore';
    else if (/Add .* to your hand/i.test(eot)) cat = 'eot_add_hand';
    else if (/Reduce/i.test(eot) && /Cost/i.test(eot)) cat = 'eot_cost_reduce';
    else if (/Destroy/i.test(eot)) cat = 'eot_destroy';
    if (!cats[cat]) cats[cat] = [];
    cats[cat].push(`${c.id}|${eot.substring(0,70)}`);
  }
}

const sorted = Object.entries(cats).sort((a,b)=>b[1].length-a[1].length);
for (const [k,v] of sorted) {
  console.log(`\n=== ${k} (${v.length}) ===`);
  v.slice(0,4).forEach(x => console.log('  '+x));
}
