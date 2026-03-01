/**
 * LLM 批量效果分类器
 * 读取 cards.json 中所有未被 effects_data.json 覆盖的卡牌，
 * 分批发送给 DeepSeek API 进行效果分类，结果合并回 effects_data.json。
 *
 * 环境变量：
 *   DEEPSEEK_API_KEY   — API 密钥
 *   DEEPSEEK_BASE_URL  — 可选，默认 https://api.deepseek.com/v1
 *
 * 用法：node generate_effects_llm.js
 */

const fs = require('fs');
const path = require('path');
const https = require('https');
const http = require('http');

// ── 配置 ──
const API_KEY = process.env.DEEPSEEK_API_KEY;
const BASE_URL = process.env.DEEPSEEK_BASE_URL || 'https://api.deepseek.com/v1';
const BATCH_SIZE = 50;
const MODEL = 'deepseek-chat';
const ROOT = path.resolve(__dirname, '..');
const EFFECTS_PATH = path.join(ROOT, 'effects_data.json');
const CARDS_PATH = path.join(__dirname, 'cards.json');
const PROGRESS_PATH = path.join(__dirname, '.llm_progress.json');

if (!API_KEY) {
  console.error('请设置环境变量 DEEPSEEK_API_KEY');
  process.exit(1);
}

// ── 所有可用的效果类别及参数说明 ──
const CATEGORY_DOC = `
可用的效果类别（category）及其 entry 格式：

触发器前缀：spell_ = 法术, bc_ = 战吼, dr_ = 亡语, eot_ = 回合结束, combo_ = 连击

效果后缀及参数：
- dmg: 对目标伤害 → [id, damage, hasSpellDamage?]  (hasSpellDamage 为 true/false)
- aoe_all: 对所有随从伤害 → [id, damage, hasSpellDamage?]
- aoe_enemy: 对所有敌方伤害 → [id, damage, hasSpellDamage?]
- aoe_enemies_heroes: 对所有敌方(含英雄)伤害 → [id, damage, hasSpellDamage?]
- face: 对敌方英雄伤害 → [id, damage, hasSpellDamage?]
- random_enemy / dmg_random: 随机敌方伤害 → [id, damage, hasSpellDamage?]
- dmg_enemy_hero: 对敌方英雄伤害(亡语用) → [id, damage]
- freeze: 冻结目标 → [id]
- freeze_all: 冻结所有 → [id]
- destroy: 消灭目标 → [id]
- destroy_all: 消灭所有随从 → [id]
- destroy_weapon: 消灭武器 → [id]
- silence: 沉默 → [id]
- heal: 治疗目标 → [id, amount]
- heal_hero: 治疗己方英雄 → [id, amount]
- restore: 恢复生命 → [id, amount]
- restore_all: 恢复所有友方 → [id, amount]
- draw: 抽牌 → [id, count]
- add_hand: 加牌到手 → [id, count]
- armor: 获得护甲 → [id, amount]
- summon: 召唤随从 → [id, attack, health]
- buff: 给目标+X/+Y → [id, atk, hp]
- buff_all: 给所有友方+X/+Y → [id, atk, hp]
- buff_friendly: 给友方随从+X/+Y → [id, atk, hp]
- buff_hand: 给手牌+X/+Y → [id, atk, hp]
- gain_buff: 自身获得+X/+Y → [id, atk, hp]
- self_buff: 回合结束自身+X/+Y → [id, atk, hp]
- atk / atk_buff / buff_atk: 给攻击力 → [id, atk]
- give_hp / buff_hp: 给生命值 → [id, hp]
- set_hp: 设置生命值 → [id, hp]
- set_hero_hp: 设置英雄生命值 → [id, hp]
- give_taunt: 给嘲讽 → [id]
- give_ds: 给圣盾 → [id]
- give_windfury: 给风怒 → [id]
- give_lifesteal: 给吸血 → [id]
- give_rush: 给突袭 → [id]
- give_immune: 给免疫 → [id]
- give_stealth: 给潜行 → [id]
- give_poison: 给剧毒 → [id]
- give_reborn: 给复生 → [id]
- equip: 装备武器 → [id, atk, durability]
- bounce: 回手 → [id]
- polymorph: 变形 → [id, atk, hp]  (变形后身材)
- copy_hand: 复制到手牌 → [id, count]
- mana_crystal: 获得法力水晶 → [id, count]
- reduce_cost: 减费 → [id]
- secret: 奥秘 → [id, cost]
- random_destroy: 随机消灭 → [id]
- transform_self: 自身变形 → [id, atk, hp]
- steal: 偷取控制 → [id]
- shuffle: 洗入牌库 → [id, count]
- discard: 弃牌 → [id, count]
- aoe_friendly: 对友方伤害 → [id, damage]
- dmg_self: 对自身英雄伤害 → [id, damage]
- hero_atk: 英雄获得攻击力 → [id, atk]
- noop: 无法模拟 → [id]

一张卡可以有多个效果（比如同时有战吼和亡语），请为每个效果分别输出。
如果卡牌是白板（无文本效果），跳过不输出。
如果效果完全无法归类到上述任何类别，使用 noop。
`;

// ── 工具函数 ──
function strip(t) {
  return t ? t.replace(/<[^>]+>/g, '').replace(/\[x\]/g, '').replace(/\n/g, ' ').replace(/\s+/g, ' ').trim() : '';
}

function loadJSON(p) {
  return JSON.parse(fs.readFileSync(p, 'utf8'));
}

function saveJSON(p, data) {
  fs.writeFileSync(p, JSON.stringify(data, null, 2));
}

// 收集 effects_data.json 中已覆盖的所有卡牌 ID
function getCoveredIds(effects) {
  const covered = new Set();
  for (const [, entries] of Object.entries(effects)) {
    if (!Array.isArray(entries)) continue;
    for (const entry of entries) {
      if (Array.isArray(entry)) covered.add(entry[0]);
      else if (typeof entry === 'string') covered.add(entry);
    }
  }
  return covered;
}

// ── API 调用 ──
function callAPI(messages) {
  return new Promise((resolve, reject) => {
    const url = new URL(BASE_URL + '/chat/completions');
    const isHttps = url.protocol === 'https:';
    const options = {
      hostname: url.hostname,
      port: url.port || (isHttps ? 443 : 80),
      path: url.pathname,
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'Authorization': `Bearer ${API_KEY}`,
      },
    };

    const body = JSON.stringify({
      model: MODEL,
      messages,
      temperature: 0.1,
      max_tokens: 8192,
    });

    const req = (isHttps ? https : http).request(options, (res) => {
      let data = '';
      res.on('data', chunk => data += chunk);
      res.on('end', () => {
        try {
          const json = JSON.parse(data);
          if (json.error) reject(new Error(json.error.message));
          else resolve(json.choices[0].message.content);
        } catch (e) {
          reject(new Error(`Parse error: ${data.substring(0, 500)}`));
        }
      });
    });

    req.on('error', reject);
    req.setTimeout(120000, () => { req.destroy(); reject(new Error('Timeout')); });
    req.write(body);
    req.end();
  });
}

function buildPrompt(batch) {
  const cardList = batch.map(c => {
    const text = strip(c.text || '');
    return `{ "id": "${c.id}", "name": "${c.name}", "type": "${c.type}", "cost": ${c.cost ?? 0}, "attack": ${c.attack ?? 0}, "health": ${c.health ?? 0}, "text": "${text}" }`;
  }).join('\n');

  return [
    {
      role: 'system',
      content: `你是炉石传说卡牌效果分类专家。根据卡牌文本，将每张卡的效果分类到预定义的效果类别中。
${CATEGORY_DOC}

输出格式：纯 JSON 数组，每个元素是 { "category": "完整类别名(含前缀)", "entry": [...] }
例如：
[
  { "category": "spell_dmg", "entry": ["CARD_ID", 3, true] },
  { "category": "bc_buff", "entry": ["CARD_ID", 2, 3] },
  { "category": "dr_summon", "entry": ["CARD_ID", 2, 2] }
]

注意：
1. category 必须包含触发器前缀（spell_/bc_/dr_/eot_/combo_）
2. 法术卡用 spell_ 前缀
3. 随从/武器的战吼用 bc_ 前缀，亡语用 dr_ 前缀
4. 如果卡牌有多个效果（如同时有战吼和亡语），分别输出多条
5. 只输出 JSON，不要其他文字`
    },
    {
      role: 'user',
      content: `请为以下卡牌分类效果：\n${cardList}`
    }
  ];
}

function parseResponse(text) {
  // 提取 JSON 数组
  const match = text.match(/\[[\s\S]*\]/);
  if (!match) return [];
  try {
    return JSON.parse(match[0]);
  } catch {
    console.warn('  JSON 解析失败，尝试修复...');
    // 尝试逐行解析
    const lines = match[0].split('\n');
    const results = [];
    for (const line of lines) {
      const m = line.match(/\{[^}]+\}/);
      if (m) {
        try { results.push(JSON.parse(m[0])); } catch {}
      }
    }
    return results;
  }
}

function mergeResults(effects, results) {
  let added = 0;
  for (const r of results) {
    if (!r.category || !r.entry) continue;
    if (!effects[r.category]) effects[r.category] = [];
    // 检查是否已存在
    const id = Array.isArray(r.entry) ? r.entry[0] : r.entry;
    const exists = effects[r.category].some(e =>
      (Array.isArray(e) ? e[0] : e) === id
    );
    if (!exists) {
      effects[r.category].push(r.entry);
      added++;
    }
  }
  return added;
}

// ── 主流程 ──
async function main() {
  console.log('加载数据...');
  const cards = loadJSON(CARDS_PATH);
  const effects = loadJSON(EFFECTS_PATH);

  // 过滤掉衍生卡
  const dominated = new Set();
  cards.forEach(c => { if (/^(CORE_|VAN_|Core_|LEG_)/.test(c.id)) dominated.add(c.id); });
  const validCards = cards.filter(c => !dominated.has(c.id) && c.collectible);

  const coveredIds = getCoveredIds(effects);
  const uncovered = validCards.filter(c => !coveredIds.has(c.id) && c.text);

  console.log(`总卡牌: ${validCards.length}, 已覆盖: ${coveredIds.size}, 未覆盖(有文本): ${uncovered.length}`);

  if (uncovered.length === 0) {
    console.log('所有卡牌已覆盖！');
    return;
  }

  // 加载断点续传进度
  let processed = new Set();
  if (fs.existsSync(PROGRESS_PATH)) {
    const progress = loadJSON(PROGRESS_PATH);
    processed = new Set(progress.processed || []);
    console.log(`从断点恢复，已处理 ${processed.size} 张`);
  }

  const remaining = uncovered.filter(c => !processed.has(c.id));
  console.log(`本次需处理: ${remaining.length} 张\n`);

  // 分批处理
  let totalAdded = 0;
  for (let i = 0; i < remaining.length; i += BATCH_SIZE) {
    const batch = remaining.slice(i, i + BATCH_SIZE);
    const batchNum = Math.floor(i / BATCH_SIZE) + 1;
    const totalBatches = Math.ceil(remaining.length / BATCH_SIZE);

    console.log(`[${batchNum}/${totalBatches}] 处理 ${batch.length} 张卡牌...`);

    try {
      const messages = buildPrompt(batch);
      const response = await callAPI(messages);
      const results = parseResponse(response);
      const added = mergeResults(effects, results);
      totalAdded += added;

      console.log(`  → LLM 返回 ${results.length} 条效果，新增 ${added} 条`);

      // 更新进度
      batch.forEach(c => processed.add(c.id));
      saveJSON(PROGRESS_PATH, { processed: [...processed] });

      // 每批次保存 effects_data.json
      saveJSON(EFFECTS_PATH, effects);

    } catch (err) {
      console.error(`  ✗ 批次 ${batchNum} 失败: ${err.message}`);
      console.log('  等待 10 秒后重试...');
      await new Promise(r => setTimeout(r, 10000));
      i -= BATCH_SIZE; // 重试当前批次
      continue;
    }

    // 限速：每批间隔 2 秒
    if (i + BATCH_SIZE < remaining.length) {
      await new Promise(r => setTimeout(r, 2000));
    }
  }

  // 最终保存
  saveJSON(EFFECTS_PATH, effects);
  const finalCovered = getCoveredIds(effects);
  console.log(`\n完成！新增 ${totalAdded} 条效果`);
  console.log(`最终覆盖: ${finalCovered.size} / ${validCards.length} 张卡牌`);

  // 清理进度文件
  if (fs.existsSync(PROGRESS_PATH)) fs.unlinkSync(PROGRESS_PATH);
}

main().catch(err => {
  console.error('致命错误:', err);
  process.exit(1);
});
