const fs = require('fs');

// 读取数据源
const csCode = fs.readFileSync('H:/桌面/炉石脚本/BotMain/AI/CardEffectDB.cs', 'utf8');
const cards = JSON.parse(fs.readFileSync('H:/桌面/炉石脚本/cards.json', 'utf8'));

// 构建卡牌ID→卡牌数据映射
const cardMap = {};
for (const c of cards) cardMap[c.id] = c;

// 清理HTML标签和格式
function strip(t) {
  return t ? t.replace(/<[^>]+>/g, '').replace(/\[x\]/g, '').replace(/\n/g, ' ').replace(/\s+/g, ' ').trim() : '';
}

// 从C#枚举名还原卡牌ID
function enumToId(enumName) {
  return enumName.replace(/^C\./, '');
}

// 统计
let pass = 0, fail = 0, warn = 0, skip = 0;
const failures = [];
const warnings = [];

// 解析所有 db.Register 调用
const regEx = /db\.Register\(C\.([A-Za-z0-9_]+),\s*EffectTrigger\.(\w+),\s*\(b,s,t\)=>\{(.+?)\}\);/g;
let match;
const registrations = [];

while ((match = regEx.exec(csCode)) !== null) {
  registrations.push({
    id: match[1],
    trigger: match[2],
    body: match[3],
    line: csCode.substring(0, match.index).split('\n').length
  });
}

console.log(`Found ${registrations.length} registrations in CardEffectDB.cs\n`);

// 从C#代码体中提取效果模式和数值
function parseEffect(body) {
  let m;
  // 单体伤害 Dmg(b,t,N+SP(b)) 或 Dmg(b,t,N)
  if ((m = body.match(/Dmg\(b,t,(\d+)\+SP\(b\)\)/))) return { type: 'dmg_sp', value: parseInt(m[1]) };
  if ((m = body.match(/Dmg\(b,t,(\d+)\)/)) && !body.includes('foreach')) return { type: 'dmg', value: parseInt(m[1]) };

  // AOE全随从伤害
  if (body.includes('FriendMinions') && body.includes('EnemyMinions') && body.includes('foreach') && (m = body.match(/Dmg\(b,m,(\d+)\)/))) {
    return { type: 'aoe_all', value: parseInt(m[1]) };
  }
  if (body.includes('FriendMinions') && body.includes('EnemyMinions') && body.includes('foreach') && (m = body.match(/int d=(\d+)\+SP/))) {
    return { type: 'aoe_all_sp', value: parseInt(m[1]) };
  }

  // AOE敌方+英雄
  if (body.includes('EnemyMinions') && body.includes('EnemyHero') && body.includes('foreach') && !body.includes('FriendMinions.ToArray()')) {
    if ((m = body.match(/int d=(\d+)\+SP/))) return { type: 'aoe_enemy_sp', value: parseInt(m[1]) };
    if ((m = body.match(/Dmg\(b,m,(\d+)\)/))) return { type: 'aoe_enemy', value: parseInt(m[1]) };
  }

  // 对英雄伤害
  if ((m = body.match(/Dmg\(b,b\.EnemyHero,(\d+)\+SP\(b\)\)/))) return { type: 'face_sp', value: parseInt(m[1]) };
  if ((m = body.match(/Dmg\(b,b\.EnemyHero,(\d+)\)/)) && !body.includes('foreach')) return { type: 'face', value: parseInt(m[1]) };

  // 抽牌
  if ((m = body.match(/FriendCardDraw\+=(\d+)/))) return { type: 'draw', value: parseInt(m[1]) };

  // 护甲
  if ((m = body.match(/Armor\+=(\d+)/))) return { type: 'armor', value: parseInt(m[1]) };

  // 治疗
  if ((m = body.match(/Health=Math\.Min\(t\.MaxHealth,t\.Health\+(\d+)\)/))) return { type: 'heal', value: parseInt(m[1]) };
  if ((m = body.match(/FriendHero\.Health=Math\.Min\(.*?Health\+(\d+)\)/))) return { type: 'heal_hero', value: parseInt(m[1]) };

  // 召唤 SimEntity{Atk=X,Health=Y}
  if ((m = body.match(/new SimEntity\{Atk=(\d+),Health=(\d+)/))) return { type: 'summon', atk: parseInt(m[1]), hp: parseInt(m[2]) };

  // buff +X/+Y
  if ((m = body.match(/t\.Atk\+=(\d+);t\.Health\+=(\d+)/))) return { type: 'buff', atk: parseInt(m[1]), hp: parseInt(m[2]) };
  if ((m = body.match(/s\.Atk\+=(\d+);s\.Health\+=(\d+)/))) return { type: 'self_buff', atk: parseInt(m[1]), hp: parseInt(m[2]) };

  // 冻结
  if (body.includes('IsFrozen=true')) return { type: 'freeze' };
  // 消灭
  if (body.includes('t.Health=0') && !body.includes('foreach')) return { type: 'destroy' };
  if (body.includes('.Health=0') && body.includes('foreach')) return { type: 'destroy_all' };
  // 沉默
  if (body.includes('Silence(t)')) return { type: 'silence' };

  // +X攻击力
  if ((m = body.match(/t\.Atk\+=(\d+)/)) && !body.includes('Health+=')) return { type: 'atk_buff', value: parseInt(m[1]) };

  // 空效果
  if (body.trim() === '') return { type: 'empty' };

  return { type: 'unknown' };
}

// 从卡牌文本提取官方数值
function extractOfficialValues(text, rawText) {
  if (!text) return null;
  const hasSP = rawText && rawText.includes('$');
  let m;

  // 伤害数值 ($N 或纯数字)
  if ((m = text.match(/Deal \$?(\d+) damage/i))) {
    return { type: 'dmg', value: parseInt(m[1]), hasSP };
  }
  // 治疗
  if ((m = text.match(/Restore #?(\d+) Health/i))) {
    return { type: 'heal', value: parseInt(m[1]) };
  }
  // 抽牌
  if ((m = text.match(/Draw (\d+) cards?/i))) {
    return { type: 'draw', value: parseInt(m[1]) };
  }
  if (/Draw a card/i.test(text)) {
    return { type: 'draw', value: 1 };
  }
  // 护甲
  if ((m = text.match(/Gain (\d+) Armor/i))) {
    return { type: 'armor', value: parseInt(m[1]) };
  }
  // 召唤 X/Y
  if ((m = text.match(/Summon .*?(\d+)\/(\d+)/i))) {
    return { type: 'summon', atk: parseInt(m[1]), hp: parseInt(m[2]) };
  }
  // buff +X/+Y
  if ((m = text.match(/\+(\d+)\/\+(\d+)/))) {
    return { type: 'buff', atk: parseInt(m[1]), hp: parseInt(m[2]) };
  }
  // +X Attack
  if ((m = text.match(/\+(\d+) Attack/i))) {
    return { type: 'atk', value: parseInt(m[1]) };
  }
  return null;
}

// 主验证循环
for (const reg of registrations) {
  const card = cardMap[reg.id];
  if (!card) {
    // CORE_/VAN_/LEG_ 前缀卡牌可能找不到原始数据
    if (/^(CORE_|VAN_|LEG_)/.test(reg.id)) { skip++; continue; }
    warnings.push(`WARN L${reg.line}: ${reg.id} not found in cards.json`);
    warn++;
    continue;
  }

  const effect = parseEffect(reg.body);
  if (effect.type === 'unknown' || effect.type === 'empty') { skip++; continue; }

  const text = strip(card.text);
  const rawText = card.text || '';

  // 跳过含 Corrupt/Infuse/Forge 的复杂卡牌（基础效果和升级效果不同）
  if (/Corrupt:|Infuse|Forge:/i.test(text)) { skip++; continue; }
  // 跳过含多段不同数值的复杂效果
  if (/\+\d+\/\+\d+.*\+\d+\/\+\d+/i.test(text)) { skip++; continue; }

  // 提取触发器对应的文本段
  let segment = text;
  if (reg.trigger === 'Battlecry') {
    const bcMatch = text.match(/Battlecry:\s*(.*)/i);
    if (bcMatch) segment = bcMatch[1];
  } else if (reg.trigger === 'Deathrattle') {
    const drMatch = text.match(/Deathrattle:\s*(.*)/i);
    if (drMatch) segment = drMatch[1];
  } else if (reg.trigger === 'EndOfTurn') {
    const eotMatch = text.match(/At the end of (?:your|each) turn,?\s*(.*)/i);
    if (eotMatch) segment = eotMatch[1];
  }

  // 校验数值
  let verified = false;
  let failMsg = null;

  switch (effect.type) {
    case 'dmg_sp':
    case 'dmg': {
      const m = segment.match(/Deal \$?(\d+) damage/i);
      if (m) {
        const official = parseInt(m[1]);
        if (official === effect.value) { verified = true; }
        else { failMsg = `damage: code=${effect.value} official=${official}`; }
      } else { skip++; continue; }
      break;
    }
    case 'aoe_all_sp':
    case 'aoe_all': {
      const m = segment.match(/Deal \$?(\d+) damage/i);
      if (m) {
        const official = parseInt(m[1]);
        if (official === effect.value) { verified = true; }
        else { failMsg = `aoe damage: code=${effect.value} official=${official}`; }
      } else { skip++; continue; }
      break;
    }
    case 'aoe_enemy_sp':
    case 'aoe_enemy': {
      const m = segment.match(/Deal \$?(\d+) damage/i);
      if (m) {
        const official = parseInt(m[1]);
        if (official === effect.value) { verified = true; }
        else { failMsg = `aoe enemy damage: code=${effect.value} official=${official}`; }
      } else { skip++; continue; }
      break;
    }
    case 'face_sp':
    case 'face': {
      const m = segment.match(/Deal \$?(\d+) damage/i);
      if (m) {
        const official = parseInt(m[1]);
        if (official === effect.value) { verified = true; }
        else { failMsg = `face damage: code=${effect.value} official=${official}`; }
      } else { skip++; continue; }
      break;
    }
    case 'draw': {
      const m = segment.match(/Draw (\d+)/i);
      const official = m ? parseInt(m[1]) : (/Draw a card/i.test(segment) ? 1 : null);
      if (official !== null) {
        if (official === effect.value) { verified = true; }
        else { failMsg = `draw: code=${effect.value} official=${official}`; }
      } else if (/Add .* to your hand/i.test(segment)) {
        skip++; continue; // 加牌当抽牌处理，跳过
      } else { skip++; continue; }
      break;
    }
    case 'armor': {
      const m = segment.match(/Gain (\d+) Armor/i);
      if (m) {
        const official = parseInt(m[1]);
        if (official === effect.value) { verified = true; }
        else { failMsg = `armor: code=${effect.value} official=${official}`; }
      } else { skip++; continue; }
      break;
    }
    case 'heal':
    case 'heal_hero': {
      const m = segment.match(/Restore #?(\d+) Health/i);
      if (m) {
        const official = parseInt(m[1]);
        if (official === effect.value) { verified = true; }
        else { failMsg = `heal: code=${effect.value} official=${official}`; }
      } else { skip++; continue; }
      break;
    }
    case 'summon': {
      const m = segment.match(/(\d+)\/(\d+)/);
      if (m) {
        const oAtk = parseInt(m[1]), oHp = parseInt(m[2]);
        if (oAtk === effect.atk && oHp === effect.hp) { verified = true; }
        else { failMsg = `summon: code=${effect.atk}/${effect.hp} official=${oAtk}/${oHp}`; }
      } else { skip++; continue; }
      break;
    }
    case 'buff':
    case 'self_buff': {
      const m = segment.match(/\+(\d+)\/\+(\d+)/);
      if (m) {
        const oAtk = parseInt(m[1]), oHp = parseInt(m[2]);
        if (oAtk === effect.atk && oHp === effect.hp) { verified = true; }
        else { failMsg = `buff: code=+${effect.atk}/+${effect.hp} official=+${oAtk}/+${oHp}`; }
      } else { skip++; continue; }
      break;
    }
    case 'atk_buff': {
      const m = segment.match(/\+(\d+) Attack/i);
      if (m) {
        const official = parseInt(m[1]);
        if (official === effect.value) { verified = true; }
        else { failMsg = `atk buff: code=${effect.value} official=${official}`; }
      } else { skip++; continue; }
      break;
    }
    default:
      skip++; continue;
  }

  if (verified) { pass++; }
  else if (failMsg) {
    fail++;
    failures.push(`FAIL L${reg.line}: ${reg.id} [${reg.trigger}] ${failMsg} | text: "${segment.slice(0,80)}"`);
  }
}

// 输出报告
console.log('=== Verification Report ===');
console.log(`PASS: ${pass}`);
console.log(`FAIL: ${fail}`);
console.log(`WARN: ${warn}`);
console.log(`SKIP: ${skip} (unknown/empty/unparseable)`);
console.log(`TOTAL: ${registrations.length}\n`);

if (failures.length > 0) {
  console.log('--- FAILURES ---');
  for (const f of failures) console.log(f);
  console.log('');
}
if (warnings.length > 0) {
  console.log('--- WARNINGS ---');
  for (const w of warnings) console.log(w);
}
