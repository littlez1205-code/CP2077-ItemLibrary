"use strict";

const savesPane = document.querySelector("#saves-pane");
const backupsPane = document.querySelector("#backups-pane");
const errorEl = document.querySelector("#error");
const titleEl = document.querySelector("#title");
const slotNameEl = document.querySelector("#slot-name");
const slotTimeEl = document.querySelector("#slot-time");
const backupAddEl = document.querySelector("#backup-add");
const libraryEl = document.querySelector("#library");
const searchEl = document.querySelector("#search");
const chipsEl = document.querySelector("#chips");
const metaEl = document.querySelector("#lib-meta");
const pageSizeEl = document.querySelector("#page-size");
const pagerEl = document.querySelector("#pager");
const resultsEl = document.querySelector("#results");
const gridEl = document.querySelector("#grid");
const emptyEl = document.querySelector("#lib-empty");
const backupViewEl = document.querySelector("#backup-view");
const backupRowsEl = document.querySelector("#backup-rows");
const backupActionsEl = document.querySelector("#backup-actions");
const confirmEl = document.querySelector("#confirm");
const confirmTitle = document.querySelector("#confirm-title");
const confirmPreview = document.querySelector("#confirm-preview");
const confirmText = document.querySelector("#confirm-text");
const confirmBody = document.querySelector("#confirm-body");
const confirmError = document.querySelector("#confirm-error");
const confirmActions = document.querySelector("#confirm-actions");
const toastEl = document.querySelector("#toast");

let uiLang = "zh-cn";

const I18N = {
  "zh-cn": {
    titleSuffix: " · 物品库",
    helpOpen: "说明",
    skinOpen: "外观",
    settingsOpen: "设置路径",
    winMin: "最小化",
    winMax: "最大化",
    winRestore: "还原",
    winClose: "关闭",
    tabSaves: "当前存档",
    tabBackups: "备份区",
    groups: [["quick", "快速"], ["manual", "手动"], ["auto", "自动"], ["other", "其他"]],
    qualityNames: { Legendary: "传说", Epic: "史诗", Rare: "稀有", Uncommon: "罕见", Common: "普通" },
    iconicBadge: "标志性",
    unsupportedBadge: "不支持",
    unsupportedHint: "如果是游戏更新后的新版本存档，需要等待工具适配新版本，暂时不能修改（这是保护存档的安全设计），最新版本请关注作者 B 站动态。",
    emptyBackups: "备份区是空的。选中存档后点「加入备份区」，把它的当前状态存一份到这里。",
    noSlot: "未选择存档",
    backupAdd: "加入备份区",
    backupTitlePrefix: "备份：",
    backupRowCompare: (b, c) => "备份 " + b + " ｜ 当前 " + c,
    restoreBtn: "恢复到存档",
    deleteBtn: "删除备份",
    toastBackupAdded: (n) => "已把 " + n + " 存入备份区",
    restoreDialogTitle: "恢复备份",
    restoreDialogText: (n) => "将用这份备份覆盖 " + n + " 的当前存档。",
    restoreConfirm: "恢复",
    cancel: "取消",
    toastRestored: (n) => "已恢复 " + n + "，读档后生效",
    deleteDialogTitle: "删除备份",
    deleteDialogText: (n, t) => "删除 " + n + " 在 " + t + " 的备份？",
    deleteConfirm: "删除",
    searchPlaceholder: "输入物品名，如 阿贾克斯、夹克、传说组件、网络接入仓、配方",
    pageSizeLabel: "每页",
    stateHeld: (n) => "持有 " + n,
    stateNotHeld: "未持有",
    stateOwned: "已拥有",
    stateGroupHeld: (h, t) => "持有 " + h + "/" + t,
    stateGroupOwned: (h, t) => "已拥有 " + h + "/" + t,
    variantCount: (len, noun) => {
      const n = noun || "品阶";
      return len + (n === "款式" ? " 种" : " 个") + n;
    },
    cardTipStats: "【基础常数】",
    cardTipEffect: "【特殊效果】",
    cardTipDesc: "【说明】",
    libUnavailable: "物品库不可用。",
    libEmptyKind: "这个分类里没有物品。",
    libEmptyOtherKind: (q) => "这个分类里没有与「" + q + "」匹配的物品，看看标签上带数字的分类。",
    libEmptyQuery: (q) => "没有找到与「" + q + "」匹配的物品。",
    libLoading: "加载中…",
    libMeta: (total, itemTotal, page, pages) => {
      const merged = itemTotal > total ? "（含不同品阶和款式共 " + formatNumber(itemTotal) + " 条）" : "";
      const where = pages > 1 ? (merged ? "" : " ") + "· 第 " + page + " / " + pages + " 页" : "";
      return "共 " + formatNumber(total) + " 件" + merged + where;
    },
    prevPage: "上一页",
    nextPage: "下一页",
    jumpPrefix: "跳到第",
    jumpSuffix: "页",
    obtainTitle: "获得物品",
    obtainPickCaption: (noun, len) => "选择" + (noun || "品阶") + "（" + len + "）",
    obtainBtn: "获得",
    obtainWriting: "写入中…",
    obtainRetry: "重试",
    obtainNotWritten: (msg) => "未写入：" + msg,
    obtainSummary: (slotName, itemName) => "将向〈" + slotName + "〉添加〈" + itemName + "〉，读档后生效。",
    effectHeading: "★ 特殊效果",
    qtyLabel: "数量",
    qtyNote: (heldOrNull, value, limit) => {
      const prefix = heldOrNull != null ? "当前持有 " + formatNumber(heldOrNull) : "当前未持有，将新增一个堆叠";
      if (!Number.isSafeInteger(value) || value < 1)
        return prefix + "。请输入至少为 1 的整数。";
      if (value > limit)
        return prefix + "。最多还能获得 " + formatNumber(limit) + "。";
      return prefix + "，获得后为 " + formatNumber((heldOrNull || 0) + value) + "。";
    },
    ownedNote: "存档里已经有这一件，获得后会再多一件。",
    gearQtyNote: (value, limit) => "独立物品，单次最多添加 " + limit + " 件",
    destLabel: "存放位置",
    destBackpack: "放入个人背包",
    destStash: "放入储藏室（载具后备箱 / 公寓）",
    destIconicTip: "💡 推荐存入储藏室：进游戏后从车后备箱或公寓储藏点取出，可直接触发官方等级校准并升阶至 5++！",
    destIconicBackpackWarn: "⚠️ 注意：直接放入背包的不朽武器受游戏机制限制最高只能升至 5 阶；存入储藏室取出可升至 5++。",
    toastObtained: (label, amount, toStash) => (toStash ? "已存入储藏室：" : "已放入背包：") + label + amount + "，读档后生效",
    skinTitle: "外观",
    skinDropText: "选择图片，或把图片拖到这里",
    skinClear: "清除背景",
    skinMask: "遮罩",
    setupTitle: "设置路径",
    setupSaveLabel: "存档目录",
    setupGameLabel: "游戏目录",
    setupPickBtn: "选择文件夹",
    setupUnset: "未选择",
    setupHintPrefix: "常见位置：",
    setupIntroFirst: "第一次使用，请先选择存档目录和游戏目录。两项都通过检查后就能开始使用，以后可以点右上角的齿轮修改。",
    setupIntroEdit: "修改后点「保存」，存档列表、物品目录和图标会按新路径重新加载。",
    setupDoneFirst: "开始使用",
    setupDoneSave: "保存",
    setupFilePrefix: "设置保存在 ",
    toastSetupFirst: "设置完成",
    toastSetupSaved: "路径已保存，已按新路径重新加载",
    helpTitle: "说明",
    helpTabStart: "快速上手",
    helpTabFaq: "常见问题",
    helpTabAbout: "关于"
  },
  "zh-tw": {
    titleSuffix: " · 物品庫",
    helpOpen: "說明",
    skinOpen: "外觀",
    settingsOpen: "設定路徑",
    winMin: "最小化",
    winMax: "最大化",
    winRestore: "還原",
    winClose: "關閉",
    tabSaves: "目前存檔",
    tabBackups: "備份區",
    groups: [["quick", "快速"], ["manual", "手動"], ["auto", "自動"], ["other", "其他"]],
    qualityNames: { Legendary: "傳說", Epic: "史詩", Rare: "稀有", Uncommon: "罕見", Common: "普通" },
    iconicBadge: "標誌性",
    unsupportedBadge: "不支援",
    unsupportedHint: "如果是遊戲更新後的新版本存檔，需要等待工具適配新版本，暫時不能修改（這是保護存檔的安全設計），最新版本請關注作者 B 站動態。",
    emptyBackups: "備份區是空的。選取存檔後點「加入備份區」，把它的目前狀態存一份到這裡。",
    noSlot: "未選擇存檔",
    backupAdd: "加入備份區",
    backupTitlePrefix: "備份：",
    backupRowCompare: (b, c) => "備份 " + b + " ｜ 目前 " + c,
    restoreBtn: "還原到存檔",
    deleteBtn: "刪除備份",
    toastBackupAdded: (n) => "已把 " + n + " 存入備份區",
    restoreDialogTitle: "還原備份",
    restoreDialogText: (n) => "將用這份備份覆蓋 " + n + " 的目前存檔。",
    restoreConfirm: "還原",
    cancel: "取消",
    toastRestored: (n) => "已還原 " + n + "，讀檔後生效",
    deleteDialogTitle: "刪除備份",
    deleteDialogText: (n, t) => "刪除 " + n + " 在 " + t + " 的備份？",
    deleteConfirm: "刪除",
    searchPlaceholder: "輸入物品名，如 阿賈克斯、夾克、傳說組件、網路接入倉、配方",
    pageSizeLabel: "每頁",
    stateHeld: (n) => "持有 " + n,
    stateNotHeld: "未持有",
    stateOwned: "已擁有",
    stateGroupHeld: (h, t) => "持有 " + h + "/" + t,
    stateGroupOwned: (h, t) => "已擁有 " + h + "/" + t,
    variantCount: (len, noun) => {
      const n = noun || "品階";
      return len + (n === "款式" ? " 種" : " 個") + n;
    },
    cardTipStats: "【基礎常數】",
    cardTipEffect: "【特殊效果】",
    cardTipDesc: "【說明】",
    libUnavailable: "物品庫不可用。",
    libEmptyKind: "這個分類裡沒有物品。",
    libEmptyOtherKind: (q) => "這個分類裡沒有與「" + q + "」相符的物品，看看標籤上帶數字的分類。",
    libEmptyQuery: (q) => "沒有找到與「" + q + "」相符的物品。",
    libLoading: "載入中…",
    libMeta: (total, itemTotal, page, pages) => {
      const merged = itemTotal > total ? "（含不同品階和款式共 " + formatNumber(itemTotal) + " 條）" : "";
      const where = pages > 1 ? (merged ? "" : " ") + "· 第 " + page + " / " + pages + " 頁" : "";
      return "共 " + formatNumber(total) + " 件" + merged + where;
    },
    prevPage: "上一頁",
    nextPage: "下一頁",
    jumpPrefix: "跳到第",
    jumpSuffix: "頁",
    obtainTitle: "獲得物品",
    obtainPickCaption: (noun, len) => "選擇" + (noun || "品階") + "（" + len + "）",
    obtainBtn: "獲得",
    obtainWriting: "寫入中…",
    obtainRetry: "重試",
    obtainNotWritten: (msg) => "未寫入：" + msg,
    obtainSummary: (slotName, itemName) => "將向〈" + slotName + "〉添加〈" + itemName + "〉，讀檔後生效。",
    effectHeading: "★ 特殊效果",
    qtyLabel: "數量",
    qtyNote: (heldOrNull, value, limit) => {
      const prefix = heldOrNull != null ? "目前持有 " + formatNumber(heldOrNull) : "目前未持有，將新增一個堆疊";
      if (!Number.isSafeInteger(value) || value < 1)
        return prefix + "。請輸入至少為 1 的整數。";
      if (value > limit)
        return prefix + "。最多還能獲得 " + formatNumber(limit) + "。";
      return prefix + "，獲得後為 " + formatNumber((heldOrNull || 0) + value) + "。";
    },
    ownedNote: "存檔裡已經有這一件，獲得後會再多一件。",
    gearQtyNote: (value, limit) => "獨立物品，單次最多新增 " + limit + " 件",
    destLabel: "存放位置",
    destBackpack: "放入個人背包",
    destStash: "放入儲藏室（載具後備箱 / 公寓）",
    destIconicTip: "💡 推薦存入儲藏室：進遊戲後從車後備箱或公寓儲藏點取出，可直接啟動官方等級校準並升階至 5++！",
    destIconicBackpackWarn: "⚠️ 注意：直接放入背包的不朽武器受遊戲機制限制最高只能升至 5 階；存入儲藏室取出可升至 5++。",
    toastObtained: (label, amount, toStash) => (toStash ? "已存入儲藏室：" : "已放入個人背包：") + label + amount + "，讀檔後生效",
    skinTitle: "外觀",
    skinDropText: "選擇圖片，或把圖片拖到這裡",
    skinClear: "清除背景",
    skinMask: "遮罩",
    setupTitle: "設定路徑",
    setupSaveLabel: "存檔目錄",
    setupGameLabel: "遊戲目錄",
    setupPickBtn: "選擇資料夾",
    setupUnset: "未選擇",
    setupHintPrefix: "常見位置：",
    setupIntroFirst: "第一次使用，請先選擇存檔目錄和遊戲目錄。兩項都通過檢查後就能開始使用，以後可以點右上角的齒輪修改。",
    setupIntroEdit: "修改後點「儲存」，存檔列表、物品目錄和圖示會按新路徑重新載入。",
    setupDoneFirst: "開始使用",
    setupDoneSave: "儲存",
    setupFilePrefix: "設定保存在 ",
    toastSetupFirst: "設定完成",
    toastSetupSaved: "路徑已儲存，已按新路徑重新載入",
    helpTitle: "說明",
    helpTabStart: "快速上手",
    helpTabFaq: "常見問題",
    helpTabAbout: "關於"
  },
  "en-us": {
    titleSuffix: " · Item Library",
    helpOpen: "Help",
    skinOpen: "Theme",
    settingsOpen: "Path Settings",
    winMin: "Minimize",
    winMax: "Maximize",
    winRestore: "Restore",
    winClose: "Close",
    tabSaves: "Saves",
    tabBackups: "Backups",
    groups: [["quick", "Quick"], ["manual", "Manual"], ["auto", "Auto"], ["other", "Other"]],
    qualityNames: { Legendary: "Legendary", Epic: "Epic", Rare: "Rare", Uncommon: "Uncommon", Common: "Common" },
    iconicBadge: "Iconic",
    unsupportedBadge: "Unsupported",
    unsupportedHint: "If this save is from a newer game update, please wait for a tool update (safety lock to protect your save).",
    emptyBackups: "No backups yet. Select a save and click 'Add Backup' to save a snapshot here.",
    noSlot: "No save selected",
    backupAdd: "Add Backup",
    backupTitlePrefix: "Backup: ",
    backupRowCompare: (b, c) => "Backup " + b + " ｜ Current " + c,
    restoreBtn: "Restore Save",
    deleteBtn: "Delete Backup",
    toastBackupAdded: (n) => "Backed up " + n,
    restoreDialogTitle: "Restore Backup",
    restoreDialogText: (n) => "Overwrite current save " + n + " with this backup?",
    restoreConfirm: "Restore",
    cancel: "Cancel",
    toastRestored: (n) => "Restored " + n + "; reload save in game",
    deleteDialogTitle: "Delete Backup",
    deleteDialogText: (n, t) => "Delete backup of " + n + " from " + t + "?",
    deleteConfirm: "Delete",
    searchPlaceholder: "Search items, e.g. Ajax, Jacket, Legendary, Cyberdeck, Recipe",
    pageSizeLabel: "Per page",
    stateHeld: (n) => "Held " + n,
    stateNotHeld: "Not held",
    stateOwned: "Owned",
    stateGroupHeld: (h, t) => "Held " + h + "/" + t,
    stateGroupOwned: (h, t) => "Owned " + h + "/" + t,
    variantCount: (len, noun) => {
      const isStyle = noun === "Style" || noun === "款式";
      return len + " " + (isStyle ? "styles" : "tiers");
    },
    cardTipStats: "[Base Stats]",
    cardTipEffect: "[Special Effect]",
    cardTipDesc: "[Description]",
    libUnavailable: "Item library unavailable.",
    libEmptyKind: "No items in this category.",
    libEmptyOtherKind: (q) => "No matches for \"" + q + "\" in this category; check categories with counts above.",
    libEmptyQuery: (q) => "No items matching \"" + q + "\".",
    libLoading: "Loading…",
    libMeta: (total, itemTotal, page, pages) => {
      const merged = itemTotal > total ? " (" + formatNumber(itemTotal) + " variants)" : "";
      const where = pages > 1 ? " · Page " + page + " / " + pages : "";
      return formatNumber(total) + " items" + merged + where;
    },
    prevPage: "Prev",
    nextPage: "Next",
    jumpPrefix: "Go to",
    jumpSuffix: "",
    obtainTitle: "Get Item",
    obtainPickCaption: (noun, len) => "Select " + (noun || "Tier") + " (" + len + ")",
    obtainBtn: "Get",
    obtainWriting: "Saving…",
    obtainRetry: "Retry",
    obtainNotWritten: (msg) => "Not saved: " + msg,
    obtainSummary: (slotName, itemName) => "Add 〈" + itemName + "〉 to 〈" + slotName + "〉; reload save in game to apply.",
    effectHeading: "★ Special Effect",
    qtyLabel: "Qty",
    qtyNote: (heldOrNull, value, limit) => {
      const prefix = heldOrNull != null ? "Currently held: " + formatNumber(heldOrNull) : "Not held yet (will add stack)";
      if (!Number.isSafeInteger(value) || value < 1)
        return prefix + ". Please enter an integer of at least 1.";
      if (value > limit)
        return prefix + ". Max additional: " + formatNumber(limit) + ".";
      return prefix + "; after adding: " + formatNumber((heldOrNull || 0) + value) + ".";
    },
    ownedNote: "Already owned in this save; obtaining will add another copy.",
    gearQtyNote: (value, limit) => "Individual item, max " + limit + " per request",
    destLabel: "Storage Destination",
    destBackpack: "Personal Backpack",
    destStash: "Stash (Vehicle Trunk / Apartment)",
    destIconicTip: "💡 Recommended to store in Stash: retrieving it from vehicle trunk or apartment stash triggers game level-scaling up to Tier 5++!",
    destIconicBackpackWarn: "⚠️ Note: Iconic weapons placed directly in backpack cannot upgrade past Tier 5 due to game mechanics; storing in Stash allows upgrading to Tier 5++.",
    toastObtained: (label, amount, toStash) => (toStash ? "Stored in Stash: " : "Added to backpack: ") + label + amount + "; reload save in game",
    skinTitle: "Theme",
    skinDropText: "Choose an image or drag it here",
    skinClear: "Clear Background",
    skinMask: "Overlay",
    setupTitle: "Path Settings",
    setupSaveLabel: "Save Directory",
    setupGameLabel: "Game Directory",
    setupPickBtn: "Browse...",
    setupUnset: "Not selected",
    setupHintPrefix: "Default path: ",
    setupIntroFirst: "First-time setup: please select your Save Directory and Game Directory. Once both pass validation, click Get Started.",
    setupIntroEdit: "Click 'Save' after making changes to reload saves, item catalog, and icons from the new paths.",
    setupDoneFirst: "Get Started",
    setupDoneSave: "Save",
    setupFilePrefix: "Settings saved at ",
    toastSetupFirst: "Setup complete",
    toastSetupSaved: "Paths saved and reloaded",
    helpTitle: "Help",
    helpTabStart: "Quick Start",
    helpTabFaq: "FAQ",
    helpTabAbout: "About"
  }
};

function t() {
  return I18N[uiLang] || I18N["zh-cn"];
}

// 图标占位：按分类画线条图形（24×24）。有游戏图标时由 attachArt 叠一张图上去，加载失败就留着占位。
const glyphs = {
  clothing: ["M8.5 3.5 12 5.5l3.5-2 4 2.5-1.8 4.2-2.2-1v10.3H8.5V9.2l-2.2 1L4.5 6z", "M12 5.5v13.5"],
  ranged: ["M3 7.5h15.5v3.5H13l-1 2h-2.2l-1.3 4.5H5l1.4-4.5H3z", "M18.5 8.5H21", "M9.8 13l.7 2"],
  melee: ["M19.5 3.5 21 5 10.5 15.5l-2-2z", "M6.5 12.5l5 5", "M8.5 15.5 3.5 20.5"],
  cyberware: ["M6 9.5a6 6 0 0 1 12 0v5a6 6 0 0 1-12 0z", "M12 3.5v5M9 8.5l3 3 3-3M12 11.5v9", "M6 12h2.5M15.5 12H18"],
  grenade: ["M12 9a6 6 0 1 0 0 12 6 6 0 1 0 0-12z", "M10 9.3V6.5h4v2.8", "M14 6.5 18 4", "M17 7.5a1.6 1.6 0 1 0 2.2-2.2", "M9 15h6"],
  arm_cyberware: ["M3.5 17.5 10 11l3 3-6.5 6.5z", "M11.5 12.5 20.5 3.5l-1 5-5 5", "M8 16l1.5 1.5"],
  quickhack: ["M8 7l-5 5 5 5", "M16 7l5 5-5 5", "M13.5 5l-3 14"],
  mod: ["M8.5 3.5h7v4h4v9h-4v4h-7v-4h-4v-9h4z", "M10 10h4v4h-4z"],
  consumable: ["M10 2.5h4v3l2 3v12a1 1 0 0 1-1 1H9a1 1 0 0 1-1-1v-12l2-3z", "M8 11.5h8", "M8 16.5h8"],
  ammo: ["M5 10c0-3 1.3-5 2-5s2 2 2 5v10H5z", "M10 10c0-3 1.3-5 2-5s2 2 2 5v10h-4z", "M15 10c0-3 1.3-5 2-5s2 2 2 5v10h-4z"],
  material: ["M7 7h10v10H7z", "M10 10h4v4h-4z", "M9.5 3.5V7M14.5 3.5V7M9.5 17v3.5M14.5 17v3.5M3.5 9.5H7M3.5 14.5H7M17 9.5h3.5M17 14.5h3.5"],
  currency: ["M12 3a9 9 0 1 0 0 18 9 9 0 1 0 0-18z", "M15.2 9.2a3.6 3.6 0 1 0 0 5.6", "M7.8 11h5.4M7.8 13h5.4"],
  other: ["M4 7.5 12 3l8 4.5v9L12 21l-8-4.5z", "M4 7.5l8 4.5 8-4.5M12 12v9"]
};

// T024：物品库分页，每页卡片数可选，记在本机（localStorage，读写失败就用默认值）。
const PAGE_SIZES = [15, 30, 60];
const PAGE_SIZE_KEY = "gametool.library.pageSize";
const pending = new Map();
let requestId = 0;
let gameId = "cp2077";
let gameName = "游戏";
let slots = [];
let backups = [];
let selected = null;
let selectedBackup = null;
const collapsed = new Set(["auto"]);
const library = {
  kind: "",
  query: "",
  page: 1,
  size: readPageSize(),
  groups: [],
  total: 0,
  itemTotal: 0,
  kinds: [],
  counts: null,
  loading: false,
  token: 0,
  available: true,
  message: "",
  iconVersion: null
};
let searchTimer = 0;
let toastTimer = 0;
let dialogKeys = null;
let obtainWaiters = [];

function call(method, params) {
  const id = "r" + (++requestId);
  return new Promise((resolve, reject) => {
    pending.set(id, { resolve, reject });
    window.chrome.webview.postMessage({ id, method, params });
  });
}

window.chrome.webview.addEventListener("message", (event) => {
  const message = typeof event.data === "string" ? JSON.parse(event.data) : event.data;
  if (message.event) {
    document.dispatchEvent(new CustomEvent("gametool", { detail: message }));
    return;
  }
  const waiter = pending.get(message.id);
  if (!waiter)
    return;
  pending.delete(message.id);
  if (message.status === "ok")
    waiter.resolve(message.data);
  else
    waiter.reject(message.error || { message: "未知错误" });
});

// D024：存档版本不在白名单内（「不支持」）时，补一句游戏更新后的处理和更新渠道。
function withUnsupportedHint(text) {
  const base = String(text || t().unsupportedBadge).replace(/[。；;.\s]+$/, "");
  return base + (uiLang === "en-us" ? ". " : "。") + t().unsupportedHint;
}

function messageOf(error) {
  if (!error)
    return "";
  const text = error.message || String(error);
  return error.code === "unsupported_save" ? withUnsupportedHint(text) : text;
}

function showError(error) {
  errorEl.hidden = !error;
  errorEl.textContent = messageOf(error);
}

function showToast(text, bad) {
  clearTimeout(toastTimer);
  toastEl.textContent = text;
  toastEl.classList.toggle("bad", !!bad);
  toastEl.hidden = false;
  toastTimer = setTimeout(() => { toastEl.hidden = true; }, bad ? 6000 : 3500);
}

function formatTime(value) {
  return value ? new Date(value).toLocaleString() : "";
}

function formatNumber(value) {
  return Number(value).toLocaleString("zh-CN");
}

function element(tag, className, text) {
  const node = document.createElement(tag);
  if (className)
    node.className = className;
  if (text != null)
    node.textContent = text;
  return node;
}

function glyph(kind) {
  const svg = document.createElementNS("http://www.w3.org/2000/svg", "svg");
  svg.setAttribute("viewBox", "0 0 24 24");
  svg.setAttribute("aria-hidden", "true");
  for (const d of glyphs[kind] || glyphs.other) {
    const path = document.createElementNS("http://www.w3.org/2000/svg", "path");
    path.setAttribute("d", d);
    svg.append(path);
  }
  return svg;
}

const ICON_KEY = /^[A-Za-z0-9_]+(\.[A-Za-z0-9_]+)*$/;

/** 游戏图标地址（宿主按版本长缓存）；没有图标版本或记录名不合规时为 null。 */
function iconUrl(recordName, version) {
  if (!version || !recordName || !ICON_KEY.test(recordName))
    return null;
  return "/icon/" + encodeURIComponent(gameId) + "/" + recordName + ".png?g=f&v=" + encodeURIComponent(version);
}

/**
 * 在占位图形所在的容器里叠一张游戏图标：懒加载、异步解码；加载成功后容器加 has-art 隐去占位，
 * 失败（404 等）就移除图片，占位留着。
 */
function attachArt(holder, recordName) {
  const url = iconUrl(recordName, library.iconVersion);
  if (!url)
    return;
  const img = document.createElement("img");
  img.className = "art";
  img.alt = "";
  img.draggable = false;
  img.loading = "lazy";
  img.decoding = "async";
  img.addEventListener("load", () => holder.classList.add("has-art"), { once: true });
  img.addEventListener("error", () => img.remove(), { once: true });
  img.src = url;
  holder.append(img);
}

// ---------- 存档列表 / 备份区 ----------

function renderSlots() {
  savesPane.replaceChildren();
  for (const [id, label] of t().groups) {
    const members = slots.filter((slot) => (slot.group || "other") === id);
    if (members.length === 0)
      continue;
    const title = element("button", "group-title", (collapsed.has(id) ? "▸ " : "▾ ") + label + " (" + members.length + ")");
    title.type = "button";
    title.addEventListener("click", () => {
      if (collapsed.has(id))
        collapsed.delete(id);
      else
        collapsed.add(id);
      renderSlots();
    });
    savesPane.append(title);
    if (collapsed.has(id))
      continue;
    for (const slot of members)
      savesPane.append(slotButton(slot));
  }
}

function slotButton(slot) {
  const button = element("button", "slot" + (selected && selected.slot === slot.slot && !selectedBackup ? " active" : ""));
  button.type = "button";
  const title = element("strong", null, slot.name);
  if (slot.supported === false) {
    button.classList.add("unsupported");
    title.append(element("span", "badge warn", t().unsupportedBadge));
  }
  button.append(title, element("small", null, formatTime(slot.time)));
  button.addEventListener("click", () => selectSlot(slot).catch(showError));
  return button;
}

function renderBackups() {
  backupsPane.replaceChildren();
  if (backups.length === 0)
    backupsPane.append(element("p", "muted empty", t().emptyBackups));
  for (const backup of backups) {
    const row = element("button", "backup" + (selectedBackup && selectedBackup.id === backup.id ? " active" : ""));
    row.type = "button";
    row.append(
      element("strong", null, backup.slotName),
      element("small", null, formatTime(backup.timeUtc) + " · " + (backup.size / 1048576).toFixed(1) + " MB"));
    row.addEventListener("click", () => selectBackup(backup));
    backupsPane.append(row);
  }
}

async function refresh() {
  slots = await call("saves.list", { gameId });
  const still = slots.find((slot) => selected && slot.slot === selected.slot);
  selected = still || slots[0] || null;
  renderSlots();
  if (selected)
    await selectSlot(selected);
  else
    showNoSlot();
  backups = await call("backup.list", { gameId });
  renderBackups();
  booted = true;
}

async function refreshQuietly() {
  slots = await call("saves.list", { gameId });
  const still = slots.find((slot) => selected && slot.slot === selected.slot);
  if (still) {
    selected = still;
    slotTimeEl.textContent = formatTime(still.time);
  }
  renderSlots();
  backups = await call("backup.list", { gameId });
  renderBackups();
}

// ---------- 存档列表自动刷新（T030） ----------
// 宿主监视存档根目录，游戏存档后推送 saves.changed（宿主已去抖 1.5 秒）；窗口重新获得焦点时也刷新一次作保底。

let booted = false;
let diskRefreshRunning = null;
let diskRefreshAgain = false;
let libraryStale = false;
let focusRefreshTimer = 0;

/** 合并并串行执行：刷新进行中又来了事件，就在这次结束后再刷一次。主界面第一次加载完之前不做。 */
function refreshFromDisk() {
  if (!booted)
    return Promise.resolve();
  if (diskRefreshRunning) {
    diskRefreshAgain = true;
    return diskRefreshRunning;
  }
  diskRefreshRunning = (async () => {
    try {
      do {
        diskRefreshAgain = false;
        await syncWithDisk();
      } while (diskRefreshAgain);
    } finally {
      diskRefreshRunning = null;
    }
  })();
  return diskRefreshRunning;
}

/**
 * 静默刷新存档列表和备份区；选中存档的时间（或支持状态）变了，就重载它的物品状态（已拥有、持有数量）。
 * 确认框开着时只更新列表，物品库等确认框关闭后再重载，不打断用户的操作；正在看备份时右侧不动。
 */
async function syncWithDisk() {
  const before = selected ? { time: selected.time, supported: selected.supported } : null;
  await refreshQuietly();
  if (selectedBackup)
    return;
  const still = selected ? slots.find((slot) => slot.slot === selected.slot) : null;
  const changed = !still || !before || before.time !== still.time || before.supported !== still.supported;
  if (!changed)
    return;
  if (!confirmEl.hidden) {
    libraryStale = true;
    return;
  }
  await reloadSelected();
}

/**
 * 重载右侧：选中的存档还在，就原地重载物品库，保留分类、搜索词、页码和滚动位置；
 * 不在了（被删除或改名）就选第一个存档，一个都没有就显示「未选择存档」。
 */
async function reloadSelected() {
  libraryStale = false;
  const still = selected ? slots.find((slot) => slot.slot === selected.slot) : null;
  if (!still) {
    selected = null;
    if (slots.length > 0) {
      await selectSlot(slots[0]);
    } else {
      renderSlots();
      showNoSlot();
    }
    return;
  }
  selected = still;
  if (still.supported === false || libraryEl.hidden) {
    await selectSlot(still);
    return;
  }
  slotTimeEl.textContent = formatTime(still.time);
  await loadLibrary(false, false, true);
}

/** 焦点类的触发（窗口激活、页面重新可见）往往一次来好几个，300 ms 内合并。 */
function refreshSoon() {
  clearTimeout(focusRefreshTimer);
  focusRefreshTimer = setTimeout(() => { refreshFromDisk().catch(showError); }, 300);
}

document.addEventListener("gametool", (event) => {
  const data = event.detail.data || {};
  if (event.detail.event === "saves.changed" && (!data.gameId || data.gameId === gameId))
    refreshFromDisk().catch(showError);
  else if (event.detail.event === "window" && data.active === true)
    refreshSoon();
});
window.addEventListener("focus", refreshSoon);
document.addEventListener("visibilitychange", () => {
  if (document.visibilityState === "visible")
    refreshSoon();
});

function showNoSlot() {
  slotNameEl.textContent = t().noSlot;
  slotTimeEl.textContent = "";
  libraryEl.hidden = true;
  backupViewEl.hidden = true;
  backupAddEl.hidden = true;
}

async function selectSlot(slot) {
  const changed = !selected || selected.slot !== slot.slot;
  selected = slot;
  selectedBackup = null;
  showError(null);
  renderSlots();
  renderBackups();
  slotNameEl.textContent = slot.name;
  slotTimeEl.textContent = formatTime(slot.time);
  backupViewEl.hidden = true;
  backupAddEl.hidden = slot.supported === false;
  if (slot.supported === false) {
    libraryEl.hidden = true;
    showError({ message: withUnsupportedHint(slot.reason) });
    return;
  }
  libraryEl.hidden = false;
  if (changed) {
    // 换了存档：先清掉上一个存档的卡片，回到第 1 页；同一个存档（如恢复备份后刷新）保留页码。
    library.groups = [];
    gridEl.replaceChildren();
    pagerEl.hidden = true;
  }
  try {
    await loadLibrary(changed, true);
  } catch (error) {
    libraryEl.hidden = true;
    showError(error);
  }
}

async function selectBackup(backup) {
  selectedBackup = backup;
  renderSlots();
  renderBackups();
  libraryEl.hidden = true;
  backupAddEl.hidden = true;
  try {
    const detail = await call("backup.info", { gameId, backupId: backup.id });
    showError(null);
    renderBackupDetail(detail);
  } catch (error) {
    backupViewEl.hidden = true;
    showError(error);
  }
}

function renderBackupDetail(detail) {
  slotNameEl.textContent = t().backupTitlePrefix + detail.slotName;
  slotTimeEl.textContent = formatTime(detail.timeUtc);
  backupRowsEl.replaceChildren();
  for (const resource of detail.resources) {
    const differs = resource.currentQuantity != null && resource.backupQuantity !== resource.currentQuantity;
    const row = element("div", "backup-row" + (differs ? " changed" : ""));
    const current = resource.currentQuantity == null ? "—" : formatNumber(resource.currentQuantity);
    const backed = resource.backupQuantity == null ? "—" : formatNumber(resource.backupQuantity);
    row.append(element("span", null, resource.nameZh), element("span", "values", t().backupRowCompare(backed, current)));
    backupRowsEl.append(row);
  }
  backupActionsEl.replaceChildren();
  const restore = element("button", null, t().restoreBtn);
  restore.type = "button";
  restore.addEventListener("click", () => restoreBackup(selectedBackup));
  const remove = element("button", "danger", t().deleteBtn);
  remove.type = "button";
  remove.addEventListener("click", () => deleteBackup(selectedBackup));
  backupActionsEl.append(restore, remove);
  backupViewEl.hidden = false;
}

function showTab(name) {
  const saves = name === "saves";
  document.querySelector("#tab-saves").classList.toggle("active", saves);
  document.querySelector("#tab-backups").classList.toggle("active", !saves);
  savesPane.hidden = !saves;
  backupsPane.hidden = saves;
  if (saves && selected && selectedBackup)
    selectSlot(selected).catch(showError);
}

document.querySelector("#tab-saves").addEventListener("click", () => showTab("saves"));
document.querySelector("#tab-backups").addEventListener("click", () => showTab("backups"));

backupAddEl.addEventListener("click", async () => {
  if (!selected)
    return;
  try {
    await call("backup.add", { gameId, slot: selected.slot });
    backups = await call("backup.list", { gameId });
    renderBackups();
    showToast(t().toastBackupAdded(selected.name));
    showError(null);
  } catch (error) {
    showError(error);
  }
});

async function restoreBackup(backup) {
  const choice = await ask(t().restoreDialogTitle, t().restoreDialogText(backup.slotName), [
    { label: t().restoreConfirm, value: "ok" },
    { label: t().cancel, value: "cancel", ghost: true }
  ]);
  if (choice !== "ok")
    return;
  try {
    await call("backup.restore", { gameId, backupId: backup.id });
    showToast(t().toastRestored(backup.slotName));
    selectedBackup = null;
    await refresh();
    showTab("saves");
    showError(null);
  } catch (error) {
    showError(error);
  }
}

async function deleteBackup(backup) {
  const choice = await ask(t().deleteDialogTitle, t().deleteDialogText(backup.slotName, formatTime(backup.timeUtc)), [
    { label: t().deleteConfirm, value: "ok", danger: true },
    { label: t().cancel, value: "cancel", ghost: true }
  ]);
  if (choice !== "ok")
    return;
  try {
    await call("backup.delete", { gameId, backupId: backup.id });
    backups = await call("backup.list", { gameId });
    selectedBackup = null;
    backupViewEl.hidden = true;
    slotNameEl.textContent = t().tabBackups;
    slotTimeEl.textContent = "";
    renderBackups();
  } catch (error) {
    showError(error);
  }
}

// ---------- 物品库 ----------
// T023：目录按「同一分类、同一中文名」合并成卡片（library.catalog），一张卡里的多个品阶 / 款式在确认框里切换。

function kindLabel(id) {
  const kind = library.kinds.find((item) => item.id === id);
  return kind ? kind.label : id;
}

function subtitle(item) {
  const kind = kindLabel(item.kind);
  return item.typeLabel && item.typeLabel !== kind ? kind + " · " + item.typeLabel : kind;
}

function shortRecord(name) {
  return String(name || "").replace(/^Items\./, "");
}

/** 卡片 / 选项的颜色：标志性物品用独立的标志性色，其余按品质，没有固定品质（如 Quality.Random）为中性。 */
function qualityClass(item) {
  if (item.iconic)
    return "q-iconic";
  return "q-" + (item.quality ? item.quality.toLowerCase() : "none");
}

/** 品质名加 Plus 档：传说、史诗+、传说++。 */
function qualityText(item) {
  const name = item.quality ? t().qualityNames[item.quality] : "";
  return name ? name + "+".repeat(Math.max(0, Math.min(2, item.plus || 0))) : "";
}

function stateOf(item) {
  if (item.stackable)
    return item.held != null ? t().stateHeld(formatNumber(item.held)) : t().stateNotHeld;
  return item.owned ? t().stateOwned : "";
}

function defaultVariant(group) {
  return group.variants[group.defaultIndex] || group.variants[0];
}

/** 合并卡片的数量说明：7 个品阶 / 3 种款式。 */
function variantCount(group) {
  return t().variantCount(group.variants.length, group.variantNoun);
}

function groupState(group) {
  if (group.variants.length === 1)
    return stateOf(group.variants[0]);
  const have = group.variants.filter((item) => (item.stackable ? item.held != null : item.owned)).length;
  if (have === 0)
    return group.stackable ? t().stateNotHeld : "";
  return group.stackable ? t().stateGroupHeld(have, group.variants.length) : t().stateGroupOwned(have, group.variants.length);
}

function groupHasTarget(group, target) {
  return group.variants.some((item) => item.target === target);
}

function readPageSize() {
  try {
    const value = Number(window.localStorage.getItem(PAGE_SIZE_KEY));
    return PAGE_SIZES.includes(value) ? value : 30;
  } catch (error) {
    return 30;
  }
}

function savePageSize(value) {
  try {
    window.localStorage.setItem(PAGE_SIZE_KEY, String(value));
  } catch (error) {
    // 存不下就只在这次运行里生效。
  }
}

function pageCount() {
  return Math.max(1, Math.ceil(library.total / library.size));
}

/** 搜索时当前分类没有命中、别的分类有：返回第一个有命中的分类（按标签顺序），否则 null。 */
function betterKind() {
  if (!library.query || !library.counts || (library.counts[library.kind] || 0) > 0)
    return null;
  const hit = library.kinds.find((kind) => (library.counts[kind.id] || 0) > 0);
  return hit ? hit.id : null;
}

/**
 * 加载当前分类、搜索词下的第 library.page 页（T024 分页）。reset 为 true 时回到第 1 页。
 * 没有「全部」标签：还不知道分类（首次加载）或选中的分类不在这个存档里时，宿主按 kindFallback 改用第一个分类并返回实际分类。
 * autoKind 为 true（搜索词变化、换存档）时，当前分类没有命中而别的分类有，就自动切到第一个有命中的分类。
 * keepScroll 为 true（存档在磁盘上变了的静默重载，T030）时结果区保持原来的滚动位置，否则回到顶部。
 */
async function loadLibrary(reset, autoKind, keepScroll) {
  if (!selected || selected.supported === false)
    return;
  if (reset)
    library.page = 1;
  const token = ++library.token;
  const slot = selected.slot;
  library.loading = true;
  paintMeta();
  let page;
  try {
    page = await call("library.catalog", {
      gameId,
      slot,
      kind: library.kind || null,
      kindFallback: true,
      query: library.query || null,
      skip: (library.page - 1) * library.size,
      take: library.size
    });
  } finally {
    if (token === library.token)
      library.loading = false;
  }
  if (token !== library.token || !selected || selected.slot !== slot)
    return;
  library.available = page.namesAvailable;
  library.message = page.message || "";
  library.iconVersion = page.iconVersion || null;
  library.kinds = page.kinds || [];
  library.kind = page.kind || "";
  library.counts = page.kindCounts || null;
  const better = autoKind ? betterKind() : null;
  if (better) {
    library.kind = better;
    await loadLibrary(true, false);
    return;
  }
  library.total = page.total;
  library.itemTotal = page.itemTotal;
  if (library.page > pageCount()) {
    // 页码超出（比如物品变少了）：退到最后一页。
    library.page = pageCount();
    await loadLibrary(false, false);
    return;
  }
  library.groups = page.groups;
  const scrollTop = keepScroll ? resultsEl.scrollTop : 0;
  gridEl.replaceChildren(...page.groups.map((group) => cardFor(group)));
  resultsEl.scrollTop = scrollTop;
  renderChips();
  renderPager();
  paintMeta();
}

/** 翻到第 page 页（结果区回到顶部在 loadLibrary 里做）。越界或就是当前页时什么也不做。 */
function goToPage(page) {
  if (!Number.isInteger(page) || page < 1 || page > pageCount() || page === library.page)
    return Promise.resolve();
  library.page = page;
  return loadLibrary(false, false);
}

function paintMeta() {
  if (!library.available) {
    metaEl.textContent = "";
    emptyEl.hidden = false;
    emptyEl.textContent = library.message || t().libUnavailable;
    return;
  }
  const empty = !library.loading && library.groups.length === 0;
  emptyEl.hidden = !empty;
  if (!library.query)
    emptyEl.textContent = t().libEmptyKind;
  else if (library.counts && Object.values(library.counts).some((count) => count > 0))
    emptyEl.textContent = t().libEmptyOtherKind(library.query);
  else
    emptyEl.textContent = t().libEmptyQuery(library.query);
  if (library.loading && library.groups.length === 0) {
    metaEl.textContent = t().libLoading;
    return;
  }
  metaEl.textContent = t().libMeta(library.total, library.itemTotal, library.page, pageCount());
}

/** 分类标签：没有「全部」（D019）。有搜索词时每个标签后面显示这个分类的命中卡片数，没有命中的标签变淡。 */
function renderChips() {
  chipsEl.replaceChildren();
  if (!library.available)
    return;
  for (const kind of library.kinds) {
    const count = library.counts ? library.counts[kind.id] || 0 : null;
    const chip = element("button", "chip" + (library.kind === kind.id ? " active" : "") + (count === 0 ? " none" : ""), kind.label);
    chip.type = "button";
    chip.dataset.kind = kind.id;
    if (count != null) {
      chip.dataset.count = String(count);
      chip.append(element("span", "chip-count", formatNumber(count)));
    }
    chip.addEventListener("click", () => {
      if (library.kind === kind.id)
        return;
      library.kind = kind.id;
      renderChips();
      loadLibrary(true, false).catch(showError);
    });
    chipsEl.append(chip);
  }
}

/** 页码：首页、末页、当前页前后各两页，中间跳过的用 null（显示为省略号），如 1 … 4 5 [6] 7 8 … 35。 */
function pageNumbers(current, count) {
  const items = [1];
  const low = Math.max(2, current - 2);
  const high = Math.min(count - 1, current + 2);
  if (low > 3)
    items.push(null);
  else if (low === 3)
    items.push(2);
  for (let page = low; page <= high; page++)
    items.push(page);
  if (high < count - 2)
    items.push(null);
  else if (high === count - 2)
    items.push(count - 1);
  if (count > 1)
    items.push(count);
  return items;
}

function pagerButton(text, page, disabled, extra) {
  const button = element("button", "pg" + (extra ? " " + extra : ""), text);
  button.type = "button";
  button.disabled = disabled;
  button.dataset.page = String(page);
  button.addEventListener("click", () => { goToPage(page).catch(showError); });
  return button;
}

/** 结果下方的翻页条：上一页、页码、下一页、跳页。只有一页时隐藏。 */
function renderPager() {
  const pages = pageCount();
  pagerEl.replaceChildren();
  pagerEl.hidden = !library.available || pages <= 1;
  if (pagerEl.hidden)
    return;
  pagerEl.append(pagerButton(t().prevPage, library.page - 1, library.page <= 1, "pg-step"));
  for (const page of pageNumbers(library.page, pages)) {
    if (page == null) {
      pagerEl.append(element("span", "pg-gap", "…"));
      continue;
    }
    const current = page === library.page;
    const button = pagerButton(String(page), page, false, current ? "active" : "");
    if (current)
      button.setAttribute("aria-current", "page");
    pagerEl.append(button);
  }
  pagerEl.append(pagerButton(t().nextPage, library.page + 1, library.page >= pages, "pg-step"));
  const jump = element("label", "pg-jump", t().jumpPrefix);
  const input = document.createElement("input");
  input.type = "number";
  input.id = "page-jump";
  input.min = "1";
  input.max = String(pages);
  input.step = "1";
  input.addEventListener("keydown", (event) => {
    if (event.key !== "Enter")
      return;
    const value = Number(input.value);
    if (Number.isInteger(value))
      goToPage(Math.min(pages, Math.max(1, value))).catch(showError);
  });
  jump.append(input, document.createTextNode(t().jumpSuffix));
  pagerEl.append(jump);
}

/** 一张卡片：名字、类型、品阶数或款式数、最高品阶的颜色，图标用默认（最高品阶）那一条的。 */
function cardFor(group) {
  const chosen = defaultVariant(group);
  const card = element("article", "item " + qualityClass(group));
  card.dataset.key = group.key;
  card.dataset.target = chosen.target;
  card.dataset.targets = group.variants.map((item) => item.target).join(" ");
  const tips = [];
  if (chosen.statBadges && chosen.statBadges.length > 0) tips.push(t().cardTipStats + "\n" + chosen.statBadges.join(" · "));
  if (chosen.effectDesc) tips.push(t().cardTipEffect + "\n" + chosen.effectDesc);
  if (chosen.description) tips.push(t().cardTipDesc + "\n" + chosen.description);
  if (tips.length > 0) card.title = tips.join("\n\n");
  const icon = element("div", "icon");
  icon.dataset.iconKey = chosen.recordName;
  icon.dataset.kind = group.kind;
  icon.append(glyph(group.kind));
  attachArt(icon, chosen.recordName);
  const tier = qualityText(group);
  if (tier)
    icon.append(element("span", "tag quality", tier));
  if (group.iconic)
    icon.append(element("span", "tag iconic", t().iconicBadge));
  const name = element("h3", "name", group.label);
  name.title = group.label;
  const sub = element("p", "sub", subtitle(group));
  let badgesRow = null;
  if (chosen.statBadges && chosen.statBadges.length > 0) {
    badgesRow = element("div", "card-badges");
    for (const b of chosen.statBadges) {
      badgesRow.append(element("span", "badge-mini", b));
    }
  }
  let rec;
  if (group.variants.length > 1) {
    rec = element("p", "rec count", variantCount(group));
    rec.title = group.variants.map((item) => item.variantLabel + "：" + item.recordName).join("\n");
  } else {
    rec = element("p", "rec", shortRecord(chosen.recordName));
    rec.title = chosen.recordName;
  }
  const foot = element("div", "foot");
  const heldAny = group.variants.some((item) => item.held != null);
  const state = element("span", "state" + (heldAny ? " held" : ""), groupState(group));
  foot.append(state);
  card.setAttribute("role", "button");
  card.setAttribute("tabindex", "0");
  card.addEventListener("click", () => openObtain(group));
  card.addEventListener("keydown", (e) => {
    if (e.key === "Enter" || e.key === " ") {
      e.preventDefault();
      openObtain(group);
    }
  });
  if (badgesRow)
    card.append(icon, name, sub, badgesRow, rec, foot);
  else
    card.append(icon, name, sub, rec, foot);
  return card;
}

function cardOf(target) {
  return [...gridEl.children].find((node) => (node.dataset.targets || "").split(" ").includes(target)) || null;
}

function replaceCard(group) {
  for (const card of gridEl.children) {
    if (card.dataset.key === group.key) {
      card.replaceWith(cardFor(group));
      return;
    }
  }
}

searchEl.addEventListener("input", () => {
  clearTimeout(searchTimer);
  searchTimer = setTimeout(() => {
    const query = searchEl.value.trim();
    if (query === library.query)
      return;
    library.query = query;
    loadLibrary(true, true).catch(showError);
  }, 200);
});

pageSizeEl.value = String(library.size);
pageSizeEl.addEventListener("change", () => {
  const value = Number(pageSizeEl.value);
  if (!PAGE_SIZES.includes(value) || value === library.size)
    return;
  library.size = value;
  savePageSize(value);
  loadLibrary(true, false).catch(showError);
});

// ---------- 确认框 ----------

function openDialog(title, text) {
  confirmTitle.textContent = title || "";
  confirmTitle.hidden = !title;
  confirmText.textContent = text;
  confirmPreview.hidden = true;
  confirmPreview.replaceChildren();
  confirmBody.replaceChildren();
  confirmError.hidden = true;
  confirmActions.replaceChildren();
  confirmEl.hidden = false;
}

function closeDialog() {
  confirmEl.hidden = true;
  if (dialogKeys) {
    document.removeEventListener("keydown", dialogKeys);
    dialogKeys = null;
  }
  // T030：确认框开着时存档在磁盘上变了，物品库推迟到现在重载（放到下一轮，让「获得」的结果先画上去）。
  if (libraryStale) {
    setTimeout(() => {
      if (libraryStale && confirmEl.hidden && !selectedBackup)
        reloadSelected().catch(showError);
    }, 0);
  }
}

function bindKeys(onEnter, onEscape) {
  dialogKeys = (event) => {
    if (event.key === "Escape") {
      event.preventDefault();
      onEscape();
    } else if (event.key === "Enter" && onEnter) {
      event.preventDefault();
      onEnter();
    }
  };
  document.addEventListener("keydown", dialogKeys);
}

function ask(title, text, buttons) {
  openDialog(title, text);
  return new Promise((resolve) => {
    const finish = (value) => {
      closeDialog();
      resolve(value);
    };
    for (const button of buttons) {
      const node = element("button", button.ghost ? "ghost" : button.danger ? "danger" : null, button.label);
      node.type = "button";
      node.addEventListener("click", () => finish(button.value));
      confirmActions.append(node);
    }
    bindKeys(null, () => finish("cancel"));
  });
}

function settleObtain(outcome) {
  const waiters = obtainWaiters;
  obtainWaiters = [];
  for (const waiter of waiters)
    waiter(outcome);
}

function nextObtainOutcome() {
  return new Promise((resolve) => obtainWaiters.push(resolve));
}

/** 选项多时用下拉框，少时用分段按钮。 */
const SEGMENT_LIMIT = 6;

/**
 * 获得确认框：唯一的输入点。合并卡片在这里选具体的品阶 / 款式（默认最高品阶），图标、持有情况随选中的条目切换；
 * 堆叠物品多一个数量框。返回对象的 confirm() 与点「获得」按钮相同，select(index) 与在选项里点选相同。
 */
function openObtain(group, initialIndex) {
  const slot = selected;
  if (!slot)
    return null;
  const variants = group.variants;
  let index = Number.isInteger(initialIndex) && initialIndex >= 0 && initialIndex < variants.length ? initialIndex : group.defaultIndex || 0;
  openDialog(t().obtainTitle, "");
  confirmPreview.hidden = false;

  const picker = element("div", "variants");
  let select = null;
  const segments = [];
  if (variants.length > 1) {
    const caption = element("p", "variants-caption", t().obtainPickCaption(group.variantNoun, variants.length));
    picker.append(caption);
    if (variants.length <= SEGMENT_LIMIT) {
      const row = element("div", "segmented");
      row.id = "obtain-variants";
      row.setAttribute("role", "radiogroup");
      variants.forEach((item, position) => {
        const segment = element("button", "seg " + qualityClass(item), item.variantLabel || shortRecord(item.recordName));
        segment.type = "button";
        segment.dataset.target = item.target;
        segment.setAttribute("role", "radio");
        segment.title = item.recordName + (stateOf(item) ? " · " + stateOf(item) : "");
        segment.addEventListener("click", () => choose(position));
        segments.push(segment);
        row.append(segment);
      });
      picker.append(row);
    } else {
      select = document.createElement("select");
      select.id = "obtain-variant";
      variants.forEach((item, position) => {
        const option = document.createElement("option");
        option.value = String(position);
        option.textContent = (item.variantLabel || shortRecord(item.recordName)) + (stateOf(item) ? "（" + stateOf(item) + "）" : "");
        option.dataset.target = item.target;
        select.append(option);
      });
      select.addEventListener("change", () => choose(Number(select.value)));
      picker.append(select);
    }
    confirmBody.append(picker);
  }

  const detailsPanel = element("div", "item-details");
  confirmBody.append(detailsPanel);

  const detail = element("div", "variant-detail");
  confirmBody.append(detail);

  const run = element("button", null, t().obtainBtn);
  run.type = "button";
  const cancel = element("button", "ghost", t().cancel);
  cancel.type = "button";
  confirmActions.append(cancel, run);
  let busy = false;
  let input = null;
  let after = null;
  let limit = 1;
  let selectedDest = null;

  function current() {
    return variants[index];
  }

  function nameOf(item) {
    return variants.length > 1 && item.variantLabel ? group.label + " · " + item.variantLabel : group.label;
  }

  function quantity() {
    if (!input)
      return null;
    const text = input.value.trim();
    return /^\d+$/.test(text) ? Number(text) : NaN;
  }

  function paintAfter() {
    const item = current();
    if (!after) {
      run.disabled = busy;
      return;
    }
    const value = quantity();
    if (item.stackable) {
      after.textContent = t().qtyNote(item.held, value, limit);
    } else {
      after.textContent = t().gearQtyNote(value, limit);
    }
    run.disabled = busy || !(Number.isSafeInteger(value) && value >= 1 && value <= limit);
  }

  /** 按选中的条目重画预览（图标、颜色、记录名、持有情况）和数量区。 */
  function paintVariant() {
    const item = current();
    confirmText.textContent = t().obtainSummary(slot.name, nameOf(item));
    confirmPreview.className = "preview " + qualityClass(item);
    confirmPreview.replaceChildren();
    const thumb = element("div", "thumb");
    thumb.dataset.iconKey = item.recordName;
    thumb.append(glyph(item.kind));
    attachArt(thumb, item.recordName);
    const info = element("div");
    const tier = qualityText(item);
    const status = stateOf(item);
    const facts = [subtitle(item), tier, shortRecord(item.recordName), status].filter((part) => !!part);
    info.append(element("strong", null, nameOf(item)), element("small", null, facts.join(" · ")));
    if (item.statBadges && item.statBadges.length > 0) {
      const badgeRow = element("div", "preview-badges");
      for (const badge of item.statBadges) {
        badgeRow.append(element("span", "stat-badge", badge));
      }
      info.append(badgeRow);
    }
    confirmPreview.append(thumb, info);
    segments.forEach((segment, position) => {
      segment.classList.toggle("active", position === index);
      segment.setAttribute("aria-checked", position === index ? "true" : "false");
    });
    if (select)
      select.value = String(index);

    detailsPanel.replaceChildren();
    if (item.effectDesc || item.description) {
      if (item.effectDesc) {
        const effectBox = element("div", "detail-effect");
        const title = element("span", "effect-title", t().effectHeading);
        const text = element("p", "effect-text", item.effectDesc);
        effectBox.append(title, text);
        detailsPanel.append(effectBox);
      }
      if (item.description) {
        const descBox = element("p", "detail-desc", item.description);
        detailsPanel.append(descBox);
      }
      detailsPanel.hidden = false;
    } else {
      detailsPanel.hidden = true;
    }

    detail.replaceChildren();
    input = null;
    after = null;
    const held = item.held || 0;
    const isMultiGear = !item.stackable && item.kind === "mod";
    limit = item.stackable ? Math.max(0, (item.maxQuantity || 0) - held) : (isMultiGear ? 10 : 1);
    if (item.stackable || isMultiGear) {
      const box = element("div", "qty");
      const row = element("div", "qty-row");
      const label = element("label", null, t().qtyLabel);
      input = document.createElement("input");
      input.type = "number";
      input.id = "obtain-quantity";
      input.min = "1";
      input.max = String(limit);
      input.step = "1";
      input.value = limit >= 1 ? "1" : "0";
      label.htmlFor = input.id;
      row.append(label, input);
      const presets = item.stackable
        ? (item.kind === "currency" ? [1000, 10000, 100000, 1000000] : [1, 10, 100, 1000])
        : [1, 2, 4, 10];
      for (const value of presets) {
        if (value > limit)
          continue;
        const preset = element("button", "preset", "+" + formatNumber(value));
        preset.type = "button";
        preset.addEventListener("click", () => {
          input.value = String(value);
          paintAfter();
          input.focus();
        });
        row.append(preset);
      }
      after = element("p", "qty-note");
      box.append(row, after);
      detail.append(box);
      input.addEventListener("input", paintAfter);
    } else if (item.owned) {
      detail.append(element("p", "dialog-note", t().ownedNote));
    }

    if (!item.stackable) {
      const destBox = element("div", "dest-box");
      const caption = element("p", "dest-caption", t().destLabel);
      const row = element("div", "segmented", null);
      row.id = "obtain-destination";
      row.setAttribute("role", "radiogroup");

      const isStash = selectedDest != null ? selectedDest === "stash" : Boolean(item.iconic);

      const btnBackpack = element("button", "seg" + (!isStash ? " active" : ""), t().destBackpack);
      btnBackpack.type = "button";
      btnBackpack.setAttribute("role", "radio");
      btnBackpack.setAttribute("aria-checked", !isStash ? "true" : "false");

      const btnStash = element("button", "seg" + (isStash ? " active" : ""), t().destStash);
      btnStash.type = "button";
      btnStash.setAttribute("role", "radio");
      btnStash.setAttribute("aria-checked", isStash ? "true" : "false");

      btnBackpack.addEventListener("click", () => {
        selectedDest = "backpack";
        paintVariant();
      });

      btnStash.addEventListener("click", () => {
        selectedDest = "stash";
        paintVariant();
      });

      row.append(btnBackpack, btnStash);
      destBox.append(caption, row);

      if (item.iconic) {
        const tipText = isStash ? t().destIconicTip : t().destIconicBackpackWarn;
        destBox.append(element("p", "dest-tip", tipText));
      }

      detail.append(destBox);
    }
    paintAfter();
  }

  function choose(position) {
    if (busy || position === index || position < 0 || position >= variants.length)
      return;
    index = position;
    confirmError.hidden = true;
    run.textContent = t().obtainBtn;
    paintVariant();
  }

  async function confirm() {
    if (busy)
      return { ok: false, error: { message: "正在写入" } };
    const item = current();
    const value = quantity();
    if (input && !(Number.isSafeInteger(value) && value >= 1 && value <= limit)) {
      paintAfter();
      return { ok: false, error: { message: "数量无效" } };
    }
    busy = true;
    run.disabled = true;
    cancel.disabled = true;
    if (select)
      select.disabled = true;
    segments.forEach((segment) => { segment.disabled = true; });
    run.textContent = t().obtainWriting;
    confirmError.hidden = true;
    let outcome;
    const isStash = !item.stackable && (selectedDest != null ? selectedDest === "stash" : Boolean(item.iconic));
    try {
      const result = await call("library.obtain", { gameId, slot: slot.slot, target: item.target, quantity: input ? value : null, toStash: isStash });
      outcome = { ok: true, result, target: item.target };
      closeDialog();
      applyObtained(group, item, result);
    } catch (error) {
      outcome = { ok: false, error, target: item.target };
      confirmError.textContent = t().obtainNotWritten(messageOf(error));
      confirmError.hidden = false;
      run.textContent = t().obtainRetry;
      busy = false;
      cancel.disabled = false;
      if (select)
        select.disabled = false;
      segments.forEach((segment) => { segment.disabled = false; });
      paintAfter();
    }
    settleObtain(outcome);
    return outcome;
  }

  run.addEventListener("click", () => { confirm(); });
  cancel.addEventListener("click", () => {
    if (!busy) {
      closeDialog();
      settleObtain({ ok: false, cancelled: true });
    }
  });
  bindKeys(() => { if (!run.disabled) confirm(); }, () => cancel.click());
  paintVariant();
  if (input) {
    input.focus();
    input.select();
  } else {
    run.focus();
  }
  return { confirm, select: choose, current };
}

function applyObtained(group, item, result) {
  if (result.stackable)
    item.held = result.after;
  item.owned = true;
  replaceCard(group);
  const amount = result.stackable ? " ×" + formatNumber(result.quantity) : (result.quantity > 1 ? " ×" + result.quantity : "");
  const label = group.variants.length > 1 && item.variantLabel ? group.label + " · " + item.variantLabel : group.label;
  showToast(t().toastObtained(label, amount, result.toStash));
  refreshQuietly().catch(showError);
}

// ---------- 外观 ----------

const pixelPng = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAAC0lEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";
const skinPanel = document.querySelector("#skin-panel");
const skinOpacity = document.querySelector("#skin-opacity");
const skinOpacityLabel = document.querySelector("#skin-opacity-label");
const backdrop = document.querySelector("#backdrop");

function applyTheme(theme) {
  const root = document.documentElement;
  root.style.setProperty("--accent", theme.accent);
  root.style.setProperty("--accent-text", theme.accentText);
}

function applyOverlay(value) {
  document.documentElement.style.setProperty("--overlay-opacity", String(value));
  skinOpacity.value = String(Math.round(value * 100));
  skinOpacityLabel.textContent = Math.round(value * 100) + "%";
}

function applySkin(skin) {
  applyOverlay(skin.overlayOpacity);
  if (skin.hasImage)
    backdrop.style.backgroundImage = "url(\"/skin/" + encodeURIComponent(gameId) + "/background?v=" + encodeURIComponent(skin.imageVersion || "") + "\")";
  else
    backdrop.style.backgroundImage = "";
}

function importSkinFile(file) {
  if (!file)
    return;
  if (file.size > 20 * 1024 * 1024) {
    showError({ message: "图片超过 20 MB。" });
    return;
  }
  const reader = new FileReader();
  reader.onload = () => {
    const text = String(reader.result || "");
    const marker = text.indexOf(",");
    const data = marker >= 0 ? text.slice(marker + 1) : text;
    call("skin.import", { gameId, fileName: file.name || "image.png", dataBase64: data })
      .then((skin) => {
        applySkin(skin);
        showError(null);
      })
      .catch(showError);
  };
  reader.readAsDataURL(file);
}

document.querySelector("#skin-open").addEventListener("click", () => {
  skinPanel.hidden = !skinPanel.hidden;
});
document.querySelector("#skin-file").addEventListener("change", (event) => {
  importSkinFile(event.target.files && event.target.files[0]);
  event.target.value = "";
});
document.querySelector("#skin-clear").addEventListener("click", () => {
  call("skin.clear", { gameId }).then((skin) => {
    applySkin(skin);
    showError(null);
  }).catch(showError);
});
skinOpacity.addEventListener("input", () => {
  applyOverlay(Number(skinOpacity.value) / 100);
});
skinOpacity.addEventListener("change", () => {
  call("skin.setOverlay", { gameId, value: Number(skinOpacity.value) / 100 }).catch(showError);
});
const skinDrop = document.querySelector("#skin-drop");
skinDrop.addEventListener("dragover", (event) => event.preventDefault());
skinDrop.addEventListener("drop", (event) => {
  event.preventDefault();
  importSkinFile(event.dataTransfer && event.dataTransfer.files[0]);
});

// ---------- 说明（D022：快速上手 / 常见问题 / 关于） ----------
// 内容是 index.html 里的静态文字；这里只切换页签、填版本号，以及第一次进入主界面时自动弹出一次「快速上手」。

const helpEl = document.querySelector("#help");
const helpOpenEl = document.querySelector("#help-open");
const helpCloseEl = document.querySelector("#help-close");
const helpBodyEl = document.querySelector("#help-body");
let helpVersionEl = document.querySelector("#help-version");
let currentAppVersion = "";
const helpTabs = [...document.querySelectorAll("[data-help-tab]")];
const helpPanes = [...document.querySelectorAll("[data-help-pane]")];
let helpTab = "start";

const HELP_PANES_HTML = {
  "zh-cn": {
    start: document.querySelector('[data-help-pane="start"]').innerHTML,
    faq: document.querySelector('[data-help-pane="faq"]').innerHTML,
    about: document.querySelector('[data-help-pane="about"]').innerHTML
  },
  "zh-tw": {
    start: `
      <ol class="help-steps">
        <li><strong>設定目錄</strong>：第一次開啟時選擇「存檔目錄」和「遊戲目錄」，兩項都通過後點「開始使用」。以後可點右上角齒輪修改。</li>
        <li><strong>選擇存檔</strong>：在左側「目前存檔」裡點選要修改的存檔。</li>
        <li><strong>獲得物品</strong>：在搜尋框輸入物品名，或點分類標籤；在卡片上點「獲得」，在確認框裡選品階或款式（可疊放物品填數量）後確認。</li>
        <li><strong>回遊戲讀檔</strong>：回到遊戲讀取這個存檔，新物品就在背包裡。</li>
      </ol>
      <p class="help-tip">想留後路？先點「加入備份區」，出問題可在「備份區」一鍵還原。</p>
      <div class="help-author">
        <span>覺得好用，歡迎關注作者 B 站「魔法苦茶籽」獲取更新。</span>
        <button class="ghost small" type="button" data-author-open>B 站主頁</button>
      </div>`,
    faq: `
      <dl class="help-faq">
        <dt>獲得後遊戲裡看不到？</dt>
        <dd>需要在遊戲裡重新讀取這個存檔。另外，遊戲執行時如果又存到同一個存檔位，會覆蓋掉修改，建議在主選單讀檔前使用。</dd>
        <dt>開了 Steam / GOG 雲端存檔會有影響嗎？</dt>
        <dd>修改後啟動遊戲時，如果平台彈出雲端存檔衝突提示，請選擇保留<strong>本機</strong>存檔，否則修改會被雲端的舊存檔覆蓋。不確定時，先把存檔加入備份區。</dd>
        <dt>物品庫是空的或提示名字表不可用？</dt>
        <dd>需要安裝官方免費 DLC REDmod（Steam / GOG / Epic 均可獲取），並在設定裡選對遊戲目錄。</dd>
        <dt>沒買自由幻局能用嗎？</dt>
        <dd>可以。工具會偵測遊戲目錄：沒有自由幻局時，物品庫只列本體物品，資料片的物品不顯示、也不會生成。</dd>
        <dt>存檔顯示「不支援」？</dt>
        <dd>分兩種情況：
          <ul class="help-list">
            <li><strong>舊版本的存檔</strong>：在遊戲裡讀取後重新存檔即可。</li>
            <li><strong>遊戲更新後的新版本存檔</strong>：工具需要適配新版本，暫時不能修改（這是保護存檔的安全設計）。請關注作者 B 站獲取更新。</li>
          </ul>
        </dd>
        <dt>怎麼備份和還原？</dt>
        <dd>選取存檔後點「加入備份區」；在「備份區」頁籤裡可以查看、還原或刪除備份。獲得物品不會自動備份。</dd>
        <dt>為什麼獲得不朽武器推薦選「放入儲藏室」？升階到 5++ 的機制是什麼？</dt>
        <dd>
          這是由於《賽博朋克 2077》2.0+ 官方腳本的<strong>等級校準機制</strong>（<code>RescaleStashedIconicsToPlayerLevel</code>）：
          <ul class="help-list">
            <li>遊戲機制規定：不朽武器升階到 5++（傳說++）需要角色達到 51 級以上並擁有《自由幻局》，且武器的掉落等級必須經過遊戲引擎校準。</li>
            <li>直接放入背包的注入武器缺少掉落時的時間戳校準，在工坊升到 5 階後會無法繼續升級到 5++。</li>
            <li><strong>解決方案</strong>：選擇<strong>「放入儲藏室」</strong>，修改後進遊戲從載具後備箱或公寓儲藏點將武器取出，遊戲原生腳本就會自動觸發等級校準，使其完美解鎖 5++ 升階！</li>
          </ul>
        </dd>
        <dt>會不會把存檔改壞？</dt>
        <dd>每次寫入前都會逐位元組校驗：除了新增或修改的物品，其他內容只要有任何變化就放棄寫入，原存檔保持不變。</dd>
      </dl>`,
    about: `
      <div class="help-about">
        <h3>遊戲工具 · Cyberpunk 2077 物品庫</h3>
        <span id="help-version" class="help-version">v</span>
      </div>
      <h4>作者</h4>
      <div class="help-author">
        <span>作者：<strong>魔法苦茶籽</strong></span>
        <button class="ghost small" type="button" data-author-open>B 站主頁</button>
      </div>
      <p class="help-note">本工具免費。遊戲更新後需要等待工具適配，最新版本與更新請關注作者 B 站動態。</p>
      <h4>執行環境</h4>
      <ul id="help-env" class="help-list">
        <li>Windows 10/11 64 位元</li>
        <li>Microsoft Edge WebView2 執行階段</li>
        <li>遊戲 2.31 / 2.5 版本存檔（有沒有自由幻局都可以）</li>
        <li>官方免費 DLC REDmod</li>
      </ul>
      <h4>聲明</h4>
      <ul class="help-list">
        <li>本工具只修改存檔檔案，不注入、不修改遊戲處理程序，也不改動遊戲目錄。</li>
        <li>物品名稱、分類與圖示均在執行時從你本機的遊戲檔案讀取，本工具不附帶任何遊戲素材。</li>
        <li>非官方工具，與 CD PROJEKT RED 無關。僅供單機遊戲使用。</li>
        <li>修改存檔有風險，建議重要存檔先加入備份區。</li>
      </ul>`
  },
  "en-us": {
    start: `
      <ol class="help-steps">
        <li><strong>Set Paths</strong>: On first launch, choose your Save Directory and Game Directory, then click Get Started. You can change them anytime via the top-right gear icon.</li>
        <li><strong>Select Save</strong>: Click a save slot under Saves on the left panel.</li>
        <li><strong>Get Items</strong>: Search by item name or click a category tab; click an item card, choose tier/style (or quantity for stackables), and click Get.</li>
        <li><strong>Reload in Game</strong>: Load the modified save in Cyberpunk 2077 to find the new items in your inventory.</li>
      </ol>
      <p class="help-tip">Want a safety net? Click "Add Backup" before editing so you can restore with one click anytime.</p>
      <div class="help-author">
        <span>Follow author "魔法苦茶籽" on Bilibili for updates.</span>
        <button class="ghost small" type="button" data-author-open>Bilibili</button>
      </div>`,
    faq: `
      <dl class="help-faq">
        <dt>Don't see the item in game after adding?</dt>
        <dd>You must reload the modified save inside the game. Avoid overwriting the same save slot from a running game session before reloading.</dd>
        <dt>Does Steam / GOG Cloud Save affect edits?</dt>
        <dd>If a cloud conflict prompt appears when launching the game, choose to keep the <strong>Local Save</strong> so cloud sync doesn't overwrite your changes.</dd>
        <dt>Item library is empty or says catalog unavailable?</dt>
        <dd>Make sure the official free DLC <strong>REDmod</strong> is installed (available on Steam / GOG / Epic) and your Game Directory is set properly.</dd>
        <dt>Can I use it without Phantom Liberty?</dt>
        <dd>Yes. When Phantom Liberty is not installed, expansion-only items are automatically hidden so you only see base-game items.</dd>
        <dt>Save slot shows "Unsupported"?</dt>
        <dd>
          <ul class="help-list">
            <li><strong>Older game save</strong>: Load and re-save it once in game (2.31 / 2.5) to upgrade the format.</li>
            <li><strong>Newer game update</strong>: Wait for a tool update (safety lock to protect your save).</li>
          </ul>
        </dd>
        <dt>Why is "Stash" recommended for Iconic weapons? How does Tier 5++ upgrading work?</dt>
        <dd>
          This is due to Cyberpunk 2077's native script <strong>level scaling mechanism</strong> (<code>RescaleStashedIconicsToPlayerLevel</code>):
          <ul class="help-list">
            <li>Under REDengine 2.0+, upgrading iconic weapons to Tier 5++ requires Phantom Liberty, player level 51+, and engine-calibrated loot tier data.</li>
            <li>Injected iconic weapons directly placed into personal backpack lack the game's drop loot calibration, capping manual crafting upgrades at Tier 5.</li>
            <li><strong>Solution</strong>: Choose <strong>"Stash (Vehicle Trunk / Apartment)"</strong>. Upon retrieving the weapon from your car trunk or apartment stash in game, the game's native script triggers level calibration, unlocking upgrades up to Tier 5++!</li>
          </ul>
        </dd>
        <dt>Will this corrupt my save file?</dt>
        <dd>Every write is byte-verified before saving: if anything outside the inventory node changes unexpectedly, the write is aborted and your save remains untouched.</dd>
      </dl>`,
    about: `
      <div class="help-about">
        <h3>GameTool · Cyberpunk 2077 Item Library</h3>
        <span id="help-version" class="help-version">v</span>
      </div>
      <h4>Author</h4>
      <div class="help-author">
        <span>Author: <strong>魔法苦茶籽</strong></span>
        <button class="ghost small" type="button" data-author-open>Bilibili</button>
      </div>
      <p class="help-note">Free standalone save editor. Follow the author's Bilibili page for updates after game patches.</p>
      <h4>Requirements</h4>
      <ul id="help-env" class="help-list">
        <li>Windows 10/11 64-bit</li>
        <li>Microsoft Edge WebView2 Runtime</li>
        <li>Cyberpunk 2077 v2.31 / v2.5 save (with or without Phantom Liberty)</li>
        <li>Official free DLC REDmod</li>
      </ul>
      <h4>Disclaimer</h4>
      <ul class="help-list">
        <li>Modifies save files only; never injects into the game process or alters game installation files.</li>
        <li>All item names, descriptions, and icons are read dynamically from your local game installation at runtime.</li>
        <li>Unofficial tool, not affiliated with CD PROJEKT RED. For single-player use only.</li>
      </ul>`
  }
};

function bindAuthorButtons() {
  for (const button of document.querySelectorAll("[data-author-open]")) {
    button.onclick = () => { openAuthorPage(); };
  }
}

function showHelpTab(name) {
  helpTab = name;
  for (const tab of helpTabs) {
    const on = tab.dataset.helpTab === name;
    tab.classList.toggle("active", on);
    tab.setAttribute("aria-selected", on ? "true" : "false");
  }
  for (const pane of helpPanes)
    pane.hidden = pane.dataset.helpPane !== name;
  helpBodyEl.scrollTop = 0;
}

function applyAppInfo(info) {
  if (info && info.version) {
    currentAppVersion = info.version;
    if (helpVersionEl)
      helpVersionEl.textContent = "v" + info.version;
  }
  if (info && info.language && I18N[info.language] && info.language !== uiLang) {
    uiLang = info.language;
    applyUiLang();
  }
}

function openHelp(tab) {
  skinPanel.hidden = true;
  showHelpTab(tab || "start");
  helpEl.hidden = false;
  helpCloseEl.focus();
}

function closeHelp() {
  helpEl.hidden = true;
}

/** 主界面就绪后调用：设置文件里还没有「已弹出」标记时，打开「快速上手」并记下标记。返回是否弹出。 */
async function autoShowHelp() {
  const info = await call("app.info", {});
  applyAppInfo(info);
  if (info.helpShown)
    return false;
  openHelp("start");
  await call("app.markHelpShown", {});
  return true;
}

helpOpenEl.addEventListener("click", () => openHelp("start"));
helpCloseEl.addEventListener("click", () => closeHelp());
helpEl.addEventListener("click", (event) => {
  if (event.target === helpEl)
    closeHelp();
});
for (const tab of helpTabs)
  tab.addEventListener("click", () => showHelpTab(tab.dataset.helpTab));

function openAuthorPage() {
  return call("app.openAuthorPage").catch((error) => showToast(messageOf(error), true));
}

bindAuthorButtons();
/** 说明开着时 Tab 只在弹窗内循环，不会跑到后面的按钮上（例如在遮罩下打开外观面板）。 */
function trapHelpFocus(event) {
  const focusable = [...helpEl.querySelectorAll("button")].filter((node) => !node.disabled && node.offsetParent !== null);
  if (focusable.length === 0)
    return;
  const index = focusable.indexOf(document.activeElement);
  const last = focusable.length - 1;
  let next = null;
  if (index < 0)
    next = event.shiftKey ? focusable[last] : focusable[0];
  else if (event.shiftKey && index === 0)
    next = focusable[last];
  else if (!event.shiftKey && index === last)
    next = focusable[0];
  if (next) {
    event.preventDefault();
    next.focus();
  }
}

// 捕获阶段先处理：Esc 只关说明，不连带关掉下面的设置面板；Tab 限制在弹窗内。
document.addEventListener("keydown", (event) => {
  if (helpEl.hidden || !confirmEl.hidden)
    return;
  if (event.key === "Tab") {
    trapHelpFocus(event);
    return;
  }
  if (event.key !== "Escape")
    return;
  event.preventDefault();
  event.stopImmediatePropagation();
  closeHelp();
}, true);

// ---------- 路径设置（D020：手动设置存档目录和游戏目录） ----------

const setupEl = document.querySelector("#setup");
const setupIntro = document.querySelector("#setup-intro");
const setupError = document.querySelector("#setup-error");
const setupCancel = document.querySelector("#setup-cancel");
const setupDone = document.querySelector("#setup-done");
const setupFile = document.querySelector("#setup-file");
const settingsOpenEl = document.querySelector("#settings-open");
const mainEl = document.querySelector("main");
const setupRows = {
  saveRoot: {
    path: document.querySelector("#setup-save-path"),
    pick: document.querySelector("#setup-save-pick"),
    hint: document.querySelector("#setup-save-hint"),
    status: document.querySelector("#setup-save-status")
  },
  gameDir: {
    path: document.querySelector("#setup-game-path"),
    pick: document.querySelector("#setup-game-pick"),
    hint: document.querySelector("#setup-game-hint"),
    status: document.querySelector("#setup-game-status")
  }
};
const setup = { first: false, busy: false, info: null, saveRoot: null, gameDir: null };

/** 设置里已有的一项 → 面板上的一行：没设置过就不显示错误，只显示提示。 */
function setupEntry(path, check) {
  if (!path)
    return { path: "", ok: false, message: "", warning: "", detected: "" };
  return {
    path: check.ok ? check.path : path,
    ok: !!check.ok,
    message: check.message || "",
    warning: check.warning || "",
    detected: check.detected || ""
  };
}

/**
 * 一行的结果文字：没通过时是原因；通过时是说明，加上游戏目录的检测结果（D023，如「已检测到：仅本体（往日之影物品将不显示）」）和提醒。
 */
function setupStatusText(entry) {
  if (!entry.ok)
    return entry.warning || entry.message;
  return [entry.message, entry.detected, entry.warning].filter(Boolean).join(" ");
}

function renderSetupRow(kind) {
  const row = setupRows[kind];
  const entry = setup[kind];
  row.path.textContent = entry.path || t().setupUnset;
  row.path.title = entry.path || "";
  row.path.classList.toggle("unset", !entry.path);
  row.pick.disabled = setup.busy;
  const status = setupStatusText(entry);
  row.status.hidden = !status;
  row.status.className = "setup-status " + (!entry.ok ? "bad" : entry.warning ? "warn" : "ok");
  row.status.textContent = status;
}

function renderSetup() {
  const info = setup.info || {};
  setupRows.saveRoot.hint.textContent = info.saveRootHint ? t().setupHintPrefix + info.saveRootHint : "";
  setupRows.gameDir.hint.textContent = info.gameDirHint || "";
  renderSetupRow("saveRoot");
  renderSetupRow("gameDir");
  setupIntro.textContent = setup.first ? t().setupIntroFirst : t().setupIntroEdit;
  setupDone.textContent = setup.first ? t().setupDoneFirst : t().setupDoneSave;
  setupDone.disabled = setup.busy || !setup.saveRoot.ok || !setup.gameDir.ok;
  setupCancel.hidden = setup.first;
  setupFile.textContent = info.file ? t().setupFilePrefix + info.file : "";
}

function openSetup(info, first) {
  setup.first = first;
  setup.busy = false;
  setup.info = info;
  setup.saveRoot = setupEntry(info.saveRoot, info.saveRootCheck);
  setup.gameDir = setupEntry(info.gameDir, info.gameDirCheck);
  setupError.hidden = true;
  setupEl.classList.toggle("first", first);
  mainEl.hidden = first;
  settingsOpenEl.hidden = first;
  skinPanel.hidden = true;
  renderSetup();
  setupEl.hidden = false;
}

function closeSetup() {
  setupEl.hidden = true;
  mainEl.hidden = false;
  settingsOpenEl.hidden = false;
}

async function pickSetupFolder(kind) {
  if (setup.busy)
    return;
  setup.busy = true;
  setupError.hidden = true;
  renderSetup();
  try {
    const picked = await call("settings.pickFolder", { gameId, kind });
    if (!picked.cancelled) {
      setup[kind] = {
        path: picked.ok ? picked.path : picked.picked,
        ok: !!picked.ok,
        message: picked.message || "",
        warning: picked.warning || "",
        detected: picked.detected || ""
      };
    }
  } catch (error) {
    setupError.textContent = messageOf(error);
    setupError.hidden = false;
  } finally {
    setup.busy = false;
    renderSetup();
  }
}

/** 换了路径：清掉旧存档的卡片和图标版本，从第一个存档重新加载。 */
async function reloadWithNewPaths() {
  selected = null;
  selectedBackup = null;
  library.kind = "";
  library.query = "";
  library.page = 1;
  library.groups = [];
  library.iconVersion = null;
  searchEl.value = "";
  gridEl.replaceChildren();
  pagerEl.hidden = true;
  showError(null);
  showTab("saves");
  await refresh();
}

async function finishSetup() {
  if (setup.busy || setupDone.disabled)
    return;
  setup.busy = true;
  setupError.hidden = true;
  renderSetup();
  try {
    const info = await call("settings.save", { gameId, saveRoot: setup.saveRoot.path, gameDir: setup.gameDir.path });
    const first = setup.first;
    setup.info = info;
    closeSetup();
    await reloadWithNewPaths();
    showToast(first ? t().toastSetupFirst : t().toastSetupSaved);
    // D022：第一次设置完路径，自动弹出一次「快速上手」（已弹出过就不再弹）。
    if (first)
      await autoShowHelp().catch(showError);
  } catch (error) {
    setupError.textContent = messageOf(error);
    setupError.hidden = false;
  } finally {
    setup.busy = false;
    if (!setupEl.hidden)
      renderSetup();
  }
}

async function openSettingsPanel() {
  try {
    openSetup(await call("settings.get", { gameId }), false);
  } catch (error) {
    showError(error);
  }
}

setupRows.saveRoot.pick.addEventListener("click", () => pickSetupFolder("saveRoot"));
setupRows.gameDir.pick.addEventListener("click", () => pickSetupFolder("gameDir"));
setupDone.addEventListener("click", () => finishSetup());
setupCancel.addEventListener("click", () => closeSetup());
settingsOpenEl.addEventListener("click", () => openSettingsPanel());
document.addEventListener("keydown", (event) => {
  if (event.key === "Escape" && !setupEl.hidden && !setup.first && !setup.busy && confirmEl.hidden)
    closeSetup();
});

// ---------- 窗口（D021：一体式标题栏） ----------
// 顶栏的拖动、双击最大化、右键系统菜单由 WebView2 的 app-region 交给系统；这里只管右上角三个按钮和四周的缩放边。

const winMaxEl = document.querySelector("#win-max");

function applyWindowState(state) {
  if (!state)
    return;
  document.body.classList.toggle("maximized", !!state.maximized);
  document.body.classList.toggle("inactive", state.active === false);
  const label = state.maximized ? t().winRestore : t().winMax;
  winMaxEl.title = label;
  winMaxEl.setAttribute("aria-label", label);
}

function windowCall(method, params) {
  call(method, params || {}).catch(() => {});
}

document.querySelector("#win-min").addEventListener("click", () => windowCall("window.minimize"));
winMaxEl.addEventListener("click", () => windowCall("window.toggleMaximize"));
document.querySelector("#win-close").addEventListener("click", () => windowCall("window.close"));
for (const edgeEl of document.querySelectorAll(".edge")) {
  edgeEl.addEventListener("mousedown", (event) => {
    if (event.button !== 0 || document.body.classList.contains("maximized"))
      return;
    event.preventDefault();
    windowCall("window.startResize", { edge: edgeEl.dataset.edge });
  });
}
document.addEventListener("gametool", (event) => {
  if (event.detail.event === "window")
    applyWindowState(event.detail.data);
});
call("window.state", {}).then(applyWindowState).catch(() => {});

// ---------- 语言切换 ----------

function applyUiLang() {
  const L = t();
  titleEl.textContent = gameName + L.titleSuffix;
  helpOpenEl.textContent = L.helpOpen;
  document.querySelector("#skin-open").textContent = L.skinOpen;
  settingsOpenEl.title = L.settingsOpen;
  settingsOpenEl.setAttribute("aria-label", L.settingsOpen);
  const winMin = document.querySelector("#win-min");
  const winClose = document.querySelector("#win-close");
  winMin.title = L.winMin;
  winMin.setAttribute("aria-label", L.winMin);
  winClose.title = L.winClose;
  winClose.setAttribute("aria-label", L.winClose);
  const maxLabel = document.body.classList.contains("maximized") ? L.winRestore : L.winMax;
  winMaxEl.title = maxLabel;
  winMaxEl.setAttribute("aria-label", maxLabel);

  document.querySelector("#tab-saves").textContent = L.tabSaves;
  document.querySelector("#tab-backups").textContent = L.tabBackups;
  backupAddEl.textContent = L.backupAdd;
  searchEl.placeholder = L.searchPlaceholder;
  const pageSizeLabel = document.querySelector("#page-size-label");
  if (pageSizeLabel) pageSizeLabel.textContent = L.pageSizeLabel;

  const skinTitle = document.querySelector("#skin-title");
  if (skinTitle) skinTitle.textContent = L.skinTitle;
  const skinDropText = document.querySelector("#skin-drop-text");
  if (skinDropText) skinDropText.textContent = L.skinDropText;
  document.querySelector("#skin-clear").textContent = L.skinClear;
  const skinMaskLabel = document.querySelector("#skin-mask-label");
  if (skinMaskLabel) skinMaskLabel.textContent = L.skinMask;

  document.querySelector("#setup-title").textContent = L.setupTitle;
  const saveLabel = document.querySelector("#setup-save-label");
  if (saveLabel) saveLabel.textContent = L.setupSaveLabel;
  const gameLabel = document.querySelector("#setup-game-label");
  if (gameLabel) gameLabel.textContent = L.setupGameLabel;
  setupRows.saveRoot.pick.textContent = L.setupPickBtn;
  setupRows.gameDir.pick.textContent = L.setupPickBtn;
  setupCancel.textContent = L.cancel;

  document.querySelector("#help-title").textContent = L.helpTitle;
  helpCloseEl.title = L.winClose;
  helpCloseEl.setAttribute("aria-label", L.winClose);
  const tabStart = document.querySelector('[data-help-tab="start"]');
  const tabFaq = document.querySelector('[data-help-tab="faq"]');
  const tabAbout = document.querySelector('[data-help-tab="about"]');
  if (tabStart) tabStart.textContent = L.helpTabStart;
  if (tabFaq) tabFaq.textContent = L.helpTabFaq;
  if (tabAbout) tabAbout.textContent = L.helpTabAbout;

  const panesHtml = HELP_PANES_HTML[uiLang] || HELP_PANES_HTML["zh-cn"];
  for (const pane of helpPanes) {
    const key = pane.dataset.helpPane;
    if (panesHtml[key])
      pane.innerHTML = panesHtml[key];
  }
  helpVersionEl = document.querySelector("#help-version");
  if (helpVersionEl && currentAppVersion)
    helpVersionEl.textContent = "v" + currentAppVersion;
  bindAuthorButtons();

  for (const btn of document.querySelectorAll("#lang-switch .lang-btn")) {
    btn.classList.toggle("active", btn.dataset.lang === uiLang);
  }
}

async function switchLanguage(lang) {
  if (!I18N[lang] || lang === uiLang)
    return;
  uiLang = lang;
  applyUiLang();
  try {
    await call("app.setLanguage", { language: lang });
    if (!setupEl.hidden)
      renderSetup();
    if (booted) {
      renderSlots();
      renderBackups();
      if (!selected && !selectedBackup)
        showNoSlot();
      else if (selectedBackup)
        await selectBackup(selectedBackup);
      else if (selected && selected.supported !== false)
        await loadLibrary(false, false, true);
    }
  } catch (error) {
    showError(error);
  }
}

for (const btn of document.querySelectorAll("#lang-switch .lang-btn")) {
  btn.addEventListener("click", () => { switchLanguage(btn.dataset.lang); });
}

// ---------- 启动 ----------

async function boot() {
  const games = await call("app.games", {});
  if (games.length > 0) {
    gameId = games[0].id;
    gameName = games[0].displayName;
  }
  const appInfo = await call("app.info", {}).catch(() => null);
  if (appInfo)
    applyAppInfo(appInfo);
  applyUiLang();
  applyTheme(await call("theme.get", { gameId }));
  applySkin(await call("skin.get", { gameId }));
  const query = new URLSearchParams(location.search);
  const settings = await call("settings.get", { gameId });
  // 自检用命令行给的临时路径，从不显示引导页。
  if (!settings.ready && !query.get("selftest")) {
    openSetup(settings, true);
    return;
  }
  await refresh();
  // D022：主界面就绪且设置文件里没有「已弹出」标记（含已设置好路径、但还没见过说明的老用户），自动弹出一次。
  // 自检用的是临时设置文件，同样会弹出，由 help-autoshow 步骤检查。
  await autoShowHelp().catch(showError);
  // --show help-start / help-faq / help-about：直接打开说明的某一页（截图用，与 --show backups 同类）。
  const helpShow = /^help-(start|faq|about)$/.exec(query.get("show") || "");
  if (helpShow)
    openHelp(helpShow[1]);
  if (query.get("show") === "backups" && selected) {
    await call("backup.add", { gameId, slot: selected.slot });
    backups = await call("backup.list", { gameId });
    renderBackups();
    showTab("backups");
    if (backups.length > 0)
      await selectBackup(backups[0]);
  }
  if (query.get("selftest") === "1")
    window.chrome.webview.postMessage({ id: "selftest", method: "app.ping" });
  if (query.get("selftest") === "full")
    await runFullSelfTest();
}

// ---------- 完整自检（--self-test=full，临时副本） ----------

function waitEvent(name, timeoutMs) {
  return new Promise((resolve, reject) => {
    const timer = setTimeout(() => {
      document.removeEventListener("gametool", onEvent);
      reject(new Error("等待 " + name + " 超时"));
    }, timeoutMs);
    function onEvent(event) {
      if (event.detail.event !== name)
        return;
      clearTimeout(timer);
      document.removeEventListener("gametool", onEvent);
      resolve(event.detail);
    }
    document.addEventListener("gametool", onEvent);
  });
}

async function step(name, ok, detail) {
  await call("selftest.step", { name, ok, detail: detail || "" });
  if (!ok)
    throw new Error(name + " " + (detail || ""));
}

function quantityOf(snapshot, id) {
  return snapshot.resources.find((resource) => resource.id === id).quantity;
}

async function catalogItem(target, kind) {
  const page = await call("generate.catalog", { gameId, slot: selected.slot, kind: kind || null, query: target, take: 200 });
  return (page.items || []).find((item) => item.target === target) || null;
}

/**
 * 在打开的确认框里通过界面选中 target 这一条：分段按钮就点对应的按钮，下拉框就改选项并触发 change。
 * 只有一条时不需要选。返回预览区当前显示的图标键（应随选中的条目切换）。
 */
function pickVariantThroughUi(target) {
  const segment = [...document.querySelectorAll("#obtain-variants .seg")].find((node) => node.dataset.target === target);
  if (segment) {
    segment.click();
  } else {
    const select = document.querySelector("#obtain-variant");
    if (select) {
      const option = [...select.options].find((node) => node.dataset.target === target);
      if (!option)
        throw new Error("确认框里没有 " + target + " 这个选项");
      select.value = option.value;
      select.dispatchEvent(new Event("change"));
    }
  }
  const thumb = confirmPreview.querySelector(".thumb");
  return thumb ? thumb.dataset.iconKey : null;
}

/** 通过界面获得：选中物品所在分类、搜索框定位卡片 → 点「获得」→ 选品阶 / 款式 → 填数量 → 点确认框里的「获得」。 */
async function obtainThroughUi(target, quantity, kind) {
  library.kind = kind;
  searchEl.value = target;
  library.query = target;
  await loadLibrary(true, false);
  const group = library.groups.find((entry) => groupHasTarget(entry, target));
  const card = cardOf(target);
  if (!group || !card)
    throw new Error("界面上找不到 " + target);
  card.click();
  if (group.variants.length > 1)
    pickVariantThroughUi(target);
  const input = document.querySelector("#obtain-quantity");
  if (quantity != null) {
    if (!input)
      throw new Error("确认框没有数量输入");
    input.value = String(quantity);
    input.dispatchEvent(new Event("input"));
  }
  const outcome = nextObtainOutcome();
  [...confirmActions.children].find((node) => node.textContent === "获得").click();
  return outcome;
}

async function libraryStackTarget(kinds) {
  for (const preferred of ["Items.CommonMaterial1", "Items.money"]) {
    const found = await catalogItem(preferred, null);
    if (found && found.stackable)
      return found;
  }
  for (const kind of kinds.filter((item) => item.stackable)) {
    const page = await call("generate.catalog", { gameId, slot: selected.slot, kind: kind.id, take: 20 });
    const found = (page.items || []).find((item) => (item.held || 0) + 5 <= item.maxQuantity);
    if (found)
      return found;
  }
  return null;
}

async function unownedClothing() {
  for (let skip = 0; skip < 2000; skip += 200) {
    const page = await call("generate.catalog", { gameId, slot: selected.slot, kind: "clothing", skip, take: 200 });
    const free = (page.items || []).find((item) => !item.owned);
    if (free)
      return free;
    if (skip + page.items.length >= page.total)
      return null;
  }
  return null;
}

function startsWithPng(bytes) {
  const signature = [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a];
  return bytes.length >= 8 && signature.every((value, index) => bytes[index] === value);
}

/** 卡片里的图标结果：art = 游戏图标已显示；placeholder = 只有占位图形；pending = 还在加载。 */
function cardArtState(card) {
  const icon = card && card.querySelector(".icon");
  if (!icon)
    return "missing";
  if (icon.classList.contains("has-art"))
    return "art";
  return icon.querySelector("img.art") ? "pending" : (icon.querySelector("svg") ? "placeholder" : "missing");
}

async function waitCardArt(card, timeoutMs) {
  const started = Date.now();
  while (cardArtState(card) === "pending" && Date.now() - started < timeoutMs)
    await new Promise((resolve) => setTimeout(resolve, 100));
  return cardArtState(card);
}

/**
 * 物品图标：直接请求第一张卡片的图标地址。有游戏时应是 200 + PNG 签名，卡片显示图标；
 * 没有游戏（或这件物品没有图标）时应是 404，卡片保留占位图形。两种都算通过，其他状态码不算。
 */
async function runIconSelfTest(version) {
  const card = gridEl.querySelector(".item");
  const record = card ? card.querySelector(".icon").dataset.iconKey : "Items.money";
  const url = iconUrl(record, version || "none");
  const started = performance.now();
  const response = await fetch(url);
  const bytes = new Uint8Array(await response.arrayBuffer());
  const elapsed = Math.round(performance.now() - started);
  const shown = card ? await waitCardArt(card, 5000) : "no-card";
  const ok = response.status === 200
    ? startsWithPng(bytes) && (!card || shown === "art")
    : response.status === 404 && (!card || shown === "placeholder");
  await step("library-icon", ok,
    record + " status=" + response.status + " bytes=" + bytes.length + " card=" + shown + " version=" + (version || "none") + " " + elapsed + "ms");
}

/** 找一张多品阶的卡片，以及其中一条不是默认项、还没拥有的条目（义体不需要同类模板，优先用它）。 */
async function variantTarget() {
  for (const kind of ["cyberware", "arm_cyberware", "quickhack", "mod", "melee"]) {
    if (!library.kinds.some((item) => item.id === kind))
      continue;
    const page = await call("library.catalog", { gameId, slot: selected.slot, kind, take: 200 });
    for (const group of page.groups || []) {
      if (group.variants.length < 2)
        continue;
      const pick = group.variants.find((item, position) => position !== group.defaultIndex && !item.owned);
      if (pick)
        return { group, pick };
    }
  }
  return null;
}

/**
 * 合并卡片：搜索卡片名 → 点「获得」→ 确认框默认是最高品阶 → 在选项里点一个非默认的条目，预览图标随之切换 →
 * 获得，写入的是选中的那一条（而不是默认项）。
 */
async function runVariantSelfTest() {
  const found = await variantTarget();
  if (!found) {
    await step("library-variant", true, "skipped: 没有多品阶的卡片");
    return;
  }
  const { group, pick } = found;
  library.kind = group.kind;
  renderChips();
  searchEl.value = group.label;
  library.query = group.label;
  await loadLibrary(true, false);
  const card = [...gridEl.children].find((node) => node.dataset.key === group.key);
  if (!card)
    throw new Error("界面上找不到卡片 " + group.key);
  const fallback = defaultVariant(group);
  card.click();
  const segments = document.querySelectorAll("#obtain-variants .seg").length;
  const dropdown = !!document.querySelector("#obtain-variant");
  const thumb = confirmPreview.querySelector(".thumb");
  const startedOnDefault = !!thumb && thumb.dataset.iconKey === fallback.recordName;
  const shownKey = pickVariantThroughUi(pick.target);
  const outcome = nextObtainOutcome();
  [...confirmActions.children].find((node) => node.textContent === "获得").click();
  const result = await outcome;
  if (!result.ok)
    closeDialog();
  const owned = result.ok ? await catalogItem(pick.target, group.kind) : null;
  await step("library-variant",
    startedOnDefault && shownKey === pick.recordName && (segments === group.variants.length || dropdown)
      && result.ok && result.result.target === pick.target && !!owned && owned.owned === true,
    group.label + " " + group.variants.length + group.variantNoun + " 默认=" + fallback.variantLabel + " 选中=" + pick.variantLabel + " " + pick.target
      + (result.ok ? "" : " " + messageOf(result.error)));
}

function pause(ms) {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

/** 等界面上的物品库加载完（点标签、翻页、改每页数量、搜索都会触发加载）。 */
async function libraryIdle() {
  const started = Date.now();
  while (library.loading && Date.now() - started < 15000)
    await pause(50);
  if (library.loading)
    throw new Error("物品库加载超时");
}

function chipOf(kind) {
  return [...chipsEl.children].find((node) => node.dataset.kind === kind) || null;
}

/** 通过界面上的「每页」下拉框改每页卡片数。 */
async function choosePageSize(size) {
  pageSizeEl.value = String(size);
  pageSizeEl.dispatchEvent(new Event("change"));
  await libraryIdle();
}

/** 通过搜索框输入（走 200 ms 防抖）。 */
async function typeSearch(text) {
  searchEl.value = text;
  searchEl.dispatchEvent(new Event("input"));
  await pause(400);
  await libraryIdle();
}

/**
 * 分页（T024）：选一个超过 30 张卡片的分类，每页改成 30（回到第 1 页），点翻页条上的「2」，
 * 界面上的卡片应正好是直接请求 skip=30、take=30 的那一页，结果区回到顶部。结束后还原每页数量（它记在本机）。
 */
async function runPagingSelfTest() {
  const original = library.size;
  const size = original === PAGE_SIZES[0] ? PAGE_SIZES[1] : PAGE_SIZES[0];
  let kind = null;
  for (const item of library.kinds) {
    const probe = await call("library.catalog", { gameId, slot: selected.slot, kind: item.id, take: 1 });
    if (probe.total > size) {
      kind = item.id;
      break;
    }
  }
  if (!kind) {
    await step("library-paging", true, "skipped: 没有超过 " + size + " 张卡片的分类");
    return;
  }
  chipOf(kind).click();
  await libraryIdle();
  await choosePageSize(size);
  const firstPage = library.kind === kind && library.page === 1 && library.size === size && gridEl.children.length === size;
  const next = [...pagerEl.querySelectorAll(".pg")].find((node) => node.textContent === "2");
  if (!next)
    throw new Error("翻页条上没有第 2 页");
  resultsEl.scrollTop = resultsEl.scrollHeight;
  next.click();
  await libraryIdle();
  const expected = await call("library.catalog", { gameId, slot: selected.slot, kind, skip: size, take: size });
  const shown = [...gridEl.children].map((node) => node.dataset.key);
  const wanted = expected.groups.map((group) => group.key);
  const pages = Math.ceil(expected.total / size);
  const active = pagerEl.querySelector(".pg.active");
  const ok = firstPage && library.page === 2 && shown.length > 0 && shown.join("|") === wanted.join("|")
    && !pagerEl.hidden && !!active && active.textContent === "2" && resultsEl.scrollTop === 0
    && metaEl.textContent.includes("第 2 / " + pages + " 页");
  const meta = metaEl.textContent;
  await choosePageSize(original);
  await step("library-paging", ok && library.page === 1 && library.size === original,
    kind + " total=" + expected.total + " pages=" + pages + " shown=" + shown.length + " first=" + (shown[0] || "") + " meta=" + meta);
}

/**
 * 跨分类搜索（T024）：先停在一个没有命中的分类，输入能命中多个分类的词：每个标签上的数字与宿主的 kindCounts 一致，
 * 并自动切到第一个有命中的分类；清空搜索后数字消失。
 */
async function runSearchCountSelfTest() {
  let query = null;
  let expected = null;
  for (const candidate of ["传说", "史诗", "稀有", "标志性"]) {
    const probe = await call("library.catalog", { gameId, slot: selected.slot, kindFallback: true, query: candidate, take: 1 });
    const counts = probe.kindCounts || {};
    if (Object.values(counts).filter((count) => count > 0).length >= 2) {
      query = candidate;
      expected = counts;
      break;
    }
  }
  if (!query) {
    await step("library-search-counts", true, "skipped: 没有能命中多个分类的搜索词");
    return;
  }
  const miss = library.kinds.find((kind) => !expected[kind.id]);
  const firstHit = library.kinds.find((kind) => expected[kind.id] > 0).id;
  if (miss) {
    chipOf(miss.id).click();
    await libraryIdle();
  }
  const from = library.kind;
  await typeSearch(query);
  const chips = [...chipsEl.children];
  const countsOk = chips.length === library.kinds.length
    && chips.every((chip) => chip.dataset.count === String(expected[chip.dataset.kind] || 0));
  const landed = library.kind;
  const switched = miss ? landed === firstHit : (expected[landed] || 0) > 0;
  const shownOk = library.total === expected[landed] && gridEl.children.length === Math.min(library.size, library.total);
  await typeSearch("");
  const cleared = library.counts === null && chipsEl.querySelectorAll(".chip-count").length === 0
    && [...chipsEl.children].every((chip) => chip.dataset.count === undefined);
  await step("library-search-counts", countsOk && switched && shownOk && cleared,
    query + " " + library.kinds.map((kind) => kind.id + "=" + (expected[kind.id] || 0)).join(",") + " " + from + "→" + landed);
}

async function runLibrarySelfTest() {
  const catalog = await call("generate.catalog", { gameId, slot: selected.slot, take: 5 });
  if (!catalog.namesAvailable) {
    await step("library-catalog", !!catalog.message, "名字表不可用：" + catalog.message);
    await runIconSelfTest(null);
    return;
  }
  await selectSlot(selected);
  const firstKind = catalog.kinds.length > 0 ? catalog.kinds[0].id : "";
  const firstItems = await call("generate.catalog", { gameId, slot: selected.slot, kind: firstKind || null, take: 1 });
  const cards = gridEl.querySelectorAll(".item").length;
  const chipKinds = [...chipsEl.children].map((chip) => chip.dataset.kind).join(",");
  const active = chipsEl.querySelector(".chip.active");
  // D019：没有「全部」标签，默认选中第一个分类；卡片按同名合并（T023）：卡片里的条目合计等于这个分类逐条目录的总数。
  await step("library-catalog",
    catalog.total > 0 && !!firstKind && chipKinds === catalog.kinds.map((kind) => kind.id).join(",")
      && !!active && active.dataset.kind === firstKind && library.kind === firstKind && library.page === 1
      && cards > 0 && cards === Math.min(library.size, library.total)
      && library.total <= firstItems.total && library.itemTotal === firstItems.total,
    "kinds=" + chipKinds + " active=" + (active && active.dataset.kind) + " items=" + firstItems.total + " groups=" + library.total + " cards=" + cards);
  await runIconSelfTest(catalog.iconVersion);
  await runPagingSelfTest();
  await runSearchCountSelfTest();

  const stack = await libraryStackTarget(catalog.kinds);
  if (!stack)
    throw new Error("目录里没有可用的堆叠物品");
  const before = stack.held || 0;
  const backupsBefore = (await call("backup.list", { gameId })).length;
  const first = await obtainThroughUi(stack.target, 3, stack.kind);
  const afterFirst = await catalogItem(stack.target, null);
  const card = cardOf(stack.target);
  await step("library-stack",
    first.ok && first.result.after === before + 3 && afterFirst.held === before + 3 && !!card && card.querySelector(".state").textContent.includes(formatNumber(before + 3)),
    stack.target + " " + before + "→" + (afterFirst && afterFirst.held) + (first.ok ? "" : " " + messageOf(first.error)));

  const clothing = await unownedClothing();
  if (clothing) {
    const gear = await obtainThroughUi(clothing.target, null, clothing.kind);
    if (gear.ok) {
      const owned = await catalogItem(clothing.target, "clothing");
      await step("library-gear", gear.result.stackable === false && !!owned && owned.owned === true, clothing.target + " 模板 " + (gear.result.templateLabel || ""));
    } else {
      // 自检存档里没有可当模板的服装：应是文档化的拒绝，存档不变。
      closeDialog();
      const text = messageOf(gear.error);
      await step("library-gear", text.includes("作为模板"), "no-template: " + text);
    }
  } else {
    await step("library-gear", true, "skipped: 没有未拥有的服装");
  }

  await runVariantSelfTest();

  const second = await call("library.obtain", { gameId, slot: selected.slot, target: stack.target, quantity: 2 });
  const backupsFinal = (await call("backup.list", { gameId })).length;
  // D019：获得不做任何备份，界面获得（堆叠、装备、多品阶）和直接获得之后备份区数量都不变。
  await step("library-no-backup", second.after === before + 5 && second.backupId === undefined && backupsFinal === backupsBefore,
    "backups " + backupsBefore + "→" + backupsFinal);
  searchEl.value = "";
  library.query = "";
  await loadLibrary(true, false);
}

/** 等页面上的条件成立（轮询，超时返回 false）。 */
async function until(condition, timeoutMs) {
  const started = Date.now();
  while (!condition()) {
    if (Date.now() - started > timeoutMs)
      return false;
    await pause(50);
  }
  return true;
}

/**
 * T030：宿主在自检沙箱的存档目录里新建一个槽位，页面不做任何操作，存档列表应自动出现它；
 * 再把选中的存档覆盖成原始副本（相当于游戏重新存档），选中存档的时间和物品库应自动重载，分类、页码、搜索词不变。
 */
async function runAutoRefreshSelfTest() {
  const before = slots.length;
  const keep = { kind: library.kind, page: library.page, query: library.query, slot: selected && selected.slot };
  const pushed = waitEvent("saves.changed", 10000);
  const created = await call("selftest.newSlot", {});
  await pushed;
  const listed = await until(() => slots.some((slot) => slot.name === created.name), 5000);
  const listOk = listed && slots.length === before + 1 && savesPane.textContent.includes(created.name)
    && !!selected && selected.slot === keep.slot;

  const oldTime = selected.time;
  const token = library.token;
  const rewritten = waitEvent("saves.changed", 10000);
  await call("selftest.overwrite", {});
  await rewritten;
  const reloaded = await until(() => selected.time !== oldTime && library.token > token && !library.loading, 5000);
  const kept = library.kind === keep.kind && library.page === keep.page && library.query === keep.query
    && slotTimeEl.textContent === formatTime(selected.time) && gridEl.children.length > 0;
  await step("saves-autorefresh", listOk && reloaded && kept,
    created.name + " slots " + before + "→" + slots.length + " reloaded=" + reloaded + " kind=" + library.kind + " page=" + library.page);
}

function samePath(left, right) {
  const trim = (value) => String(value || "").replace(/[\\/]+$/, "").toLowerCase();
  return !!left && trim(left) === trim(right);
}

/**
 * D020：路径校验走桥接（坏路径、好的临时存档目录、单个存档文件夹 → 上一级、不是游戏目录），
 * 再点齿轮打开设置面板，检查两行显示的是当前路径，取消后关闭。自检从不显示首次引导页，也不碰真实设置文件。
 */
async function runSettingsSelfTest() {
  const info = await call("settings.get", { gameId });
  const root = info.saveRoot || "";
  const bad = await call("settings.validate", { gameId, kind: "saveRoot", path: root + "\\不存在的目录" });
  const good = await call("settings.validate", { gameId, kind: "saveRoot", path: root });
  const single = await call("settings.validate", { gameId, kind: "saveRoot", path: root + "\\ManualSave-0" });
  const notGame = await call("settings.validate", { gameId, kind: "gameDir", path: root });
  const validated = info.saveRootCheck.ok === true
    && bad.ok === false && String(bad.message).includes("不存在")
    && good.ok === true && samePath(good.path, root)
    && single.ok === true && samePath(single.path, root) && String(single.message).includes("上一级")
    && notGame.ok === false && String(notGame.message).includes("Cyberpunk2077.exe");
  await step("settings-validate", validated && setupEl.hidden && !mainEl.hidden,
    "bad=" + bad.message + " single=" + single.message + " game=" + (info.gameDirCheck.ok ? "ok " + (info.gameDirCheck.warning || "") : info.gameDirCheck.message));

  settingsOpenEl.click();
  for (let i = 0; i < 50 && setupEl.hidden; i++)
    await pause(20);
  const shown = !setupEl.hidden && !mainEl.hidden && !setupCancel.hidden && setupDone.textContent === "保存"
    && samePath(setupRows.saveRoot.path.textContent, root)
    && setupDone.disabled === !(info.saveRootCheck.ok && info.gameDirCheck.ok);
  // D021：设置面板开着时（路径、常见位置、设置文件位置都显示出来）把界面上的全部文字交给宿主，检查里面没有真实的用户目录。
  const leak = await call("selftest.leak", { text: displayedText() });
  const hint = setupRows.saveRoot.hint.textContent;
  setupCancel.click();
  await step("settings-panel", shown && setupEl.hidden && settingsOpenEl.hidden === false,
    "path=" + setupRows.saveRoot.path.textContent + " done=" + setupDone.textContent);
  await step("path-masking", leak.clean === true && leak.length > 0 && hint.includes("%USERPROFILE%\\") && !hint.includes(":\\Users\\"),
    "clean=" + leak.clean + " hint=" + hint + " file=" + setupFile.textContent + " bad=" + bad.message);
}

/**
 * D023：宿主在临时目录里搭两个假游戏目录（只有占位文件），经桥接校验：只有本体的显示「仅本体」，
 * 有往日之影内容归档和 tweakdb_ep1.bin 的显示「本体 + 往日之影」；设置行的文字里带上检测结果。当前游戏目录也要给出检测结果。
 */
async function runExpansionSelfTest() {
  const fake = await call("selftest.fakeGame", {});
  const baseOnly = await call("settings.validate", { gameId, kind: "gameDir", path: fake.baseOnly });
  const withExpansion = await call("settings.validate", { gameId, kind: "gameDir", path: fake.withExpansion });
  const baseText = setupStatusText(setupEntry(fake.baseOnly, baseOnly));
  const fullText = setupStatusText(setupEntry(fake.withExpansion, withExpansion));
  const info = await call("settings.get", { gameId });
  const current = info.gameDirCheck || {};
  const ok = baseOnly.ok === true && withExpansion.ok === true
    && baseOnly.detected === "已检测到：仅本体（往日之影物品将不显示）"
    && !!baseOnly.expansions && baseOnly.expansions.phantomLiberty === false
    && withExpansion.detected === "已检测到：本体 + 往日之影"
    && !!withExpansion.expansions && withExpansion.expansions.phantomLiberty === true
    && baseText.includes("仅本体") && fullText.includes("本体 + 往日之影")
    && (!current.ok || String(current.detected || "").startsWith("已检测到："));
  await step("settings-expansion", ok, "base=" + baseText + " full=" + fullText + " current=" + (current.detected || current.message || ""));
}

/**
 * D022：自检的设置文件是临时的、没有「已弹出」标记，所以启动后（主界面就绪）应已自动打开「快速上手」，
 * 并已记下标记；关掉后重新读取状态，不会再弹出。
 */
async function runHelpAutoShowSelfTest() {
  const opened = !helpEl.hidden && helpTab === "start";
  const info = await call("app.info", {});
  helpCloseEl.click();
  const closed = helpEl.hidden;
  const again = await autoShowHelp();
  const stayed = again === false && helpEl.hidden;
  await step("help-autoshow", opened && info.helpShown === true && closed && stayed,
    "opened=" + opened + " marked=" + info.helpShown + " closed=" + closed + " again=" + again);
}

/** D022：点「说明」打开，依次切换三个页签（每次只显示一页且有内容），「关于」里有版本号；Esc 和关闭按钮都能关。 */
async function runHelpPanelSelfTest() {
  const info = await call("app.info", {});
  helpOpenEl.click();
  const shown = !helpEl.hidden && helpTab === "start";
  const seen = [];
  for (const tab of helpTabs) {
    tab.click();
    const visible = helpPanes.filter((pane) => !pane.hidden);
    const ok = visible.length === 1 && visible[0].dataset.helpPane === tab.dataset.helpTab
      && tab.classList.contains("active") && (visible[0].textContent || "").trim().length > 20;
    seen.push(tab.dataset.helpTab + "=" + ok);
  }
  const aboutShown = helpTab === "about" && !helpPanes.find((pane) => pane.dataset.helpPane === "about").hidden;
  const version = helpVersionEl.textContent;
  const versionOk = aboutShown && /^v\d+\.\d+\.\d+/.test(version) && version === "v" + info.version && helpVersionEl.offsetParent !== null;
  document.body.dispatchEvent(new KeyboardEvent("keydown", { key: "Escape", bubbles: true }));
  const escClosed = helpEl.hidden;
  helpOpenEl.click();
  const reopened = !helpEl.hidden && helpTab === "start";
  helpCloseEl.click();
  await step("help-panel",
    shown && seen.length === 3 && seen.every((entry) => entry.endsWith("=true")) && versionOk && escClosed && reopened && helpEl.hidden,
    seen.join(" ") + " version=" + version + " esc=" + escClosed);
}

/** 等宿主的假启动器收到第 count 次请求（按钮点击是异步发出的）。 */
async function waitLaunched(count) {
  let launched = await call("selftest.launched", {});
  for (let i = 0; i < 100 && launched.count < count; i++) {
    await pause(30);
    launched = await call("selftest.launched", {});
  }
  return launched;
}

/**
 * D024：「关于」页有作者名、免费说明和「B 站主页」按钮（是按钮不是链接），运行环境里不再有 .NET；
 * 快速上手末尾也有同样的按钮。两个按钮各点一次，宿主的假启动器（自检从不打开浏览器）应收到两次、且都是固定的作者主页。
 * 常见问题有云存档一条；「不支持」的错误文字补上了游戏更新后的说明。
 */
async function runHelpAuthorSelfTest() {
  const aboutPane = helpPanes.find((pane) => pane.dataset.helpPane === "about");
  const startPane = helpPanes.find((pane) => pane.dataset.helpPane === "start");
  const faqPane = helpPanes.find((pane) => pane.dataset.helpPane === "faq");
  const aboutButton = aboutPane.querySelector("button[data-author-open]");
  const startButton = startPane.querySelector("button[data-author-open]");
  const aboutText = aboutPane.textContent || "";
  const envText = document.querySelector("#help-env").textContent || "";
  const textOk = aboutText.includes("作者：魔法苦茶籽") && aboutText.includes("本工具免费")
    && (startPane.textContent || "").includes("「魔法苦茶籽」")
    && (faqPane.textContent || "").includes("保留本机存档")
    && !envText.includes(".NET") && envText.includes("WebView2")
    && !helpEl.querySelector("a[href]")
    && messageOf({ code: "unsupported_save", message: "x" }).includes("作者 B 站动态");
  const before = (await call("selftest.launched", {})).count;
  openHelp("about");
  const aboutVisible = !!aboutButton && aboutButton.offsetParent !== null && aboutButton.textContent.trim() === "B 站主页";
  if (aboutButton)
    aboutButton.click();
  const first = await waitLaunched(before + 1);
  showHelpTab("start");
  const startVisible = !!startButton && startButton.offsetParent !== null && startButton.textContent.trim() === "B 站主页";
  if (startButton)
    startButton.click();
  const second = await waitLaunched(before + 2);
  closeHelp();
  await step("help-author",
    textOk && aboutVisible && startVisible && first.count === before + 1 && second.count === before + 2 && second.fixedOnly === true && helpEl.hidden,
    "text=" + textOk + " about=" + aboutVisible + " start=" + startVisible + " launched=" + second.count + " fixed=" + second.fixedOnly);
}

/** 界面上能看到的文字：正文、悬停提示（title）、输入框的值。 */
function displayedText() {
  const parts = [document.body.textContent || ""];
  for (const node of document.querySelectorAll("[title]"))
    parts.push(node.getAttribute("title") || "");
  for (const node of document.querySelectorAll("input, select"))
    parts.push(node.value || "");
  return parts.join("\n");
}

async function runFullSelfTest() {
  try {
    const before = slots.length;
    await step("list", before > 0, String(before));
    // D021：一体式标题栏的窗口方法能应答，自绘按钮和八个缩放边都在页面上（自检不真的最小化或关闭窗口）。
    const windowState = await call("window.state", {});
    await step("window-state",
      typeof windowState.maximized === "boolean" && typeof windowState.active === "boolean"
        && !!document.querySelector("#win-min") && !!winMaxEl && !!document.querySelector("#win-close")
        && document.querySelectorAll(".edge").length === 8,
      "maximized=" + windowState.maximized + " active=" + windowState.active);
    await runHelpAutoShowSelfTest();
    await runHelpPanelSelfTest();
    await runHelpAuthorSelfTest();
    await runSettingsSelfTest();
    await runExpansionSelfTest();
    const info = await call("saves.info", { gameId, slot: selected.slot });
    await step("info", info.resources.length === 10, String(info.resources.length));
    const legacy = slots.find((slot) => slot.name === "LegacySave");
    await step("legacy", !!legacy && legacy.supported === false && !!legacy.reason, legacy && legacy.reason);
    const residue = slots.find((slot) => slot.name === "ResidueSave");
    const residueInfo = await call("saves.info", { gameId, slot: residue.slot });
    await step("residue", residueInfo.resources.length === 10, String(residueInfo.resources.length));

    // 使能模式已从界面撤下（D013），桥接方法仍保留：直接调用覆盖原有流程。
    const originals = new Map(info.resources.map((resource) => [resource.id, resource.quantity]));
    const money = info.resources.find((resource) => resource.id === "money");
    const material = info.resources.find((resource) => resource.id === "craft.t1");
    const extra = info.resources.find((resource) => resource.id === "craft.t2");
    const targets = [
      { id: money.id, value: money.quantity + 1 },
      { id: material.id, value: material.quantity + 1 }
    ];
    const started = await call("enable.start", { gameId, slot: selected.slot, changes: targets, backupFirst: true });
    const listed = await call("backup.list", { gameId });
    const after = await call("saves.info", { gameId, slot: selected.slot });
    const changedOk = quantityOf(after, money.id) === money.quantity + 1 && quantityOf(after, material.id) === material.quantity + 1;
    const othersOk = after.resources.every((resource) => resource.id === money.id || resource.id === material.id || resource.quantity === originals.get(resource.id));
    const details = await call("backup.info", { gameId, backupId: started.backupId });
    const backedMoney = details.resources.find((resource) => resource.id === "money").backupQuantity;
    await step("backup-info", backedMoney === originals.get("money"), String(backedMoney));
    await step("enable", listed.length === 1 && changedOk && othersOk && !!started.backupId, "backup=" + started.backupId);
    const patched = waitEvent("patched", 10000);
    await call("selftest.overwrite", {});
    await patched;
    const rewritten = await call("saves.info", { gameId, slot: selected.slot });
    const restoredTargets = quantityOf(rewritten, money.id) === money.quantity + 1 && quantityOf(rewritten, material.id) === material.quantity + 1;
    await step("watch", restoredTargets, "");
    await call("enable.update", { gameId, changes: targets.concat([{ id: extra.id, value: extra.quantity + 1 }]) });
    const held = waitEvent("patched", 10000);
    await call("selftest.overwrite", {});
    await held;
    const updated = await call("saves.info", { gameId, slot: selected.slot });
    const heldOk = quantityOf(updated, money.id) === money.quantity + 1
      && quantityOf(updated, material.id) === material.quantity + 1
      && quantityOf(updated, extra.id) === extra.quantity + 1;
    await step("update", heldOk, "");
    let rejected = false;
    try {
      await call("backup.restore", { gameId, backupId: started.backupId });
    } catch (error) {
      rejected = true;
    }
    await step("restore-rejected", rejected, "");
    await call("enable.stop", {});
    let sawPatch = false;
    const listener = (event) => {
      if (event.detail.event === "patched")
        sawPatch = true;
    };
    document.addEventListener("gametool", listener);
    await call("selftest.overwrite", {});
    await new Promise((resolve) => setTimeout(resolve, 2500));
    document.removeEventListener("gametool", listener);
    await step("stopped", !sawPatch, "");
    await call("backup.restore", { gameId, backupId: started.backupId });
    const hash = await call("selftest.hash", { backupId: started.backupId });
    await step("restore", hash.match === true, "");
    await call("backup.delete", { gameId, backupId: started.backupId });
    const left = await call("backup.list", { gameId });
    await step("delete", left.length === 0, String(left.length));
    await call("skin.import", { gameId, fileName: "pixel.png", dataBase64: pixelPng });
    await call("skin.setOverlay", { gameId, value: 0.5 });
    const skin = await call("skin.get", { gameId });
    const image = await fetch("/skin/" + encodeURIComponent(gameId) + "/background?v=" + encodeURIComponent(skin.imageVersion || ""));
    await step("skin", skin.hasImage === true && skin.overlayOpacity === 0.5 && image.status === 200, String(image.status));
    await call("skin.clear", { gameId });
    const cleared = await call("skin.get", { gameId });
    await step("skin-clear", cleared.hasImage === false, "");
    const cloneListed = await call("clone.list", { gameId, slot: selected.slot });
    const gear = (cloneListed.items || [])[0];
    let cloneOk = false;
    if (gear) {
      const ran = await call("clone.run", { gameId, slot: selected.slot, key: gear.key, copies: 1, backupFirst: false });
      cloneOk = ran.copies === 1 && ran.addedBytes > 0 && (ran.seeds || []).length === 1;
    }
    await step("clone", cloneOk, gear ? gear.label : "none");
    await runLibrarySelfTest();
    await runAutoRefreshSelfTest();
    await call("selftest.done", { ok: true, detail: "full" });
  } catch (error) {
    await call("selftest.done", { ok: false, detail: messageOf(error) });
  }
}

boot().catch((error) => {
  showError(error);
  const query = new URLSearchParams(location.search);
  if (query.get("selftest") === "full")
    call("selftest.done", { ok: false, detail: messageOf(error) });
});
