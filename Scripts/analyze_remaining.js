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

// 分析未注册卡牌的效果模式
const patterns = {};
for (const c of valid) {
  if (!c.text) continue;
  const t = strip(c.text);
  const id = c.id.replace(/[^a-zA-Z0-9_]/g,'_');
  if (registered.has(id)) continue;

  let cat = 'other';
  if (/Secret:/i.test(t)) cat = 'secret';
  else if (/Whenever/i.test(t)) cat = 'whenever';
  else if (/After /i.test(t)) cat = 'after';
  else if (/Give .+\+\d+\/\+\d+/i.test(t)) cat = 'buff_give';
  else if (/Gain \+\d+\/\+\d+/i.test(t)) cat = 'buff_gain';
  else if (/\+\d+ Attack/i.test(t)) cat = 'buff_atk';
  else if (/Discover/i.test(t)) cat = 'discover';
  else if (/Transform/i.test(t)) cat = 'transform';
  else if (/Return/i.test(t) && /hand/i.test(t)) cat = 'bounce';
  else if (/Overload/i.test(t)) cat = 'overload';
  else if (/Costs? \(\d+\) less/i.test(t)) cat = 'cost_reduce';
  else if (/Taunt/i.test(t) && !/Battlecry|Deathrattle|Combo/i.test(t)) cat = 'taunt_only';
  else if (/Choose One/i.test(t)) cat = 'choose_one';
  else if (/Adapt/i.test(t)) cat = 'adapt';
  else if (/Recruit/i.test(t)) cat = 'recruit';
  else if (/Invoke/i.test(t)) cat = 'invoke';
  else if (/Corrupt/i.test(t)) cat = 'corrupt';
  else if (/Tradeable/i.test(t)) cat = 'tradeable';
  else if (/Lifesteal/i.test(t)) cat = 'lifesteal_only';
  else if (/Rush/i.test(t) && !/Battlecry|Deathrattle/i.test(t)) cat = 'rush_only';
  else if (/Charge/i.test(t) && !/Battlecry|Deathrattle/i.test(t)) cat = 'charge_only';
  else if (/Spell Damage/i.test(t)) cat = 'spell_damage';
  else if (/Stealth/i.test(t)) cat = 'stealth_only';
  else if (/Add .+ to your hand/i.test(t)) cat = 'add_to_hand';
  else if (/Set .+ Health/i.test(t) || /Set .+ Attack/i.test(t)) cat = 'set_stats';
  else if (/Swap/i.test(t)) cat = 'swap';
  else if (/Copy/i.test(t)) cat = 'copy';
  else if (/Equip/i.test(t)) cat = 'equip_weapon';
  else if (c.type === 'HERO') cat = 'hero_card';
  else if (c.type === 'LOCATION') cat = 'location';

  patterns[cat] = (patterns[cat]||0)+1;
}
const sorted = Object.entries(patterns).sort((a,b)=>b[1]-a[1]);
sorted.forEach(([k,v]) => console.log(`${k}: ${v}`));
console.log('Total unregistered:', sorted.reduce((s,x)=>s+x[1],0));
