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

// 所有缺失且有文本的非英雄非地标卡牌
const missing = cards.filter(c => 
  !existingIds.has(c.id) && 
  c.text && clean(c.text) &&
  c.type !== 'HERO' && c.type !== 'LOCATION'
);

// 分析这些卡牌的 mechanics
const mechCount = {};
missing.forEach(c => {
  (c.mechanics || []).forEach(m => {
    mechCount[m] = (mechCount[m] || 0) + 1;
  });
});

console.log('Missing complex cards:', missing.length);
console.log('\nMechanics distribution:');
Object.entries(mechCount).sort((a,b) => b[1]-a[1]).forEach(([k,v]) => {
  console.log(`  ${k}: ${v}`);
});

// 分析效果文本中的动作模式
const patterns = {
  '战吼+伤害': 0, '战吼+治疗': 0, '战吼+抽牌': 0, '战吼+发现': 0,
  '战吼+召唤': 0, '战吼+buff': 0, '战吼+其他': 0,
  '亡语+召唤': 0, '亡语+抽牌': 0, '亡语+伤害': 0, '亡语+其他': 0,
  '每当触发': 0, '光环效果': 0, '条件效果': 0,
  '抉择': 0, '发现': 0, '奥秘': 0, '过载': 0,
  '纯关键词': 0, '法术伤害': 0, '减费': 0,
  '其他': 0
};

missing.forEach(c => {
  const t = clean(c.text);
  const mechs = c.mechanics || [];
  
  if (t.includes('抉择')) { patterns['抉择']++; return; }
  if (mechs.includes('SECRET') || t.includes('奥秘：')) { patterns['奥秘']++; return; }
  if (t.includes('每当') || t.includes('在你') || t.includes('在本回合')) { patterns['每当触发']++; return; }
  if (t.includes('如果') || t.includes('只要')) { patterns['条件效果']++; return; }
  if (t.includes('发现')) { patterns['发现']++; return; }
  if (t.includes('法力值消耗') && t.includes('减少')) { patterns['减费']++; return; }
  
  const hasBc = mechs.includes('BATTLECRY') || t.includes('战吼');
  const hasDr = mechs.includes('DEATHRATTLE') || t.includes('亡语');
  
  if (hasBc) {
    if (t.includes('伤害')) patterns['战吼+伤害']++;
    else if (t.includes('恢复') || t.includes('治疗')) patterns['战吼+治疗']++;
    else if (t.includes('抽')) patterns['战吼+抽牌']++;
    else if (t.includes('召唤')) patterns['战吼+召唤']++;
    else if (t.match(/[+＋]\d+/)) patterns['战吼+buff']++;
    else patterns['战吼+其他']++;
    return;
  }
  if (hasDr) {
    if (t.includes('召唤')) patterns['亡语+召唤']++;
    else if (t.includes('抽')) patterns['亡语+抽牌']++;
    else if (t.includes('伤害')) patterns['亡语+伤害']++;
    else patterns['亡语+其他']++;
    return;
  }
  
  // 纯关键词/光环
  if (t.match(/^(嘲讽|圣盾|突袭|冲锋|风怒|吸血|剧毒|潜行|法术伤害)/)) {
    patterns['纯关键词']++;
    return;
  }
  if (t.includes('法术伤害')) { patterns['法术伤害']++; return; }
  if (t.includes('过载')) { patterns['过载']++; return; }
  
  patterns['其他']++;
});

console.log('\nEffect pattern distribution:');
Object.entries(patterns).sort((a,b) => b[1]-a[1]).forEach(([k,v]) => {
  if (v > 0) console.log(`  ${k}: ${v}`);
});

// 看看"战吼+其他"具体是什么
console.log('\nSample "战吼+其他":');
let count = 0;
missing.forEach(c => {
  if (count >= 15) return;
  const t = clean(c.text);
  const mechs = c.mechanics || [];
  const hasBc = mechs.includes('BATTLECRY') || t.includes('战吼');
  if (!hasBc) return;
  if (t.includes('伤害') || t.includes('恢复') || t.includes('治疗') || 
      t.includes('抽') || t.includes('召唤') || t.match(/[+＋]\d+/) ||
      t.includes('每当') || t.includes('如果') || t.includes('抉择') ||
      t.includes('发现') || t.includes('奥秘') || t.includes('在你')) return;
  console.log(`  ${c.id} ${c.name}: ${t.substring(0, 80)}`);
  count++;
});

// 看看"每当触发"具体是什么
console.log('\nSample "每当触发":');
count = 0;
missing.forEach(c => {
  if (count >= 15) return;
  const t = clean(c.text);
  if (t.includes('每当') || t.includes('在你') || t.includes('在本回合')) {
    console.log(`  ${c.id} ${c.name}: ${t.substring(0, 80)}`);
    count++;
  }
});
