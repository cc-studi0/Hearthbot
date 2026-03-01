const fs = require('fs');
const cards = JSON.parse(fs.readFileSync('cards.json','utf8'));
function strip(t) { return t ? t.replace(/<[^>]+>/g,'').replace(/\[x\]/g,'').replace(/\n/g,' ').replace(/\s+/g,' ').trim() : ''; }
for (const c of cards) {
  if (!c.text) continue;
  const t = strip(c.text);
  if (/Battlecry/i.test(t) && /Restore.*Health.*hero/i.test(t)) {
    const bc = t.replace(/^.*?Battlecry:\s*/i, '');
    console.log(c.id, '|', bc.substring(0,60));
  }
}
