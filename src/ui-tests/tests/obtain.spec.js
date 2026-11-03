// @ts-check
"use strict";

const { test, expect, HostError } = require("./host");

const SLOT = "C:/saves/ManualSave-1/sav.dat";

/** @param {import("@playwright/test").Page} page */
function card(page, key) {
  return page.locator(`#grid .item[data-key="${key}"]`);
}

test("标志性武器默认放入储藏室，确认后按储藏室写入", async ({ page, host }) => {
  await host.open();
  const text = await host.text("zh-cn");
  await card(page, "ranged:Items.Preset_Iconic").click();
  const [backpack, stash] = [page.locator("#obtain-destination .seg").nth(0), page.locator("#obtain-destination .seg").nth(1)];
  await expect(stash).toHaveClass(/active/);
  await expect(stash).toBeEnabled();
  await expect(backpack).not.toHaveClass(/active/);
  await expect(page.locator(".dest-tip")).toHaveText(text.destIconicTip);
  await page.keyboard.press("Enter");
  await expect(page.locator("#confirm")).toBeHidden();
  expect(host.paramsOf("library.obtain")).toEqual([
    { gameId: "cp2077", slot: SLOT, target: "Items.Preset_Iconic", quantity: null, toStash: true }
  ]);
});

test("改选品阶和背包后，按所选的写入", async ({ page, host }) => {
  await host.open();
  await card(page, "ranged:Items.Preset_Iconic").click();
  await page.locator("#obtain-variants .seg", { hasText: "史诗" }).click();
  await page.locator("#obtain-destination .seg").nth(0).click();
  await expect(page.locator("#obtain-destination .seg").nth(0)).toHaveClass(/active/);
  await page.keyboard.press("Enter");
  await expect(page.locator("#confirm")).toBeHidden();
  expect(host.paramsOf("library.obtain")).toEqual([
    { gameId: "cp2077", slot: SLOT, target: "Items.Preset_Iconic_Epic", quantity: null, toStash: false }
  ]);
});

test("存档没有储藏室记录：储藏室不可选，标志性武器放入背包并说明怎么解锁", async ({ page, host }) => {
  host.state.stashAvailable = false;
  await host.open();
  const text = await host.text("zh-cn");
  await card(page, "ranged:Items.Preset_Iconic").click();
  await expect(page.locator("#obtain-destination .seg").nth(1)).toBeDisabled();
  await expect(page.locator("#obtain-destination .seg").nth(0)).toHaveClass(/active/);
  await expect(page.locator(".dest-tip")).toHaveText(text.destStashUnavailable);
  await page.keyboard.press("Enter");
  await expect(page.locator("#confirm")).toBeHidden();
  expect(host.paramsOf("library.obtain")[0]).toMatchObject({ target: "Items.Preset_Iconic", toStash: false });
});

test("手雷不能放储藏室，没有存放位置选项", async ({ page, host }) => {
  await host.open();
  await card(page, "grenade:Items.Grenade").click();
  await expect(page.locator("#confirm")).toBeVisible();
  await expect(page.locator("#obtain-destination")).toHaveCount(0);
});

test("堆叠物品的数量不能超过离上限还剩的数量", async ({ page, host }) => {
  await host.open();
  const text = await host.text("zh-cn");
  await card(page, "consumable:Items.Food").click();
  const quantity = page.locator("#obtain-quantity");
  const run = page.locator("#confirm-actions button", { hasText: text.obtainBtn });
  // 持有 5、上限 8：最多再加 3 个。
  await expect(quantity).toHaveAttribute("max", "3");
  await quantity.fill("4");
  await expect(run).toBeDisabled();
  await quantity.fill("3");
  await expect(run).toBeEnabled();
  await run.click();
  await expect(page.locator("#confirm")).toBeHidden();
  expect(host.paramsOf("library.obtain")).toEqual([
    { gameId: "cp2077", slot: SLOT, target: "Items.Food", quantity: 3, toStash: false }
  ]);
});

test("写入失败：对话框留着，显示原因，按钮变成重试", async ({ page, host }) => {
  host.on("library.obtain", () => {
    throw new HostError("rejected", "存档在补丁过程中被改写");
  });
  await host.open();
  const text = await host.text("zh-cn");
  await card(page, "clothing:Items.Jacket").click();
  await page.keyboard.press("Enter");
  await expect(page.locator("#confirm-error")).toContainText("存档在补丁过程中被改写");
  await expect(page.locator("#confirm")).toBeVisible();
  await expect(page.locator("#confirm-actions button", { hasText: text.obtainRetry })).toBeEnabled();
});
