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

// 分析缺失卡牌中需要哪些新模板
// 按效果动词分类
const effectVerbs = {};
missing.forEach(c => {
  const t = clean(c.text || '');
  if (!t) return;
  
  // 提取核心动词/效果
  const verbs = [
    '变形', '复制', '交换', '偷取', '夺取控制', '返回', '洗入', '弃掉',
    '减少', '增加', '变为', '替换', '移除', '摧毁', '消灭',
    '免疫', '不能攻击', '不能被法术', '无法使用',
    '过载', '法力水晶', '法力值消耗',
    '随机施放', '再次施放', '触发', '激活',
    '发现', '生成', '获取', '置入',
    '伤害', '恢复', '治疗', '护甲',
    '冻结', '沉默', '消灭', '召唤',
    '嘲讽', '圣盾', '风怒', '吸血', '突袭', '冲锋', '剧毒', '潜行',
    '抽', '弃', '过载',
    '奥秘', '任务', '支线',
    '连击', '抉择', '激励', '回响', '磁力',
    '进化', '退化',
    '法术伤害'
  ];
  
  verbs.forEach(v => {
    if (t.includes(v)) {
      effectVerbs[v] = (effectVerbs[v] || 0) + 1;
    }
  });
});

console.log('Effect verb frequency in missing cards:');
Object.entries(effectVerbs).sort((a,b) => b[1]-a[1]).forEach(([k,v]) => {
  console.log(`  ${k}: ${v}`);
});

// 看看模拟器能表达哪些状态变化
console.log('\n=== SimBoard/SimEntity 可表达的状态变化 ===');
console.log('SimEntity: Atk, Health, MaxHealth, Armor, Cost, SpellPower');
console.log('  bool: IsTaunt, IsDivineShield, IsWindfury, HasPoison, IsLifeSteal');
console.log('  bool: HasReborn, IsFrozen, IsImmune, IsSilenced, IsStealth');
console.log('  bool: HasCharge, HasRush, IsTired');
console.log('SimBoard: FriendMinions, EnemyMinions, Hand, FriendHero, EnemyHero');
console.log('  FriendWeapon, EnemyWeapon, Mana, FriendCardDraw');

// 统计：哪些效果是模拟器完全无法表达的
const unmodelable = [
  '变形', '复制', '交换', '偷取', '夺取控制', '洗入',
  '减少', '增加', '变为', '替换', '移除',
  '随机施放', '再次施放', '触发', '激活',
  '法力水晶', '法力值消耗',
  '不能攻击', '不能被法术', '无法使用',
  '进化', '退化',
  '奥秘', '任务', '支线',
  '激励', '回响', '磁力'
];

let unmodelableCount = 0;
const unmodelableCards = [];
missing.forEach(c => {
  const t = clean(c.text || '');
  if (!t) return;
  const isUnmodelable = unmodelable.some(v => t.includes(v));
  if (isUnmodelable) {
    unmodelableCount++;
  }
});

console.log(`\n模拟器完全无法表达的卡牌: ${unmodelableCount} / ${missing.length}`);
console.log(`理论上可以用现有+扩展模板覆盖的: ${missing.length - unmodelableCount}`);
