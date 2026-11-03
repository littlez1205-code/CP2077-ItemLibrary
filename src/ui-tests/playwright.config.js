// @ts-check
"use strict";

const { defineConfig } = require("@playwright/test");

// 界面测试：Chromium 打开 GameTool.Desktop/wwwroot/index.html，宿主（C# 桥接）由 tests/host.js 模拟。
module.exports = defineConfig({
  testDir: "./tests",
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  reporter: process.env.CI ? [["list"], ["github"]] : "list",
  use: {
    browserName: "chromium",
    viewport: { width: 1400, height: 900 },
    trace: "retain-on-failure"
  }
});
