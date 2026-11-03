// @ts-check
"use strict";

const path = require("node:path");
const { pathToFileURL } = require("node:url");
const base = require("@playwright/test");

/** 被测页面：桌面端嵌入的 index.html，直接从源码目录加载。 */
const INDEX_URL = pathToFileURL(path.resolve(__dirname, "../../GameTool.Desktop/wwwroot/index.html")).href;

/** 宿主应答失败：页面收到 { status: "error", error: { code, message } }。 */
class HostError extends Error {
  /** @param {string} code @param {string} message */
  constructor(code, message) {
    super(message);
    this.code = code;
  }
}

function item(target, label, kind, extra = {}) {
  return {
    target, label, recordName: target, kind, stackable: false, tweakDbIdHex: "00", labelShared: false, owned: false,
    quality: "Legendary", iconic: false, canStash: true, plus: 0, statBadges: [], ...extra
  };
}

function group(variants, extra = {}) {
  const first = variants[0];
  return {
    key: first.kind + ":" + first.target, label: first.label, kind: first.kind, stackable: first.stackable, variants,
    defaultIndex: 0, quality: first.quality, plus: 0, iconic: first.iconic, statBadges: [], ...extra
  };
}

/** 物品名、分类名按语言给出，与真实宿主一样（真实的名字来自玩家本机的游戏文件）。 */
const NAMES = {
  "zh-cn": { iconic: "不朽手枪", legendary: "传说", epic: "史诗", tier: "品阶", jacket: "夹克", grenade: "手雷", food: "汉堡", ranged: "远程武器", clothing: "服装", grenades: "手雷", consumables: "消耗品" },
  "zh-tw": { iconic: "不朽手槍", legendary: "傳說", epic: "史詩", tier: "品階", jacket: "夾克", grenade: "手榴彈", food: "漢堡", ranged: "遠程武器", clothing: "服裝", grenades: "手榴彈", consumables: "消耗品" },
  "en-us": { iconic: "Iconic Pistol", legendary: "Legendary", epic: "Epic", tier: "Tier", jacket: "Jacket", grenade: "Grenade", food: "Burger", ranged: "Ranged", clothing: "Clothing", grenades: "Grenades", consumables: "Consumables" }
};

/**
 * 默认的物品目录：标志性手枪（两个品阶）、夹克（已拥有）、手雷（不能放储藏室）、汉堡（堆叠，持有 5、上限 8）。
 * @param {string} language
 */
function catalog(language) {
  const name = NAMES[language] || NAMES["zh-cn"];
  return {
    kinds: [
      { id: "ranged", label: name.ranged }, { id: "clothing", label: name.clothing },
      { id: "grenade", label: name.grenades }, { id: "consumable", label: name.consumables, stackable: true }
    ],
    groups: [
      group([
        item("Items.Preset_Iconic", name.iconic, "ranged", { iconic: true, variantLabel: name.legendary }),
        item("Items.Preset_Iconic_Epic", name.iconic, "ranged", { iconic: true, quality: "Epic", variantLabel: name.epic })
      ], { variantNoun: name.tier }),
      group([item("Items.Jacket", name.jacket, "clothing", { owned: true })]),
      group([item("Items.Grenade", name.grenade, "grenade", { canStash: false })]),
      group([item("Items.Food", name.food, "consumable", { stackable: true, canStash: false, held: 5, maxQuantity: 8 })])
    ]
  };
}

/** 默认目录里的卡片数。 */
const CARD_COUNT = 4;

const SLOTS = Object.freeze([
  { slot: "C:/saves/ManualSave-1/sav.dat", name: "ManualSave-1", time: "2026-11-01T00:00:00Z", group: "manual", supported: true },
  { slot: "C:/saves/AutoSave-1/sav.dat", name: "AutoSave-1", time: "2026-10-01T00:00:00Z", group: "auto", supported: false, reason: "Unsupported version" }
]);

/**
 * 模拟的宿主：页面的 window.chrome.webview 换成假的，每个请求交给同名的应答函数。
 * 应答函数返回应答数据，抛 HostError 表示失败；用例可以用 on() 换掉其中几个。
 * calls 按顺序记下每个请求，push() 模拟宿主主动推送的事件。
 */
class FakeHost {
  /** @param {import("@playwright/test").Page} page */
  constructor(page) {
    this.page = page;
    /** @type {{ method: string, params: any }[]} */
    this.calls = [];
    /** 页面脚本错误和控制台错误：每个用例结束时必须为空。 */
    this.errors = [];
    /** hold() 之后的应答都先等着，release() 才放行：用来检查页面在宿主应答之前的样子。 */
    this.gate = null;
    this.releaseGate = null;
    this.state = { language: "zh-cn", stashAvailable: true };
    /** @type {Record<string, (params: any) => any>} */
    this.handlers = {
      "app.games": () => [{ id: "cp2077", displayName: "Cyberpunk 2077" }],
      "app.info": () => ({ version: "1.0.7", helpShown: true, language: this.state.language }),
      "app.setLanguage": (params) => {
        this.state.language = params.language;
        return {};
      },
      "app.markHelpShown": () => ({}),
      "window.state": () => ({ maximized: false, active: true }),
      "theme.get": () => ({ accent: "#f3e600", accentText: "#000" }),
      "skin.get": () => ({ overlayOpacity: 0.5, hasImage: false }),
      "settings.get": () => ({ ready: true }),
      "saves.list": () => SLOTS,
      "backup.list": () => [],
      "library.catalog": (params) => {
        const { kinds, groups } = catalog(this.state.language);
        return {
          namesAvailable: true,
          kinds,
          kind: params.kind || "ranged",
          total: groups.length,
          itemTotal: groups.length,
          skip: 0,
          take: 50,
          groups,
          hidden: [],
          stashAvailable: this.state.stashAvailable
        };
      },
      "library.obtain": (params) => ({ target: params.target, label: "x", stackable: false, quantity: params.quantity ?? 1, toStash: !!params.toStash }),
      "library.batchObtain": (params) => ({
        totalRequested: params.items.length,
        succeeded: params.items.length,
        failed: 0,
        elapsedMilliseconds: 5,
        results: params.items.map((entry) => ({ target: entry.target, label: entry.target, success: true }))
      })
    };
    page.on("pageerror", (error) => this.errors.push("pageerror: " + error.message));
    page.on("console", (message) => {
      if (message.type() === "error")
        this.errors.push("console: " + message.text());
    });
  }

  /** 之后的请求先不应答，直到 release()。 */
  hold() {
    this.gate = new Promise((resolve) => { this.releaseGate = resolve; });
  }

  release() {
    this.releaseGate?.();
    this.gate = null;
  }

  /** 换掉某个方法的应答。 */
  on(method, handler) {
    this.handlers[method] = handler;
    return this;
  }

  /** 打开页面（query 例如 "?lang=en-us"）；默认等到物品卡片出现，即启动完成并选中了第一个存档。 */
  async open({ query = "", waitForItems = true } = {}) {
    await this.page.exposeFunction("__fakeHost", (request) => this.answer(request));
    await this.page.addInitScript(() => {
      const listeners = [];
      // @ts-ignore 页面里由宿主（WebView2）提供的对象
      window.chrome = {
        webview: {
          addEventListener: (type, listener) => {
            if (type === "message")
              listeners.push(listener);
          },
          postMessage: (message) => {
            // @ts-ignore
            window.__fakeHost(message).then((reply) => {
              for (const listener of listeners)
                listener({ data: reply });
            });
          }
        }
      };
      // @ts-ignore 用例里模拟宿主推送
      window.__hostPush = (message) => {
        for (const listener of listeners)
          listener({ data: message });
      };
    });
    await this.page.goto(INDEX_URL + query);
    if (waitForItems)
      await base.expect(this.page.locator("#grid .item").first()).toBeVisible();
  }

  async answer(request) {
    const params = request.params || {};
    this.calls.push({ method: request.method, params });
    if (this.gate)
      await this.gate;
    const handler = this.handlers[request.method];
    if (!handler)
      return { id: request.id, status: "error", error: { code: "unknown_method", message: "fake host has no " + request.method } };
    try {
      return { id: request.id, status: "ok", data: await handler(params) };
    } catch (error) {
      if (error instanceof HostError)
        return { id: request.id, status: "error", error: { code: error.code, message: error.message } };
      throw error;
    }
  }

  /** 某个方法收到过的请求参数，按先后顺序。 */
  paramsOf(method) {
    return this.calls.filter((call) => call.method === method).map((call) => call.params);
  }

  /** 页面里当前语言的界面文字表（i18n.js 的 I18N），用来得到预期文字。 */
  text(language) {
    return this.page.evaluate((lang) => {
      // @ts-ignore 页面全局
      const table = I18N[lang];
      return Object.fromEntries(Object.entries(table).filter(([, value]) => typeof value === "string"));
    }, language);
  }
}

const test = base.test.extend({
  host: async ({ page }, use) => {
    const host = new FakeHost(page);
    await use(host);
    base.expect(host.errors, "页面不应有脚本错误").toEqual([]);
  }
});

module.exports = { test, expect: base.expect, HostError, CARD_COUNT };
