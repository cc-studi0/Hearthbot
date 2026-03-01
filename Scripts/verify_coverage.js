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
console.log('已注册卡牌ID数:', registered.size);

function strip(t) { return t ? t.replace(/<[^>]+>/g,'').replace(/\[x\]/g,'').replace(/\n/g,' ').trim() : ''; }

let withText = 0, withEffect = 0, missed = 0;
for (const c of valid) {
  if (!c.text) continue;
  withText++;
  const t = strip(c.text);
  const id = c.id.replace(/[^a-zA-Z0-9_]/g,'_');
  if (registered.has(id)) { withEffect++; continue; }
}
console.log('有文本的卡牌:', withText);
console.log('已注册效果:', withEffect);
console.log('覆盖率:', (withEffect/withText*100).toFixed(1) + '%');
console.log('总卡牌(去重):', valid.length);
console.log('无文本(白板):', valid.length - withText);
