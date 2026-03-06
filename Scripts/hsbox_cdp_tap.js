#!/usr/bin/env node
"use strict";

/**
 * Attach to HS box CEF DevTools and tap recommendation callbacks:
 *   - window.onUpdateLadderActionRecommend
 *   - window.onUpdateArenaRecommend
 *
 * Output is JSON Lines to stdout. Optional --out writes the same JSONL to file.
 *
 * Usage:
 *   node tools/hsbox_cdp_tap.js --port 9222
 *   node tools/hsbox_cdp_tap.js --host 127.0.0.1 --port 9222 --out hs_rec.jsonl
 */

const fs = require("node:fs");
const http = require("node:http");
const { setTimeout: sleep } = require("node:timers/promises");
const WebSocketCtor = resolveWebSocketCtor();

const PREFIX = "__HS_REC_JSON__:";
const DIAG_PREFIX = "__HS_REC_DIAG__:";
const SCRIPT_VERSION = "ladder-primary-v3-20260305";
const DEFAULT_HOST = "127.0.0.1";
const DEFAULT_PORT = 9222;
const DEFAULT_HINT = "ladder-opp";

function resolveWebSocketCtor() {
  if (typeof globalThis.WebSocket === "function") {
    return globalThis.WebSocket;
  }

  try {
    const undici = require("undici");
    if (undici && typeof undici.WebSocket === "function") {
      return undici.WebSocket;
    }
  } catch {
    // ignore
  }

  try {
    const ws = require("ws");
    if (typeof ws === "function") return ws;
    if (ws && typeof ws.WebSocket === "function") return ws.WebSocket;
  } catch {
    // ignore
  }

  return null;
}

function parseArgs(argv) {
  const out = {
    host: DEFAULT_HOST,
    port: DEFAULT_PORT,
    hint: DEFAULT_HINT,
    outFile: "",
  };

  for (let i = 2; i < argv.length; i += 1) {
    const a = argv[i];
    const b = argv[i + 1];
    if (a === "--host" && b) {
      out.host = b;
      i += 1;
      continue;
    }
    if (a === "--port" && b) {
      out.port = Number(b);
      i += 1;
      continue;
    }
    if (a === "--hint" && b) {
      out.hint = b;
      i += 1;
      continue;
    }
    if (a === "--out" && b) {
      out.outFile = b;
      i += 1;
      continue;
    }
    if (a === "--help" || a === "-h") {
      printHelp();
      process.exit(0);
    }
  }

  if (!Number.isInteger(out.port) || out.port <= 0) {
    throw new Error(`invalid --port: ${out.port}`);
  }
  return out;
}

function printHelp() {
  console.log(
    [
      "HS Box recommend tapper (CDP)",
      "",
      "Options:",
      "  --host <host>   DevTools host (default: 127.0.0.1)",
      "  --port <port>   DevTools port (default: 9222)",
      "  --hint <text>   Target URL match hint (default: ladder-opp)",
      "  --out <file>    Optional JSONL output file",
      "",
      "Example:",
      "  node tools/hsbox_cdp_tap.js --port 9222 --out hs_rec.jsonl",
    ].join("\n")
  );
}

async function getJson(url) {
  if (typeof fetch === "function") {
    const res = await fetch(url);
    if (!res.ok) {
      throw new Error(`HTTP ${res.status} for ${url}`);
    }
    return res.json();
  }

  return new Promise((resolve, reject) => {
    const req = http.get(url, (res) => {
      const chunks = [];
      res.on("data", (c) => chunks.push(c));
      res.on("end", () => {
        const body = Buffer.concat(chunks).toString("utf8");
        const status = Number(res.statusCode || 0);
        if (status < 200 || status >= 300) {
          reject(new Error(`HTTP ${status} for ${url}`));
          return;
        }
        try {
          resolve(JSON.parse(body));
        } catch (err) {
          reject(new Error(`JSON parse failed: ${String(err)}`));
        }
      });
    });

    req.on("error", reject);
    req.setTimeout(4000, () => {
      req.destroy(new Error(`request timeout for ${url}`));
    });
  });
}

function rankTargets(list, hint) {
  if (!Array.isArray(list)) return [];
  const normalizedHint = String(hint || "").trim().toLowerCase();

  const candidates = list.filter((x) => {
    const url = String(x?.url || "").toLowerCase();
    return (
      !!x?.webSocketDebuggerUrl &&
      !url.startsWith("devtools://") &&
      !url.includes("chrome-devtools")
    );
  });

  if (candidates.length === 0) return null;

  const score = (target) => {
    const url = String(target?.url || "").toLowerCase();
    const title = String(target?.title || "").toLowerCase();
    let s = 0;

    if (normalizedHint) {
      if (url.includes(normalizedHint)) s += 300;
      if (title.includes(normalizedHint)) s += 220;
    }

    if (url.includes("/ladder-opp")) s += 500;
    if (url.includes("/ladder")) s += 300;
    if (url.includes("/analysis")) s += 160;
    if (url.includes("client-battle")) s += 280;
    if (url.includes("client-jipaiqi")) s += 200;
    if (url.includes("jipaiqi")) s += 180;
    if (url.includes("lushi.163.com")) s += 120;
    if (url.includes("hearthstone") || url.includes("lushi")) s += 80;
    if (title.includes("jipaiqi")) s += 140;
    if (title.includes("炉石") || title.includes("hearthstone")) s += 80;
    if (title.includes("盒子")) s += 60;
    if (target?.type === "page") s += 25;
    if (url.startsWith("http://") || url.startsWith("https://")) s += 15;
    if (url.startsWith("file://")) s += 10;

    return s;
  };

  candidates.sort((a, b) => score(b) - score(a));
  for (let i = 0; i < candidates.length; i += 1) {
    candidates[i].__score = score(candidates[i]);
  }
  return candidates;
}

function pickTargets(list, hint, limit = 4) {
  const ranked = rankTargets(list, hint);
  if (!Array.isArray(ranked) || ranked.length === 0)
    return [];

  const normalizeUrlKey = (raw) => {
    const text = String(raw || "").trim();
    if (!text)
      return "";
    try {
      const u = new URL(text);
      return `${u.origin}${u.pathname}`.toLowerCase();
    } catch {
      return text.split("#")[0].split("?")[0].toLowerCase();
    }
  };

  const deduped = [];
  const seen = new Set();
  const seenUrls = new Set();
  for (const target of ranked) {
    const urlKey = normalizeUrlKey(target?.url);
    if (urlKey && seenUrls.has(urlKey))
      continue;

    const key = String(
      target?.webSocketDebuggerUrl
      || target?.id
      || target?.url
      || ""
    );
    if (!key || seen.has(key))
      continue;
    seen.add(key);
    if (urlKey)
      seenUrls.add(urlKey);
    deduped.push(target);
  }
  return deduped.slice(0, Math.max(1, limit | 0));
}

function createCdp(wsUrl) {
  if (!WebSocketCtor) {
    throw new Error("WebSocket unavailable in current Node runtime");
  }

  const ws = new WebSocketCtor(wsUrl);
  let id = 0;
  const pending = new Map();
  let opened = false;
  let closed = false;

  const onOpen = new Promise((resolve, reject) => {
    ws.addEventListener("open", () => {
      opened = true;
      resolve();
    });
    ws.addEventListener("error", (err) => {
      if (!opened) reject(err);
    });
  });

  ws.addEventListener("message", (evt) => {
    let msg;
    try {
      msg = JSON.parse(String(evt.data));
    } catch {
      return;
    }
    if (msg && typeof msg.id === "number") {
      const p = pending.get(msg.id);
      if (!p) return;
      pending.delete(msg.id);
      if (msg.error) {
        p.reject(new Error(msg.error.message || JSON.stringify(msg.error)));
      } else {
        p.resolve(msg.result);
      }
    } else if (msg && msg.method) {
      if (typeof cdp.onEvent === "function") {
        cdp.onEvent(msg.method, msg.params || {});
      }
    }
  });

  ws.addEventListener("close", () => {
    closed = true;
    for (const [, p] of pending) {
      p.reject(new Error("CDP socket closed"));
    }
    pending.clear();
    if (typeof cdp.onClose === "function") cdp.onClose();
  });

  function call(method, params = {}) {
    if (closed) {
      return Promise.reject(new Error("CDP socket closed"));
    }
    const reqId = ++id;
    const payload = JSON.stringify({ id: reqId, method, params });
    return new Promise((resolve, reject) => {
      pending.set(reqId, { resolve, reject });
      ws.send(payload);
    });
  }

  const cdp = {
    onOpen,
    onEvent: null,
    onClose: null,
    call,
    close: () => {
      try {
        ws.close();
      } catch {
        // ignore
      }
    },
  };
  return cdp;
}

function buildInjectCode() {
  return `
(() => {
  const PREFIX = "${PREFIX}";
  const DIAG_PREFIX = "${DIAG_PREFIX}";
  const ENABLE_FALLBACK_CAPTURE = true;

  const isActionArrayLike = (arr) => {
    if (!Array.isArray(arr)) return false;
    let checked = 0;
    for (const item of arr) {
      if (item && typeof item === "object") {
        if ("actionName" in item || "action" in item || "name" in item)
          return true;
      }
      if (++checked >= 8) break;
    }
    return false;
  };

  const hasActionSignal = (payload) => {
    if (!payload || typeof payload !== "object")
      return false;
    if (isActionArrayLike(payload))
      return true;

    const preferred = ["data", "A", "a", "planA", "plan_a", "recommendA", "recommend_a", "actions", "steps", "args"];
    for (const key of preferred) {
      if (!(key in payload)) continue;
      const v = payload[key];
      if (isActionArrayLike(v))
        return true;
      if (v && typeof v === "object") {
        for (const nested of Object.values(v)) {
          if (isActionArrayLike(nested))
            return true;
        }
      }
    }
    return false;
  };

  const topKeys = (payload, limit = 10) => {
    if (!payload || typeof payload !== "object")
      return [];
    try {
      const keys = Object.keys(payload).filter((k) => k !== "args");
      if (keys.length <= limit)
        return keys;
      return keys.slice(0, limit);
    } catch (_) {
      return [];
    }
  };

  const shortHash = (text) => {
    const raw = String(text || "");
    let hash = 2166136261;
    for (let i = 0; i < raw.length; i++) {
      hash ^= raw.charCodeAt(i);
      hash = Math.imul(hash, 16777619);
    }
    const u = hash >>> 0;
    return u.toString(16).padStart(8, "0");
  };

  const buildDiagSummary = (source, payload, error) => {
    let serialized = "";
    try {
      serialized = JSON.stringify(payload ?? null);
    } catch (_) {
      serialized = String(payload ?? "");
    }
    if (serialized.length > 4096)
      serialized = serialized.slice(0, 4096);

    const lower = serialized.toLowerCase();
    const hasMulliganSignal = lower.includes("mulligan")
      || lower.includes("replace")
      || lower.includes("reroll")
      || lower.includes("swap")
      || lower.includes("waster")
      || lower.includes("highlight");

    return {
      source,
      ts: Date.now(),
      hasActionPayload: hasActionSignal(payload),
      hasMulliganSignal,
      payloadKeysTopN: topKeys(payload, 10),
      fingerprintShort: shortHash(source + "|" + serialized),
      hasError: !!error
    };
  };

  const emit = (source, payload, error) => {
    try {
      const now = Date.now();
      window.__hsLastEmitTs = now;
      const data = { source, ts: now };
      if (payload !== undefined) data.payload = payload;
      if (error) data.error = String(error);
      // 通道1: 打到 console，便于在 DevTools 直接观察推荐内容
      console.log(PREFIX + JSON.stringify(data));

      // 诊断信息保留 console 输出（节流，避免刷屏）
      const diag = buildDiagSummary(source, payload, error);
      const isArgsOnly = payload
        && typeof payload === "object"
        && !Array.isArray(payload)
        && Object.keys(payload).length === 1
        && Array.isArray(payload.args);
      if (!isArgsOnly) {
        const diagKey = diag.source + "|" + diag.fingerprintShort;
        const diagMap = window.__hsDiagEmitMap || (window.__hsDiagEmitMap = Object.create(null));
        const lastDiagTs = Number(diagMap[diagKey] || 0);
        if (!lastDiagTs || now - lastDiagTs >= 500) {
          diagMap[diagKey] = now;
          console.log(DIAG_PREFIX + JSON.stringify(diag));
        }
      }

      // 通道2: 数据存入队列，由外部 Runtime.evaluate 读取（更稳）
      if (!window.__hsRecQueue) window.__hsRecQueue = [];
      window.__hsRecQueue.push(data);
      if (window.__hsRecQueue.length > 80)
        window.__hsRecQueue = window.__hsRecQueue.slice(-50);
    } catch (err) {
      try {
        console.log(PREFIX + JSON.stringify({
          source: "emit_error",
          ts: Date.now(),
          error: String(err)
        }));
      } catch (_) {}
    }
  };

  const normalize = (raw) => {
    if (typeof raw !== "string") return raw;
    return raw
      .replaceAll("opp-target-hero", "oppTargetHero")
      .replaceAll("opp-target", "oppTarget")
      .replaceAll("target-hero", "targetHero");
  };

  const parsePayload = (raw) => {
    if (typeof raw !== "string") return raw;
    const fixed = normalize(raw);
    try {
      return JSON.parse(fixed);
    } catch (err) {
      try {
        if (fixed.startsWith("{") || fixed.startsWith("[")) {
          const loose = (new Function("return (" + fixed + ");"))();
          if (loose && typeof loose === "object") return loose;
        }
      } catch (_) {}
      return {
        __raw: raw,
        __fixed: fixed,
        __parseError: String(err)
      };
    }
  };

  const shouldCaptureUrl = (url) => {
    const u = String(url || "").toLowerCase();
    if (!u) return false;
    return u.includes("recommend")
      || u.includes("jipaiqi")
      || u.includes("mulligan")
      || u.includes("lushi.163.com")
      || u.includes("action")
      || u.includes("decision")
      || u.includes("plan");
  };

  const ACTION_KEYS = ["actionName", "action", "name"];
  const PLAN_A_KEYS = [
    "A",
    "a",
    "planA",
    "plan_a",
    "recommendA",
    "recommend_a",
    "optionA",
    "option_a"
  ];
  const PLAN_WRAPPER_KEYS = [
    "data",
    "payload",
    "result",
    "recommend",
    "recommendation",
    "plan",
    "plans",
    "list",
    "items",
    "args"
  ];
  const PLAN_LABEL_KEYS = [
    "plan",
    "planName",
    "name",
    "id",
    "key",
    "label",
    "type",
    "option",
    "recommend"
  ];
  const NON_PLAN_FALLBACK_KEYS = [
    "data",
    "payload",
    "result",
    "recommend",
    "recommendation",
    "action",
    "actions",
    "plan",
    "steps",
    "args"
  ];

  const isObj = (v) => v && typeof v === "object" && !Array.isArray(v);

  const looksLikeActionObject = (v) => {
    if (!isObj(v)) return false;
    return ACTION_KEYS.some((k) => Object.prototype.hasOwnProperty.call(v, k));
  };

  const looksLikeActionArray = (arr) => {
    if (!Array.isArray(arr) || arr.length === 0) return false;
    let checked = 0;
    for (const item of arr) {
      if (looksLikeActionObject(item)) return true;
      if (++checked >= 8) break;
    }
    return false;
  };

  const normalizePlanToken = (raw) =>
    String(raw || "")
      .toLowerCase()
      .replace(/[^a-z0-9]/g, "");

  const isPlanAIdentifier = (raw) => {
    const token = normalizePlanToken(raw);
    return token === "a"
      || token === "plana"
      || token === "recommenda"
      || token === "optiona";
  };

  const isPlanNonAIdentifier = (raw) => {
    const token = normalizePlanToken(raw);
    return token === "b"
      || token === "c"
      || token === "planb"
      || token === "planc"
      || token === "recommendb"
      || token === "recommendc"
      || token === "optionb"
      || token === "optionc";
  };

  const isPlanVariantKey = (raw) => {
    const token = normalizePlanToken(raw);
    return token === "b"
      || token === "c"
      || token === "planb"
      || token === "planc"
      || token === "recommendb"
      || token === "recommendc"
      || token === "optionb"
      || token === "optionc";
  };

  const isPlanLabelKey = (raw) => {
    const token = normalizePlanToken(raw);
    return token === "plan"
      || token === "planname"
      || token === "name"
      || token === "id"
      || token === "key"
      || token === "label"
      || token === "type"
      || token === "option"
      || token === "recommend";
  };

  const extractPlanAOnly = (value, depth = 0) => {
    if (depth > 6 || value === null || value === undefined) return null;

    if (Array.isArray(value)) {
      if (looksLikeActionArray(value)) return value;

      let checked = 0;
      for (const item of value) {
        const nested = extractPlanAOnly(item, depth + 1);
        if (nested) return nested;
        if (++checked >= 32) break;
      }
      return null;
    }

    if (!isObj(value)) return null;

    for (const key of PLAN_A_KEYS) {
      if (!Object.prototype.hasOwnProperty.call(value, key)) continue;
      const candidate = value[key];
      if (looksLikeActionArray(candidate)) return candidate;
      const nested = extractPlanAOnly(candidate, depth + 1);
      if (nested) return nested;
    }

    let isPlanAObject = false;
    for (const key of PLAN_LABEL_KEYS) {
      if (!Object.prototype.hasOwnProperty.call(value, key)) continue;
      const label = value[key];
      if (typeof label !== "string" && typeof label !== "number") continue;
      if (isPlanAIdentifier(label)) {
        isPlanAObject = true;
        break;
      }
    }

    if (isPlanAObject) {
      const actionKeys = [
        "actions",
        "steps",
        "data",
        "plan",
        "recommend",
        "recommendation",
        "list",
        "items",
        "args",
        "payload"
      ];

      for (const key of actionKeys) {
        if (!Object.prototype.hasOwnProperty.call(value, key)) continue;
        const candidate = value[key];
        if (looksLikeActionArray(candidate)) return candidate;
        const nested = extractPlanAOnly(candidate, depth + 1);
        if (nested) return nested;
      }
    }

    for (const key of PLAN_WRAPPER_KEYS) {
      if (!Object.prototype.hasOwnProperty.call(value, key)) continue;
      const nested = extractPlanAOnly(value[key], depth + 1);
      if (nested) return nested;
    }

    return null;
  };

  const containsNonPlanAMarkers = (value, depth = 0) => {
    if (depth > 6 || value === null || value === undefined) return false;

    if (Array.isArray(value)) {
      let checked = 0;
      for (const item of value) {
        if (containsNonPlanAMarkers(item, depth + 1)) return true;
        if (++checked >= 32) break;
      }
      return false;
    }

    if (!isObj(value)) return false;

    let checked = 0;
    for (const [key, child] of Object.entries(value)) {
      if (isPlanVariantKey(key)) return true;

      if (isPlanLabelKey(key) && (typeof child === "string" || typeof child === "number")) {
        if (isPlanNonAIdentifier(child)) return true;
      }

      if (containsNonPlanAMarkers(child, depth + 1)) return true;
      if (++checked >= 64) break;
    }

    return false;
  };

  const extractActionCandidate = (value, depth = 0) => {
    if (depth > 6 || value === null || value === undefined) return null;

    if (Array.isArray(value)) {
      if (looksLikeActionArray(value)) return value;

      const fromPlanA = extractPlanAOnly(value, depth + 1);
      if (fromPlanA) return fromPlanA;

      if (containsNonPlanAMarkers(value, depth + 1))
        return null;

      let checked = 0;
      for (const item of value) {
        const nested = extractActionCandidate(item, depth + 1);
        if (nested) return nested;
        if (++checked >= 32) break;
      }
      return null;
    }

    if (!isObj(value)) return null;

    const fromPlanA = extractPlanAOnly(value, depth + 1);
    if (fromPlanA) return fromPlanA;

    if (containsNonPlanAMarkers(value, depth + 1))
      return null;

    for (const key of NON_PLAN_FALLBACK_KEYS) {
      if (!Object.prototype.hasOwnProperty.call(value, key)) continue;
      const candidate = value[key];
      if (looksLikeActionArray(candidate)) return candidate;
      const nested = extractActionCandidate(candidate, depth + 1);
      if (nested) return nested;
    }

    let checked = 0;
    for (const [, child] of Object.entries(value)) {
      const nested = extractActionCandidate(child, depth + 1);
      if (nested) return nested;
      if (++checked >= 48) break;
    }

    return null;
  };

  const extractRecommendLikeObject = (value, depth = 0) => {
    if (depth > 6 || value === null || value === undefined) return null;

    if (Array.isArray(value)) {
      if (looksLikeActionArray(value)) return value;

      let checked = 0;
      for (const item of value) {
        const nested = extractRecommendLikeObject(item, depth + 1);
        if (nested) return nested;
        if (++checked >= 32) break;
      }
      return null;
    }

    if (!isObj(value)) return null;

    const keys = Object.keys(value);
    const isArgsOnly = keys.length === 1
      && keys[0] === "args"
      && Array.isArray(value.args);

    if (isArgsOnly) {
      let checked = 0;
      for (const item of value.args) {
        const nested = extractRecommendLikeObject(item, depth + 1);
        if (nested) return nested;
        if (++checked >= 32) break;
      }
      return null;
    }

    if (!isArgsOnly) {
      const joined = keys.join("|").toLowerCase();
      if (/(recommend|plan|action|mulligan|replace|reroll|swap|highlight|decision)/.test(joined))
        return value;
    }

    let checked = 0;
    for (const [, child] of Object.entries(value)) {
      const nested = extractRecommendLikeObject(child, depth + 1);
      if (nested) return nested;
      if (++checked >= 48) break;
    }

    return null;
  };

  const collectRecommendRoots = () => {
    const roots = [];
    const directKeys = [
      "__INITIAL_STATE__",
      "__NUXT__",
      "__NEXT_DATA__",
      "store",
      "vm",
      "app",
      "__VUE__",
      "__PINIA__"
    ];

    for (const key of directKeys) {
      if (Object.prototype.hasOwnProperty.call(window, key))
        roots.push(window[key]);
    }

    const dynamicKeys = Object.getOwnPropertyNames(window);
    let scanned = 0;
    for (const key of dynamicKeys) {
      if (++scanned > 220) break;
      if (!/(recommend|ladder|analysis|action|plan|jipaiqi|mulligan|replace|decision)/i.test(key)) continue;
      const value = window[key];
      if (!value || typeof value !== "object") continue;
      roots.push(value);
    }

    return roots;
  };

  const findPolledRecommendCandidate = () => {
    const roots = collectRecommendRoots();
    for (const root of roots) {
      const actionCandidate = extractActionCandidate(root);
      if (actionCandidate) return actionCandidate;

      const recommendObj = extractRecommendLikeObject(root, 0);
      if (recommendObj) return recommendObj;
    }

    return null;
  };

  const hook = (name) => {
    const storeKey = "__hs_hook_original_" + name;
    const trapKey = "__hs_hook_trapped_" + name;
    const current = window[name];
    if (current && current.__hsHooked && window[trapKey]) return;
    if (!window[storeKey] && typeof current === "function") {
      window[storeKey] = current;
    }

    const makeWrapped = (original) => {
      const wrapped = function(...args) {
        try {
          const parsedArgs = args.map((a) => parsePayload(a));
          let payload = null;

          for (const arg of parsedArgs) {
            const candidate = extractActionCandidate(arg);
            if (candidate) {
              payload = candidate;
              break;
            }

            const recommendObj = extractRecommendLikeObject(arg, 0);
            if (recommendObj) {
              payload = recommendObj;
              break;
            }
          }

          if (!payload)
            payload = findPolledRecommendCandidate();

          if (payload)
            emit(name, payload, null);
        } catch (err) {
          emit(name, null, err);
        }
        if (typeof original === "function") {
          return original.apply(this, args);
        }
        return undefined;
      };
      wrapped.__hsHooked = true;
      return wrapped;
    };

    // 用 defineProperty 拦截后续赋值，实现即时 hook
    if (!window[trapKey]) {
      let _value = makeWrapped(window[storeKey]);
      try {
        Object.defineProperty(window, name, {
          get: () => _value,
          set: (newFn) => {
            if (newFn && newFn.__hsHooked) {
              _value = newFn;
              return;
            }
            if (typeof newFn === "function") {
              window[storeKey] = newFn;
            }
            _value = makeWrapped(typeof newFn === "function" ? newFn : window[storeKey]);
          },
          configurable: true,
          enumerable: true,
        });
        window[trapKey] = true;
      } catch (_) {
        // defineProperty 失败时回退到普通赋值
        window[name] = makeWrapped(window[storeKey]);
      }
    } else {
      // trap 已存在，直接触发 setter 更新
      window[name] = makeWrapped(window[storeKey]);
    }
  };

  const HOOK_NAMES = window.__hsHookNames || new Set([
    // 主动作推荐入口
    "onUpdateLadderActionRecommend",
    "onUpdateArenaRecommend",
    "fRecommend",
    // 留牌相关
    "onUpdateMulliganRecommend",
    "onUpdateLadderMulliganRecommend",
    "onUpdateActionMulliganRecommend",
    "onUpdateMulliganActionRecommend",
    "onMulliganRecommendChanged",
    "fReplaceCard",
    "fWasterCard",
    "highLightAction"
  ]);
  window.__hsHookNames = HOOK_NAMES;

  const collectDynamicHookNames = () => {
    try {
      const keys = Object.getOwnPropertyNames(window);
      const aliases = [
        /^onUpdateLadderActionRecommend$/i,
        /^onUpdateLadderMulliganRecommend$/i,
        /^onUpdateMulliganRecommend$/i,
        /^onUpdateActionMulliganRecommend$/i,
        /^onUpdateMulliganActionRecommend$/i
      ];
      for (const key of keys) {
        if (HOOK_NAMES.has(key)) continue;
        const value = window[key];
        if (typeof value === "function" && aliases.some((re) => re.test(key)))
          HOOK_NAMES.add(key);
      }
    } catch (_) {}
  };

  const hookAll = () => {
    collectDynamicHookNames();
    for (const n of HOOK_NAMES) hook(n);
  };

  hookAll();

  const pokeRecommendRefresh = () => {
    const names = [
      "onUpdateLadderActionRecommend",
      "onUpdateMulliganRecommend",
      "onUpdateLadderMulliganRecommend",
      "onUpdateActionMulliganRecommend",
      "onUpdateMulliganActionRecommend",
      "onMulliganRecommendChanged",
      "fReplaceCard",
      "fWasterCard"
    ];

    for (const name of names) {
      try {
        const fn = window[name];
        if (typeof fn !== "function") continue;
        if (fn.__hsHooked) {
          // hook 包装函数一般支持空调用，用于触发当前状态重放。
          fn.call(window);
          continue;
        }
        if (fn.length === 0) {
          fn.call(window);
        }
      } catch (_) {}
    }
  };

  const maybeEmitPolledRecommend = () => {
    try {
      const candidate = findPolledRecommendCandidate();
      if (!candidate) return;

      const fp = JSON.stringify(candidate);
      if (!fp) return;
      if (window.__hsLastPolledRecommendFp === fp)
        return;
      window.__hsLastPolledRecommendFp = fp;
      emit("pollRecommend", candidate, null);
    } catch (_) {}
  };

  const maybeActivePullLadderOpp = () => {
    try {
      if (!ENABLE_FALLBACK_CAPTURE || typeof window.fetch !== "function")
        return;

      const now = Date.now();
      const lastEmitTs = Number(window.__hsLastEmitTs || 0);
      const lastPullTs = Number(window.__hsLastPullTs || 0);
      if (window.__hsPullInFlight)
        return;
      if (lastPullTs > 0 && now - lastPullTs < 1000)
        return;
      if (lastEmitTs > 0 && now - lastEmitTs < 650)
        return;

      window.__hsLastPullTs = now;
      window.__hsPullInFlight = true;

      const fallback = "https://hs-web-embed.lushi.163.com/client-jipaiqi/ladder-opp";
      const pullUrl = String(window.location?.origin || "").includes("lushi.163.com")
        ? "/client-jipaiqi/ladder-opp"
        : fallback;

      window.fetch(pullUrl, {
        cache: "no-store",
        credentials: "include"
      })
        .then((resp) => {
          return resp.text().then((text) => ({
            ok: !!resp.ok,
            text,
            status: Number(resp.status || 0),
            finalUrl: String(resp.url || pullUrl)
          }));
        })
        .then((result) => {
          if (!result || !result.ok || !result.text)
            return;
          const parsed = parsePayload(result.text);
          if (!parsed || typeof parsed !== "object")
            return;

          let serialized = "";
          try { serialized = JSON.stringify(parsed); } catch (_) {}
          if (!serialized)
            return;

          const fp = shortHash(serialized.slice(0, 8192));
          const prevFp = String(window.__hsLastPullFp || "");
          const lastPullEmitTs = Number(window.__hsLastPullEmitTs || 0);
          if (fp && prevFp === fp && now - lastPullEmitTs < 2400)
            return;

          window.__hsLastPullFp = fp;
          window.__hsLastPullEmitTs = Date.now();
          emit("fetch_recommend", {
            url: result.finalUrl,
            data: parsed,
            status: result.status,
            via: "active_pull"
          }, null);
        })
        .catch(() => {})
        .finally(() => {
          window.__hsPullInFlight = false;
        });
    } catch (_) {
      window.__hsPullInFlight = false;
    }
  };

  if (ENABLE_FALLBACK_CAPTURE && !window.__hsHookFetch && typeof window.fetch === "function") {
    const rawFetch = window.fetch;
    const wrappedFetch = async function(...args) {
      const resp = await rawFetch.apply(this, args);
      try {
        const req = args[0];
        const reqUrl = (req && typeof req === "object" && "url" in req) ? req.url : req;
        const url = String(reqUrl || resp.url || "");
        if (shouldCaptureUrl(url)) {
          resp.clone().text().then((text) => {
            if (!text) return;
            const payload = parsePayload(text);
            if (payload && typeof payload === "object")
              emit("fetch_recommend", { url, data: payload }, null);
          }).catch(() => {});
        }
      } catch (_) {}
      return resp;
    };
    wrappedFetch.__hsHooked = true;
    window.fetch = wrappedFetch;
    window.__hsHookFetch = true;
  }

  if (ENABLE_FALLBACK_CAPTURE && !window.__hsHookXhr && window.XMLHttpRequest && window.XMLHttpRequest.prototype) {
    try {
      const proto = window.XMLHttpRequest.prototype;
      const rawOpen = proto.open;
      const rawSend = proto.send;

      proto.open = function(method, url, ...rest) {
        this.__hsHookUrl = url;
        return rawOpen.call(this, method, url, ...rest);
      };

      proto.send = function(...args) {
        try {
          this.addEventListener("load", function() {
            try {
              const url = String(this.__hsHookUrl || this.responseURL || "");
              if (!shouldCaptureUrl(url)) return;
              const text = typeof this.responseText === "string" ? this.responseText : "";
              if (!text) return;
              const payload = parsePayload(text);
              if (payload && typeof payload === "object")
                emit("xhr_recommend", { url, data: payload }, null);
            } catch (_) {}
          }, { once: true });
        } catch (_) {}
        return rawSend.apply(this, args);
      };

      window.__hsHookXhr = true;
    } catch (_) {}
  }

  if (!window.__hsHookLoop) {
    window.__hsHookLoop = setInterval(() => {
      hookAll();
      if (ENABLE_FALLBACK_CAPTURE) {
        maybeEmitPolledRecommend();
        maybeActivePullLadderOpp();
      }
      const lastEmitTs = Number(window.__hsLastEmitTs || 0);
      if (!lastEmitTs || Date.now() - lastEmitTs > 1200)
        pokeRecommendRefresh();
    }, 250);
  }

  // attach 后先主动尝试一次，减少首轮等待
  if (ENABLE_FALLBACK_CAPTURE) {
    maybeEmitPolledRecommend();
  }
  pokeRecommendRefresh();
})();
`;
}

function parseConsoleText(arg) {
  if (!arg || typeof arg !== "object") return "";
  if (typeof arg.value === "string") return arg.value;
  if (typeof arg.value === "number" || typeof arg.value === "boolean") {
    return String(arg.value);
  }
  if (typeof arg.description === "string") return arg.description;
  return "";
}

function isActionArrayLike(arr) {
  if (!Array.isArray(arr)) return false;
  let checked = 0;
  for (const item of arr) {
    if (item && typeof item === "object") {
      if ("actionName" in item || "action" in item || "name" in item) {
        return true;
      }
    }
    if (++checked >= 8) break;
  }
  return false;
}

function hasActionPayload(payload) {
  if (!payload || typeof payload !== "object") return false;
  if (isActionArrayLike(payload)) return true;

  const preferred = ["data", "A", "a", "planA", "plan_a", "recommendA", "recommend_a", "actions", "steps", "args"];
  for (const key of preferred) {
    if (!(key in payload)) continue;
    const v = payload[key];
    if (isActionArrayLike(v)) return true;
    if (v && typeof v === "object") {
      for (const nested of Object.values(v)) {
        if (isActionArrayLike(nested)) return true;
      }
    }
  }
  return false;
}

function hasMulliganSignal(payload) {
  if (payload === null || payload === undefined) return false;
  let text = "";
  try {
    text = typeof payload === "string" ? payload : JSON.stringify(payload);
  } catch {
    text = String(payload);
  }
  const lower = text.toLowerCase();
  return lower.includes("mulligan")
    || lower.includes("replace")
    || lower.includes("reroll")
    || lower.includes("swap")
    || lower.includes("waster")
    || lower.includes("highlight");
}

function isArgsOnlyPayload(payload) {
  if (!payload || typeof payload !== "object" || Array.isArray(payload))
    return false;
  const keys = Object.keys(payload);
  return keys.length === 1 && keys[0] === "args" && Array.isArray(payload.args);
}

function hasCardIdSignal(payload) {
  if (!payload) return false;
  if (payload && typeof payload === "object" && !Array.isArray(payload)) {
    if (typeof payload.cardId === "string" && payload.cardId.trim()) {
      return true;
    }
  }
  return !!extractCardIdLike(payload);
}

function normalizePayloadForSource(source, payload) {
  if (!stringEqualsIgnoreCase(source, "highLightAction"))
    return payload;

  if (payload && typeof payload === "object" && !Array.isArray(payload)) {
    if (typeof payload.cardId === "string" && payload.cardId.trim()) {
      return { cardId: payload.cardId.trim() };
    }
    if (Array.isArray(payload.args)) {
      const cardIdFromArgs = extractCardIdLike(payload.args.join("|"));
      if (cardIdFromArgs) {
        return { cardId: cardIdFromArgs };
      }
    }
  }

  const cardId = extractCardIdLike(payload);
  if (cardId) {
    return { cardId };
  }
  return payload;
}

function stringEqualsIgnoreCase(a, b) {
  return String(a || "").toLowerCase() === String(b || "").toLowerCase();
}

function shouldKeepRecommendationEvent(source, payload) {
  const src = String(source || "");
  if (!src)
    return false;

  if (isArgsOnlyPayload(payload))
    return false;

  if (stringEqualsIgnoreCase(src, "onUpdateLadderActionRecommend")) {
    return hasActionPayload(payload) || hasMulliganSignal(payload) || hasCardIdSignal(payload);
  }

  if (stringEqualsIgnoreCase(src, "highLightAction")) {
    return hasCardIdSignal(payload) || hasMulliganSignal(payload);
  }

  if (stringEqualsIgnoreCase(src, "onUpdateMulliganRecommend")
    || stringEqualsIgnoreCase(src, "onUpdateLadderMulliganRecommend")
    || stringEqualsIgnoreCase(src, "onUpdateActionMulliganRecommend")
    || stringEqualsIgnoreCase(src, "onUpdateMulliganActionRecommend")
    || stringEqualsIgnoreCase(src, "onMulliganRecommendChanged")
    || stringEqualsIgnoreCase(src, "fReplaceCard")
    || stringEqualsIgnoreCase(src, "fWasterCard")) {
    return hasMulliganSignal(payload) || hasCardIdSignal(payload);
  }

  if (stringEqualsIgnoreCase(src, "pollRecommend")
    || stringEqualsIgnoreCase(src, "fetch_recommend")
    || stringEqualsIgnoreCase(src, "xhr_recommend")) {
    return hasActionPayload(payload) || hasMulliganSignal(payload) || hasCardIdSignal(payload);
  }

  if (stringEqualsIgnoreCase(src, "network_ws")
    || stringEqualsIgnoreCase(src, "network_response")
    || stringEqualsIgnoreCase(src, "dom_recommend")) {
    return hasActionPayload(payload) || hasMulliganSignal(payload) || hasCardIdSignal(payload);
  }

  return false;
}

function shouldLogDiagEvent(diag) {
  if (!diag || typeof diag !== "object")
    return false;

  const source = String(diag.source || "");
  if (!source)
    return false;

  const keys = Array.isArray(diag.payloadKeysTopN)
    ? diag.payloadKeysTopN.filter((k) => !stringEqualsIgnoreCase(k, "args"))
    : [];
  if (keys.length === 0 && !diag.hasActionPayload && !diag.hasMulliganSignal) {
    return false;
  }

  if (stringEqualsIgnoreCase(source, "onUpdateLadderActionRecommend")) {
    return !!diag.hasActionPayload || !!diag.hasMulliganSignal;
  }

  if (stringEqualsIgnoreCase(source, "highLightAction")) {
    return !!diag.hasMulliganSignal;
  }

  if (stringEqualsIgnoreCase(source, "onUpdateMulliganRecommend")
    || stringEqualsIgnoreCase(source, "onUpdateLadderMulliganRecommend")
    || stringEqualsIgnoreCase(source, "onUpdateActionMulliganRecommend")
    || stringEqualsIgnoreCase(source, "onUpdateMulliganActionRecommend")
    || stringEqualsIgnoreCase(source, "onMulliganRecommendChanged")
    || stringEqualsIgnoreCase(source, "fReplaceCard")
    || stringEqualsIgnoreCase(source, "fWasterCard")) {
    return !!diag.hasMulliganSignal;
  }

  if (stringEqualsIgnoreCase(source, "pollRecommend")
    || stringEqualsIgnoreCase(source, "fetch_recommend")
    || stringEqualsIgnoreCase(source, "xhr_recommend")) {
    return !!diag.hasActionPayload || !!diag.hasMulliganSignal;
  }

  if (stringEqualsIgnoreCase(source, "network_ws")
    || stringEqualsIgnoreCase(source, "network_response")
    || stringEqualsIgnoreCase(source, "dom_recommend")) {
    return !!diag.hasActionPayload || !!diag.hasMulliganSignal;
  }

  return false;
}

function normalizeTrackerJsonText(raw) {
  if (typeof raw !== "string") return raw;
  return raw
    .replaceAll("opp-target-hero", "oppTargetHero")
    .replaceAll("opp-target", "oppTarget")
    .replaceAll("target-hero", "targetHero");
}

function tryParseStructuredPayload(raw) {
  if (raw && typeof raw === "object") return raw;
  if (typeof raw !== "string") return null;

  const fixed = normalizeTrackerJsonText(raw.trim());
  if (!fixed) return null;

  try {
    return JSON.parse(fixed);
  } catch {
    try {
      if ((fixed.startsWith("{") && fixed.endsWith("}")) || (fixed.startsWith("[") && fixed.endsWith("]"))) {
        const loose = (new Function("return (" + fixed + ");"))();
        if (loose && typeof loose === "object") return loose;
      }
    } catch {
      // ignore
    }
  }

  return null;
}

function isRecommendUrl(url) {
  const u = String(url || "").toLowerCase();
  if (!u) return false;
  return u.includes("recommend")
    || u.includes("jipaiqi")
    || u.includes("mulligan")
    || u.includes("analysis")
    || u.includes("lushi.163.com")
    || u.includes("action")
    || u.includes("decision")
    || u.includes("plan");
}

function extractCardIdLike(value) {
  const text = String(value || "");
  const m = text.match(/\b[A-Z]{2,10}_[A-Za-z0-9]{2,24}\b/);
  if (!m) return "";
  const id = m[0];
  if (id.startsWith("CORE_REV_")) return "";
  return id;
}

async function runOnce(cfg) {
  const listUrl = `http://${cfg.host}:${cfg.port}/json/list`;
  const tabs = await getJson(listUrl);
  const targets = pickTargets(tabs, cfg.hint, 4);
  if (targets.length === 0) {
    const summary = Array.isArray(tabs)
      ? tabs
        .slice(0, 8)
        .map((t) => `${t?.type || "?"}:${t?.title || "<no-title>"}|${t?.url || "<no-url>"}`)
        .join(" || ")
      : "tabs unavailable";
    throw new Error(`no target matched hint "${cfg.hint}" ; tabs=${summary}`);
  }
  const primary = targets[0];
  process.stderr.write(
    `[hsbox-cdp] target picked: ${primary.title || "<no-title>"} | ${primary.url}\n`
  );
  process.stderr.write(
    `[hsbox-cdp] target shortlist: ${targets.map((t) => `${t.title || "<no-title>"}|${t.url || "<no-url>"}|score=${t.__score || 0}`).join(" || ")}\n`
  );

  const emitLine = (obj) => {
    // 数据只写 stdout，由 C# 端通过管道接收后写入 JSONL
    process.stdout.write(JSON.stringify(obj) + "\n");
  };
  let lastEmitFingerprint = "";
  let lastEmitTs = 0;
  const DUPLICATE_EMIT_WINDOW_MS = 2600;

  const emitRecommendation = (source, payload, extra = null) => {
    const normalizedPayload = normalizePayloadForSource(source, payload);
    if (!shouldKeepRecommendationEvent(source, normalizedPayload))
      return;

    const event = {
      source,
      ts: Date.now(),
      payload: normalizedPayload
    };
    if (extra && typeof extra === "object") {
      for (const [k, v] of Object.entries(extra))
        event[k] = v;
    }

    // 短窗去重：抑制 poke 导致的同一推荐反复落盘。
    // 仅按时间窗口去重，不跨长时间保留，避免跨回合误伤。
    let fingerprint = "";
    try {
      fingerprint = JSON.stringify({
        source: event.source,
        payload: event.payload,
        target: event.target || "",
        via: event.via || "",
        url: event.url || ""
      });
    } catch {
      fingerprint = "";
    }
    if (fingerprint
      && fingerprint === lastEmitFingerprint
      && event.ts - lastEmitTs <= DUPLICATE_EMIT_WINDOW_MS) {
      return;
    }
    if (fingerprint) {
      lastEmitFingerprint = fingerprint;
      lastEmitTs = event.ts;
    }

    emitLine(event);
    if (hasActionPayload(normalizedPayload)) {
      process.stderr.write(
        `[hsbox-cdp] captured action array: source=${source}\n`
      );
    }
  };

  const handleStructuredBody = (source, body, meta) => {
    const parsed = tryParseStructuredPayload(body);
    if (parsed) {
      if (hasActionPayload(parsed)) {
        emitRecommendation(source, parsed, meta);
        return;
      }

      const serialized = JSON.stringify(parsed);
      const lower = serialized.toLowerCase();
      const hasRecommendSignal = lower.includes("recommend")
        || lower.includes("mulligan")
        || lower.includes("replace")
        || lower.includes("reroll")
        || lower.includes("swap")
        || lower.includes("highlight")
        || lower.includes("action");
      if (hasRecommendSignal) {
        // 留牌等场景常不是动作数组，仍需落盘给上层解析。
        emitRecommendation(source, parsed, meta);
      }
      const cardId = hasRecommendSignal ? extractCardIdLike(serialized) : "";
      if (cardId) {
        emitRecommendation("highLightAction", { cardId }, meta);
      }
      return;
    }

    const rawLower = String(body || "").toLowerCase();
    const hasRecommendSignal = rawLower.includes("recommend")
      || rawLower.includes("mulligan")
      || rawLower.includes("replace")
      || rawLower.includes("reroll")
      || rawLower.includes("swap")
      || rawLower.includes("highlight")
      || rawLower.includes("action");
    if (!hasRecommendSignal)
      return;

    const cardId = extractCardIdLike(body);
    if (cardId) {
      emitRecommendation("highLightAction", { cardId }, meta);
    }
  };

  const attachOneTarget = async (target) => {
    const wsUrl = target?.webSocketDebuggerUrl;
    if (!wsUrl)
      throw new Error("target has no webSocketDebuggerUrl");

    const cdp = createCdp(wsUrl);
    await cdp.onOpen;

    const wsUrlByRequestId = new Map();
    const seenBodies = new Set();

    cdp.onEvent = (method, params) => {
      if (method === "Runtime.consoleAPICalled") {
        const args = params?.args || [];
        for (const a of args) {
          const txt = parseConsoleText(a);
          if (txt.startsWith(DIAG_PREFIX)) {
            const body = txt.slice(DIAG_PREFIX.length);
            try {
              const diag = JSON.parse(body);
              if (!shouldLogDiagEvent(diag))
                continue;
            } catch {
              if (body.includes("\"payloadKeysTopN\":[\"args\"]")) {
                continue;
              }
            }
            process.stderr.write(`[hsbox-cdp][diag] ${body}\n`);
            continue;
          }
          if (!txt.startsWith(PREFIX)) continue;
          const body = txt.slice(PREFIX.length);
          try {
            const parsed = JSON.parse(body);
            const parsedSource = String(parsed?.source || "");
            if (!parsedSource)
              continue;

            const parsedPayload = normalizePayloadForSource(parsedSource, parsed?.payload);
            emitRecommendation(parsedSource, parsedPayload, null);
          } catch {
            // ignore unstructured noise
          }
        }
        return;
      }

      if (method === "Network.webSocketCreated") {
        const requestId = String(params?.requestId || "");
        const url = String(params?.url || "");
        if (requestId && url) wsUrlByRequestId.set(requestId, url);
        return;
      }

      if (method === "Network.webSocketFrameReceived") {
        const requestId = String(params?.requestId || "");
        const frameData = String(params?.response?.payloadData || "");
        const url = wsUrlByRequestId.get(requestId) || "";

        if (!frameData)
          return;

        const maybeInteresting = isRecommendUrl(url)
          || frameData.includes("actionName")
          || frameData.includes("recommend")
          || frameData.includes("highLight")
          || !!extractCardIdLike(frameData);

        if (!maybeInteresting)
          return;

        handleStructuredBody("network_ws", frameData, {
          url,
          target: String(target?.url || "")
        });
        return;
      }

      if (method === "Network.responseReceived") {
        const requestId = String(params?.requestId || "");
        if (!requestId)
          return;

        const url = String(params?.response?.url || "");
        if (!isRecommendUrl(url))
          return;

        cdp.call("Network.getResponseBody", { requestId })
          .then((result) => {
            if (!result || typeof result.body !== "string")
              return;

            const rawBody = result.base64Encoded
              ? Buffer.from(result.body, "base64").toString("utf8")
              : result.body;
            const key = `${requestId}:${rawBody.slice(0, 180)}`;
            if (seenBodies.has(key))
              return;
            if (seenBodies.size > 128)
              seenBodies.clear();
            seenBodies.add(key);

            handleStructuredBody("network_response", rawBody, {
              url,
              target: String(target?.url || "")
            });
          })
          .catch(() => { });
      }
    };

    const injectExpression = buildInjectCode();
    const injectedContextIds = new Set();
    let injectFailCount = 0;

    const inject = (contextId = null) => {
      const payload = {
        expression: injectExpression,
        includeCommandLineAPI: false,
        returnByValue: true,
        awaitPromise: false,
      };
      if (Number.isInteger(contextId) && contextId > 0) {
        payload.contextId = contextId;
      }

      return cdp.call("Runtime.evaluate", payload)
        .then((injectResult) => {
          if (injectResult?.exceptionDetails) {
            const errText = String(injectResult.exceptionDetails.text || "unknown");
            process.stderr.write(`[hsbox-cdp] inject exception: ${errText} @ ${target.url}\n`);
          }
        })
        .catch((err) => {
          injectFailCount++;
          if (injectFailCount <= 3) {
            const cid = Number.isInteger(contextId) && contextId > 0 ? `context=${contextId}` : "context=default";
            process.stderr.write(`[hsbox-cdp] inject failed (${cid}): ${String(err?.message || err)} @ ${target.url}\n`);
          }
        });
    };

    await cdp.call("Runtime.enable");
    await cdp.call("Network.enable");
    await cdp.call("Page.enable");
    await inject();

    cdp.onEvent = ((base) => (method, params) => {
      base(method, params);

      if (method === "Runtime.executionContextCreated") {
        const contextId = Number(params?.context?.id || 0);
        if (contextId > 0 && !injectedContextIds.has(contextId)) {
          injectedContextIds.add(contextId);
          inject(contextId);
        }
        return;
      }

      if (method === "Runtime.executionContextDestroyed") {
        const contextId = Number(params?.executionContextId || 0);
        if (contextId > 0) {
          injectedContextIds.delete(contextId);
        }
        return;
      }

      if (method === "Page.frameNavigated") {
        inject();
      }
    })(cdp.onEvent);

    // ── 队列轮询: 定期读取注入代码存入的推荐队列 ──
    const QUEUE_POLL_MS = 250;

    const queuePollExpression = `
    (() => {
      const now = Date.now();

      // 仅在静默阶段 poke，避免同一推荐反复回放
      const lastPoke = Number(window.__hsLastPokeTs || 0);
      const lastEmit = Number(window.__hsLastEmitTs || 0);
      if ((!lastEmit || now - lastEmit >= 1300) && now - lastPoke >= 1200) {
        window.__hsLastPokeTs = now;
        const names = [
          "onUpdateLadderActionRecommend",
          "onUpdateMulliganRecommend",
          "onUpdateLadderMulliganRecommend"
        ];
        for (const name of names) {
          try {
            const fn = window[name];
            if (typeof fn === "function") fn.call(window);
          } catch(_){}
        }
      }

      // 取走队列数据
      if (window.__hsRecQueue && window.__hsRecQueue.length > 0) {
        const items = window.__hsRecQueue.splice(0);
        return JSON.stringify({ queueItems: items });
      }

      return JSON.stringify({ skip: true });
    })()
    `;

    let pollTimer = null;

    const startPoll = () => {
      if (pollTimer) return;
      pollTimer = setInterval(async () => {
        try {
          const evalResult = await cdp.call("Runtime.evaluate", {
            expression: queuePollExpression,
            returnByValue: true,
            awaitPromise: false,
          });

          const raw = evalResult?.result?.value;
          if (!raw || typeof raw !== "string") return;

          const parsed = JSON.parse(raw);
          if (parsed.skip) return;

          if (Array.isArray(parsed.queueItems)) {
            for (const item of parsed.queueItems) {
              const src = String(item?.source || "queue_recommend");
              const normalizedPayload = normalizePayloadForSource(src, item?.payload);
              emitRecommendation(src, normalizedPayload, {
                target: String(target?.url || ""),
                via: "direct_queue"
              });
            }
          }
        } catch (_) {
          // CDP 连接断开或页面不可用时静默忽略
        }
      }, QUEUE_POLL_MS);
    };

    // CDP 关闭时清理定时器
    const prevOnClose = cdp.onClose;
    cdp.onClose = () => {
      if (pollTimer) {
        clearInterval(pollTimer);
        pollTimer = null;
      }
      if (typeof prevOnClose === "function") {
        try { prevOnClose(); } catch { }
      }
    };

    startPoll();

    process.stderr.write(
      `[hsbox-cdp] attached: ${target.title || "<no-title>"} | ${target.url}\n`
    );
    return cdp;
  };

  const sessions = [];
  // 同时 attach 候选页，降低单页 miss 导致的漏采
  for (const target of targets) {
    try {
      const cdp = await attachOneTarget(target);
      sessions.push({ cdp, target });
    } catch (err) {
      process.stderr.write(`[hsbox-cdp] attach failed: ${String(err.message || err)} | ${target.url || "<no-url>"}\n`);
    }
  }

  if (sessions.length === 0) {
    // 不再使用持久文件流，无需关闭
    throw new Error("attach failed for all targets");
  }

  process.stderr.write(
    `[hsbox-cdp] ready (prefix ${PREFIX}, session=1)\n`
  );

  // Keep process alive until Ctrl+C or all sockets close.
  const endReason = await new Promise((resolve) => {
    let done = false;
    let closedCount = 0;
    const finalize = (reason) => {
      if (done) return;
      done = true;
      for (const s of sessions) {
        try { s.cdp.close(); } catch { }
      }
      // 不再使用持久文件流，无需关闭
      resolve(reason);
    };

    const onSig = () => {
      finalize("signal");
    };
    process.once("SIGINT", onSig);
    process.once("SIGTERM", onSig);

    for (const s of sessions) {
      const prevOnClose = s.cdp.onClose;
      s.cdp.onClose = () => {
        if (typeof prevOnClose === "function") {
          try { prevOnClose(); } catch { }
        }
        closedCount++;
        if (closedCount >= sessions.length) {
          finalize("all_sockets_closed");
        }
      };
    }
  });

  return endReason;
}

async function main() {
  const cfg = parseArgs(process.argv);
  process.stderr.write(
    `[hsbox-cdp] host=${cfg.host} port=${cfg.port} hint=${cfg.hint} version=${SCRIPT_VERSION}\n`
  );

  // Retry loop: user often starts HS box after this script.
  while (true) {
    try {
      const reason = await runOnce(cfg);
      if (reason === "signal")
        return;

      process.stderr.write(`[hsbox-cdp] detached (${reason}), retry in 1s...\n`);
      await sleep(1000);
    } catch (err) {
      process.stderr.write(`[hsbox-cdp] ${String(err.message || err)}\n`);
      process.stderr.write("[hsbox-cdp] retry in 2s...\n");
      await sleep(2000);
    }
  }
}

main().catch((err) => {
  process.stderr.write(`[hsbox-cdp] fatal: ${String(err.stack || err)}\n`);
  process.exit(1);
});
