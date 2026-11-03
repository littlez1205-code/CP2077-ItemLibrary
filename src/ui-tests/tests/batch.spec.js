// @ts-check
"use strict";

const { test, expect, HostError } = require("./host");

const SLOT = "C:/saves/ManualSave-1/sav.dat";
const ICONIC = "ranged:Items.Preset_Iconic";
const FOOD = "consumable:Items.Food";

/** 进入多选，选中标志性手枪和汉堡，打开确认框。 */
async function selectIconicAndFood(page) {
  await page.locator("#batch-toggle-btn").click();
  await page.locator(`#grid .item[data-key="${ICONIC}"]`).click();
  await page.locator(`#grid .item[data-key="${FOOD}"]`).click();
  await expect(page.locator("#batch-count")).toHaveText("已选 2 项");
  await page.locator("#batch-submit-btn").click();
  await expect(page.locator("#confirm")).toBeVisible();
}

test("批量获得：确认框列出数量，送出所选物品和存放方式", async ({ page, host }) => {
  await host.open();
  await selectIconicAndFood(page);
  // 装备 ×1；堆叠的消耗品默认 10 个，但汉堡离上限只剩 3 个。
  await expect(page.locator(".batch-preview-qty")).toHaveText(["×1", "×3"]);
  await page.keyboard.press("Enter");
  await expect(page.locator("#confirm")).toBeHidden();
  expect(host.paramsOf("library.batchObtain")).toEqual([{
    gameId: "cp2077",
    slot: SLOT,
    items: [
      { target: "Items.Preset_Iconic", quantity: 1, toStash: null },
      { target: "Items.Food", quantity: 3, toStash: null }
    ],
    destinationMode: "smart"
  }]);
  // 全部成功：退出多选。
  await expect(page.locator("#batch-bar")).toBeHidden();
  await expect(page.locator("#grid .item.batch-selected")).toHaveCount(0);
});

test("部分失败：逐条列出原因，失败的留在多选里可以直接重试", async ({ page, host }) => {
  host.on("library.batchObtain", (params) => ({
    totalRequested: params.items.length,
    succeeded: params.items.length - 1,
    failed: 1,
    elapsedMilliseconds: 12,
    results: params.items.map((entry, index) => index === 0
      ? { target: entry.target, label: "不朽手枪", success: false, error: "存档中暂无储藏箱记录" }
      : { target: entry.target, label: "汉堡", success: true })
  }));
  await host.open();
  await selectIconicAndFood(page);
  await page.keyboard.press("Enter");
  await expect(page.locator(".batch-failed-row")).toHaveCount(1);
  await expect(page.locator(".batch-failed-row")).toContainText("不朽手枪");
  await expect(page.locator(".batch-failed-row")).toContainText("存档中暂无储藏箱记录");
  await page.keyboard.press("Enter");
  await expect(page.locator("#confirm")).toBeHidden();
  await expect(page.locator("#batch-bar")).toBeVisible();
  await expect(page.locator("#batch-count")).toHaveText("已选 1 项");
  await expect(page.locator(`#grid .item[data-key="${ICONIC}"]`)).toHaveClass(/batch-selected/);
  await expect(page.locator(`#grid .item[data-key="${FOOD}"]`)).not.toHaveClass(/batch-selected/);
});

test("整批被拒绝：一件没写，确认框留着并可重试", async ({ page, host }) => {
  host.on("library.batchObtain", () => {
    throw new HostError("bad_args", "单次批次新增最多支持 50 件物品。");
  });
  await host.open();
  const text = await host.text("zh-cn");
  await selectIconicAndFood(page);
  await page.keyboard.press("Enter");
  await expect(page.locator("#confirm-error")).toContainText("单次批次新增最多支持 50 件物品。");
  await expect(page.locator("#confirm-actions button", { hasText: text.obtainRetry })).toBeEnabled();
  await expect(page.locator("#batch-count")).toHaveText("已选 2 项");
});

test("写入成功后刷新失败：只报刷新的错误，不说「未写入」", async ({ page, host }) => {
  await host.open();
  await selectIconicAndFood(page);
  host.on("saves.list", () => {
    throw new HostError("failed", "刷新存档列表失败");
  });
  await page.keyboard.press("Enter");
  await expect(page.locator("#error")).toContainText("刷新存档列表失败");
  await expect(page.locator("#confirm")).toBeHidden();
  expect(host.paramsOf("library.batchObtain")).toHaveLength(1);
});

test("存档没有储藏室记录时，「全部放入储藏室」不可选", async ({ page, host }) => {
  host.state.stashAvailable = false;
  await host.open();
  await page.locator("#batch-toggle-btn").click();
  await expect(page.locator('#batch-dest option[value="stash"]')).toBeDisabled();
  await expect(page.locator("#batch-dest")).toHaveValue("smart");
});
