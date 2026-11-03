// @ts-check
"use strict";

const { test, expect, HostError, CARD_COUNT } = require("./host");

const SLOT = "C:/saves/ManualSave-1/sav.dat";

test("启动后选中第一个可用存档，列出它的物品，标题带游戏名", async ({ page, host }) => {
  await host.open();
  await expect(page.locator("#grid .item")).toHaveCount(CARD_COUNT);
  await expect(page.locator("#title")).toHaveText("Cyberpunk 2077 · 物品库");
  await expect(page.locator("#slot-name")).toHaveText("ManualSave-1");
  expect(host.paramsOf("library.catalog")[0]).toMatchObject({ gameId: "cp2077", slot: SLOT });
});

test("搜索框里的文字交给宿主查询", async ({ page, host }) => {
  await host.open();
  await page.locator("#search").fill("夹克");
  await expect.poll(() => host.paramsOf("library.catalog").map((params) => params.query)).toContain("夹克");
});

test("宿主报错时显示错误信息", async ({ page, host }) => {
  host.on("saves.list", () => {
    throw new HostError("failed", "读不到存档目录");
  });
  await host.open({ waitForItems: false });
  await expect(page.locator("#error")).toContainText("读不到存档目录");
});

test("不支持的存档：显示原因和更新提示，不读物品目录", async ({ page, host }) => {
  await host.open();
  const text = await host.text("zh-cn");
  const catalogCalls = host.paramsOf("library.catalog").length;
  await page.locator("#saves-pane .group-title", { hasText: "自动" }).click();
  await page.locator("#saves-pane .slot", { hasText: "AutoSave-1" }).click();
  await expect(page.locator("#slot-name")).toHaveText("AutoSave-1");
  await expect(page.locator("#error")).toContainText("Unsupported version");
  await expect(page.locator("#error")).toContainText(text.unsupportedHint);
  await expect(page.locator("#library")).toBeHidden();
  expect(host.paramsOf("library.catalog")).toHaveLength(catalogCalls);
});
