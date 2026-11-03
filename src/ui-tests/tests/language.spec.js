// @ts-check
"use strict";

const { test, expect } = require("./host");

/** 作者署名是专有名词，英文界面里也照原样显示。 */
const AUTHOR = "魔法苦茶籽";

/**
 * 屏幕上看得到的中文：可见元素的文字，以及 title / aria-label / placeholder / alt，外加页面标题。
 * 语言切换按钮（简 / 繁）本身不算。
 * @param {import("@playwright/test").Page} page
 */
function visibleChinese(page) {
  return page.evaluate((author) => {
    const cjk = /[㐀-鿿（）：、。]/;
    const found = [];
    const visible = (el) => !!(el.offsetWidth || el.offsetHeight || el.getClientRects().length) && getComputedStyle(el).visibility !== "hidden";
    const where = (el) => "<" + el.tagName.toLowerCase() + (el.id ? "#" + el.id : "") + ">";
    for (const el of document.querySelectorAll("body *")) {
      if (el.closest("#lang-switch") || !visible(el))
        continue;
      for (const node of el.childNodes) {
        const text = node.nodeType === Node.TEXT_NODE ? (node.textContent || "").replaceAll(author, "") : "";
        if (cjk.test(text))
          found.push(where(el) + " " + text.trim().slice(0, 60));
      }
      for (const attr of ["title", "aria-label", "placeholder", "alt"]) {
        const value = el.getAttribute(attr);
        if (value && cjk.test(value))
          found.push(where(el) + " [" + attr + "] " + value.slice(0, 60));
      }
    }
    if (cjk.test(document.title))
      found.push("document.title " + document.title);
    return found;
  }, AUTHOR);
}

test("切到英文后，各处界面都没有中文（作者署名除外）", async ({ page, host }) => {
  await host.open();
  await page.locator('#lang-switch .lang-btn[data-lang="en-us"]').click();
  await expect(page.locator("#tab-saves")).toHaveText("Saves");
  expect(host.paramsOf("app.setLanguage")).toEqual([{ language: "en-us" }]);
  const found = [];
  const scan = async (where) => {
    for (const hit of await visibleChinese(page))
      found.push(where + ": " + hit);
  };

  await scan("main");
  await page.locator('#grid .item[data-key="ranged:Items.Preset_Iconic"]').click();
  await expect(page.locator("#confirm")).toBeVisible();
  await scan("obtain");
  await page.keyboard.press("Escape");
  await page.locator('#grid .item[data-key="consumable:Items.Food"]').click();
  await expect(page.locator("#obtain-quantity")).toBeVisible();
  await scan("obtain-stack");
  await page.keyboard.press("Escape");
  await page.locator("#batch-toggle-btn").click();
  await page.locator('#grid .item[data-key="clothing:Items.Jacket"]').click();
  await scan("batch");
  await page.locator("#batch-cancel-btn").click();
  await page.locator("#tab-backups").click();
  await scan("backups");
  await page.locator("#tab-saves").click();
  await page.locator("#help-open").click();
  for (const tab of ["start", "faq", "about"]) {
    await page.locator(`[data-help-tab="${tab}"]`).click();
    await expect(page.locator(`[data-help-pane="${tab}"]`)).toBeVisible();
    await scan("help-" + tab);
  }
  await page.locator("#help-close").click();
  await page.locator("#skin-open").click();
  await scan("skin");
  await page.keyboard.press("Escape");

  expect(found).toEqual([]);
});

for (const [language, saves, noSlot] of [["en-us", "Saves", "No save selected"], ["zh-tw", "目前存檔", "未選擇存檔"]]) {
  test(`宿主给的启动语言（${language}）在应答之前就生效，不先闪简体`, async ({ page, host }) => {
    host.state.language = language;
    host.hold();
    await host.open({ query: "?lang=" + language, waitForItems: false });
    // 桥接还没有任何应答：这时看到的文字只能来自地址里的语言。
    await expect(page.locator("#tab-saves")).toHaveText(saves);
    await expect(page.locator("#slot-name")).toHaveText(noSlot);
    expect(await page.evaluate(() => document.title)).not.toBe("游戏工具");
    host.release();
    await expect(page.locator("#grid .item").first()).toBeVisible();
    await expect(page.locator("#tab-saves")).toHaveText(saves);
  });
}

test("地址里的语言不认识时按简体处理", async ({ page, host }) => {
  await host.open({ query: "?lang=constructor" });
  await expect(page.locator("#tab-saves")).toHaveText("当前存档");
  await expect(page.locator("#title")).toHaveText("Cyberpunk 2077 · 物品库");
});

test("说明在三种语言下都有内容，版本号和作者主页按钮正常", async ({ page, host }) => {
  host.on("app.openAuthorPage", () => ({}));
  await host.open();
  for (const language of ["zh-cn", "zh-tw", "en-us"]) {
    await page.locator(`#lang-switch .lang-btn[data-lang="${language}"]`).click();
    await page.locator("#help-open").click();
    for (const tab of ["start", "faq", "about"]) {
      await page.locator(`[data-help-tab="${tab}"]`).click();
      const pane = page.locator(`[data-help-pane="${tab}"]`);
      await expect(pane).toBeVisible();
      expect((await pane.innerText()).trim().length, `${language} ${tab}`).toBeGreaterThan(100);
    }
    await expect(page.locator("#help-version")).toHaveText("v1.0.7");
    await expect(page.locator("[data-help-pane] [data-author-open]")).toHaveCount(2);
    await page.locator("#help-close").click();
  }
  // 作者主页按钮交给宿主用系统浏览器打开。
  await page.locator("#help-open").click();
  await page.locator('[data-help-pane="start"] [data-author-open]').click();
  await expect.poll(() => host.paramsOf("app.openAuthorPage").length).toBe(1);
});
