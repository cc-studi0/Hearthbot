#!/usr/bin/env node
const fs = require('fs');
const path = require('path');

const ROOT = path.resolve(__dirname, '..');
const CARD_EFFECTS_DIR = path.join(ROOT, 'CardEffects');

function walk(dir, out) {
  for (const ent of fs.readdirSync(dir, { withFileTypes: true })) {
    const p = path.join(dir, ent.name);
    if (ent.isDirectory()) walk(p, out);
    else if (ent.isFile() && ent.name.endsWith('.json')) out.push(p);
  }
}

function sanitizeClassName(id) {
  return `Sim_${id.replace(/[^A-Za-z0-9_]/g, '_')}`;
}

function esc(s) {
  return String(s).replace(/\\/g, '\\\\').replace(/"/g, '\\"');
}

function emitEffectDef(effect) {
  const type = esc(effect.type || 'noop');
  const v = Number.isFinite(effect.v) ? effect.v : 0;
  const atk = Number.isFinite(effect.atk) ? effect.atk : 0;
  const hp = Number.isFinite(effect.hp) ? effect.hp : 0;
  const n = Number.isFinite(effect.n) ? effect.n : 1;
  const dur = Number.isFinite(effect.dur) ? effect.dur : 0;
  const useSP = effect.useSP === true ? 'true' : 'false';
  return `new EffectDef(\"${type}\", v: ${v}, atk: ${atk}, hp: ${hp}, n: ${n}, dur: ${dur}, useSP: ${useSP})`;
}

function emitTriggerDef(triggerName, block) {
  const targetType = esc((block && block.targetType) ? block.targetType : 'None');
  const effects = Array.isArray(block && block.effects) ? block.effects : [];
  const effectLines = effects.map(e => `                ${emitEffectDef(e)}`).join(',\n');
  return [
    `            new TriggerDef(`,
    `                \"${esc(triggerName)}\",`,
    `                \"${targetType}\",`,
    effectLines,
    `            )`
  ].join('\n');
}

function emitClass(cardId, triggers, className) {
  const triggerDefs = triggers.map(t => emitTriggerDef(t.name, t.block)).join(',\n');
  return `using BotMain.AI;\n\nnamespace BotMain.AI.CardEffectsScripts\n{\n    internal sealed class ${className} : ICardEffectScript\n    {\n        public void Register(CardEffectDB db)\n        {\n            CardEffectScriptRuntime.RegisterById(\n                db,\n                \"${esc(cardId)}\",\n${triggerDefs}\n            );\n        }\n    }\n}\n`;
}

function main() {
  if (!fs.existsSync(CARD_EFFECTS_DIR)) {
    console.error(`CardEffects dir not found: ${CARD_EFFECTS_DIR}`);
    process.exit(1);
  }

  const files = [];
  walk(CARD_EFFECTS_DIR, files);

  let ok = 0;
  let skipped = 0;
  const usedClass = new Map();

  for (const jsonFile of files) {
    const cardId = path.basename(jsonFile, '.json');
    let root;
    try {
      root = JSON.parse(fs.readFileSync(jsonFile, 'utf8'));
    } catch (e) {
      console.error(`Parse error: ${jsonFile} -> ${e.message}`);
      skipped++;
      continue;
    }

    const triggers = Object.keys(root || {}).map(k => ({ name: k, block: root[k] }));
    const validTriggers = triggers.filter(t => t.block && Array.isArray(t.block.effects) && t.block.effects.length > 0);
    if (validTriggers.length === 0) {
      skipped++;
      continue;
    }

    let className = sanitizeClassName(cardId);
    if (usedClass.has(className)) {
      const c = usedClass.get(className) + 1;
      usedClass.set(className, c);
      className = `${className}_${c}`;
    } else {
      usedClass.set(className, 1);
    }

    const cs = emitClass(cardId, validTriggers, className);
    const csFile = jsonFile.replace(/\.json$/i, '.cs');
    fs.writeFileSync(csFile, cs, 'utf8');
    ok++;
  }

  console.log(`Generated ${ok} card effect .cs files, skipped ${skipped}.`);
}

main();
