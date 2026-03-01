const fs = require('fs');
const effects = JSON.parse(fs.readFileSync('./effects_data.json', 'utf8'));
const cards = JSON.parse(fs.readFileSync('./.playwright-mcp/hs-cards-all.json', 'utf8'));

const existingIds = new Set();
for (const cat in effects) {
  for (const entry of effects[cat]) {
    existingIds.add(Array.isArray(entry) ? entry[0] : entry);
  }
}

function clean(t) {
  return (t || '').replace(/<[^>]+>/g, '').replace(/\$/g, '').replace(/\n/g, '').trim();
}

const missing = cards.filter(c => 
  !existingIds.has(c.id) && c.type !== 'HERO' && c.type !== 'LOCATION'
);

// 分析哪些卡牌可以用"默认效果"近似
// 1. 纯关键词随从（嘲讽、圣盾等）- 不需要注册效果，关键词已在 SimEntity 上
// 2. 有战吼但效果无法模拟 - 可以给一个基于费用的近似效果
// 3. 有亡语但效果无法模拟 - 同上
// 4. 被动/光环效果 - 无法模拟，但可以给一个评估加分

// 统计：如果我们对所有未分类的战吼卡给一个"按费用近似伤害"的效果
const bcCards = missing.filter(c => {
  const t = clean(c.text || '');
  const mechs = c.mechanics || [];
  return (mechs.includes('BATTLECRY') || t.includes('战吼')) && c.type === 'MINION';
});

const drCards = missing.filter(c => {
  const t = clean(c.text || '');
  const mechs = c.mechanics || [];
  return (mechs.includes('DEATHRATTLE') || t.includes('亡语')) && c.type === 'MINION';
});

const spellCards = missing.filter(c => c.type === 'SPELL' && clean(c.text || ''));

const weaponCards = missing.filter(c => c.type === 'WEAPON' && clean(c.text || ''));

const vanillaMinions = missing.filter(c => c.type === 'MINION' && !clean(c.text || ''));

const passiveMinions = missing.filter(c => {
  const t = clean(c.text || '');
  const mechs = c.mechanics || [];
  return c.type === 'MINION' && t && 
    !mechs.includes('BATTLECRY') && !t.includes('战吼') &&
    !mechs.includes('DEATHRATTLE') && !t.includes('亡语') &&
    !mechs.includes('COMBO') && !t.includes('连击');
});

console.log('Missing breakdown:');
console.log(`  Battlecry minions: ${bcCards.length}`);
console.log(`  Deathrattle minions: ${drCards.length}`);
console.log(`  Spells: ${spellCards.length}`);
console.log(`  Weapons: ${weaponCards.length}`);
console.log(`  Vanilla minions (no text): ${vanillaMinions.length}`);
console.log(`  Passive/aura minions: ${passiveMinions.length}`);
console.log(`  Total: ${missing.length}`);

// 看看被动随从的文本
console.log('\nSample passive minions:');
passiveMinions.slice(0, 20).forEach(c => {
  console.log(`  ${c.id} [${c.cost}] ${c.name}: ${clean(c.text).substring(0, 70)}`);
});

// 看看未分类法术
console.log('\nSample unclassified spells:');
spellCards.slice(0, 20).forEach(c => {
  console.log(`  ${c.id} [${c.cost}] ${c.name}: ${clean(c.text).substring(0, 70)}`);
});
