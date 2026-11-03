"use strict";

// 界面文字（简体 / 繁體 / English）。app.js 用 t() 取当前语言的一份；三种语言的键必须一致。
// 改文字只改这里；新增键时三种语言都要加。

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
    ownershipAll: "全部",
    ownershipUnowned: "未拥有",
    ownershipOwned: "已拥有",
    stateHeld: (n) => "持有 " + n,
    stateNotHeld: "未持有",
    stateOwned: "已拥有",
    stateUnowned: "未拥有",
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
    destStashUnavailable: "这个存档还没有储藏室记录，暂时只能放入背包。在游戏里往载具后备箱或公寓储藏处放一件东西并存档后，就可以选择储藏室。",
    batchFailedIntro: (succ, fail) => "成功 " + succ + " 件，失败 " + fail + " 件。以下物品没有写入：",
    batchFailedOk: "知道了",
    toastObtained: (label, amount, toStash) => (toStash ? "已存入储藏室：" : "已放入背包：") + label + amount + "，读档后生效",
    batchToggle: "批量选择",
    batchToggleActive: "退出多选",
    batchSelectedCount: (n) => "已选 " + n + " 项",
    batchDestLabel: "存放位置：",
    batchDestSmart: "智能推荐（不朽入储藏室）",
    batchDestBackpack: "全部放入背包",
    batchDestStash: "全部放入储藏室",
    batchSubmit: (n) => "一键获得 (" + n + ")",
    batchClear: "清空",
    batchCancel: "退出多选",
    batchTitle: "批量获得物品",
    batchConfirmSummary: (slotName, count, destText) => "将向〈" + slotName + "〉添加选中的 " + count + " 件物品（" + destText + "），读档后生效：",
    batchEmptyAlert: "请先勾选要获得的物品。",
    batchConfirm: (n) => "确定要将选中的 " + n + " 件物品添加到当前存档吗？",
    batchToastSuccess: (n, ms) => "批量添加成功：共 " + n + " 件物品（耗时 " + ms + "ms）",
    batchToastPartial: (succ, fail) => "批量添加完成：成功 " + succ + " 件，失败 " + fail + " 件",
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
    helpTabAbout: "关于",
    appTitle: "游戏工具",
    gameFallback: "游戏",
    ariaLang: "语言",
    ariaCaption: "窗口",
    ariaOwnership: "状态筛选",
    ariaPager: "翻页",
    unknownError: "未知错误",
    qtyInvalid: "数量无效",
    skinTooLarge: "图片超过 20 MB。",
    labelSep: "：",
    withNote: (text, note) => text + "（" + note + "）"
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
    ownershipAll: "全部",
    ownershipUnowned: "未擁有",
    ownershipOwned: "已擁有",
    stateHeld: (n) => "持有 " + n,
    stateNotHeld: "未持有",
    stateOwned: "已擁有",
    stateUnowned: "未擁有",
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
    obtainSummary: (slotName, itemName) => "將向〈" + slotName + "〉新增〈" + itemName + "〉，讀檔後生效。",
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
    destStashUnavailable: "這個存檔還沒有儲藏室記錄，暫時只能放入背包。在遊戲裡往載具後備箱或公寓儲藏處放一件東西並存檔後，就可以選擇儲藏室。",
    batchFailedIntro: (succ, fail) => "成功 " + succ + " 件，失敗 " + fail + " 件。以下物品沒有寫入：",
    batchFailedOk: "知道了",
    toastObtained: (label, amount, toStash) => (toStash ? "已存入儲藏室：" : "已放入個人背包：") + label + amount + "，讀檔後生效",
    batchToggle: "批次選擇",
    batchToggleActive: "退出多選",
    batchSelectedCount: (n) => "已選 " + n + " 項",
    batchDestLabel: "存放位置：",
    batchDestSmart: "智慧推薦（不朽存儲藏箱）",
    batchDestBackpack: "全部放入背包",
    batchDestStash: "全部放入儲藏箱",
    batchSubmit: (n) => "一鍵獲得 (" + n + ")",
    batchClear: "清空",
    batchCancel: "退出多選",
    batchTitle: "批次獲得物品",
    batchConfirmSummary: (slotName, count, destText) => "將向〈" + slotName + "〉新增選中的 " + count + " 件物品（" + destText + "），讀檔後生效：",
    batchEmptyAlert: "請先勾選要獲得的物品。",
    batchConfirm: (n) => "確定要將選中的 " + n + " 件物品新增到存檔嗎？",
    batchToastSuccess: (n, ms) => "批次獲得成功：共 " + n + " 件物品（耗時 " + ms + "ms）",
    batchToastPartial: (succ, fail) => "批次新增完成：成功 " + succ + " 件，失敗 " + fail + " 件",
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
    helpTabAbout: "關於",
    appTitle: "遊戲工具",
    gameFallback: "遊戲",
    ariaLang: "語言",
    ariaCaption: "視窗",
    ariaOwnership: "狀態篩選",
    ariaPager: "翻頁",
    unknownError: "未知錯誤",
    qtyInvalid: "數量無效",
    skinTooLarge: "圖片超過 20 MB。",
    labelSep: "：",
    withNote: (text, note) => text + "（" + note + "）"
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
    ownershipAll: "All",
    ownershipUnowned: "Unowned",
    ownershipOwned: "Owned",
    stateHeld: (n) => "Held " + n,
    stateNotHeld: "Not held",
    stateOwned: "Owned",
    stateUnowned: "Unowned",
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
    destStashUnavailable: "This save has no stash record yet, so items can only go to the backpack. Put any item into a vehicle trunk or apartment stash in game and save, then Stash becomes available.",
    batchFailedIntro: (succ, fail) => succ + " succeeded, " + fail + " failed. These items were not written:",
    batchFailedOk: "OK",
    toastObtained: (label, amount, toStash) => (toStash ? "Stored in Stash: " : "Added to backpack: ") + label + amount + "; reload save in game",
    batchToggle: "Batch Select",
    batchToggleActive: "Exit Batch",
    batchSelectedCount: (n) => n + " selected",
    batchDestLabel: "Destination:",
    batchDestSmart: "Smart (Iconics to Stash)",
    batchDestBackpack: "All to Backpack",
    batchDestStash: "All to Stash",
    batchSubmit: (n) => "Obtain (" + n + ")",
    batchClear: "Clear",
    batchCancel: "Exit",
    batchTitle: "Batch Obtain",
    batchConfirmSummary: (slotName, count, destText) => "Add " + count + " selected items to 〈" + slotName + "〉 (" + destText + "); reload save to apply:",
    batchEmptyAlert: "Please select at least one item.",
    batchConfirm: (n) => "Add " + n + " selected items to save?",
    batchToastSuccess: (n, ms) => "Batch obtained " + n + " items (" + ms + "ms)",
    batchToastPartial: (succ, fail) => "Batch complete: " + succ + " succeeded, " + fail + " failed",
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
    helpTabAbout: "About",
    appTitle: "Game Tool",
    gameFallback: "Game",
    ariaLang: "Language",
    ariaCaption: "Window",
    ariaOwnership: "Ownership filter",
    ariaPager: "Pages",
    unknownError: "Unknown error",
    qtyInvalid: "Invalid quantity",
    skinTooLarge: "The image is larger than 20 MB.",
    labelSep: ": ",
    withNote: (text, note) => text + " (" + note + ")"
  }
};

// 说明对话框三个页签的内容（HTML），按语言切换时整块替换。
// 里面的 #help-version 和 [data-author-open] 由 app.js 在替换后重新绑定。

const HELP_PANES_HTML = {
  "zh-cn": {
    start: `
      <ol class="help-steps">
        <li><strong>设置目录</strong>：第一次打开时选择「存档目录」和「游戏目录」，两项都通过后点「开始使用」。以后可点右上角齿轮修改。</li>
        <li><strong>选择存档</strong>：在左侧「当前存档」里点选要修改的存档。</li>
        <li><strong>获得物品</strong>：在搜索框输入物品名，或点分类标签；在卡片上点「获得」，在确认框里选品阶或款式（可叠放物品填数量）后确认。</li>
        <li><strong>回游戏读档</strong>：回到游戏读取这个存档，新物品就在背包里。</li>
      </ol>
      <p class="help-tip">想留后路？先点「加入备份区」，出问题可在「备份区」一键还原。</p>
      <div class="help-author">
        <span>觉得好用，欢迎关注作者 B 站「魔法苦茶籽」获取更新。</span>
        <button class="ghost small" type="button" data-author-open>B 站主页</button>
      </div>`,
    faq: `
      <dl class="help-faq">
        <dt>获得后游戏里看不到？</dt>
        <dd>需要在游戏里重新读取这个存档。另外，游戏运行时如果又存到同一个存档位，会覆盖掉修改，建议在主菜单读档前使用。</dd>
        <dt>开了 Steam / GOG 云存档会有影响吗？</dt>
        <dd>修改后启动游戏时，如果平台弹出云存档冲突提示，请选择保留<strong>本机</strong>存档，否则修改会被云端的旧存档覆盖。不确定时，先把存档加入备份区。</dd>
        <dt>物品库是空的或提示名字表不可用？</dt>
        <dd>需要安装官方免费 DLC REDmod（Steam / GOG / Epic 均可获取），并在设置里选对游戏目录。</dd>
        <dt>没买往日之影能用吗？</dt>
        <dd>可以。工具会检测游戏目录：没有往日之影时，物品库只列本体物品，往日之影的物品不显示、也不会生成。设置里游戏目录一行会显示检测结果。</dd>
        <dt>存档显示「不支持」？</dt>
        <dd>分两种情况：
          <ul class="help-list">
            <li><strong>旧版本的存档</strong>：在游戏里读取后重新存档即可。</li>
            <li><strong>游戏更新后的新版本存档</strong>：工具需要适配新版本，暂时不能修改（这是保护存档的安全设计）。请关注作者 B 站获取更新。</li>
          </ul>
        </dd>
        <dt>怎么备份和还原？</dt>
        <dd>选中存档后点「加入备份区」；在「备份区」页签里可以查看、还原或删除备份。获得物品不会自动备份。</dd>
        <dt>会不会把存档改坏？</dt>
        <dd>每次写入前都会逐字节校验：除了新增或修改的物品，其他内容只要有任何变化就放弃写入，原存档保持不变。</dd>
        <dt>如何查看已拥有/未拥有的物品？如何多选批量添加？</dt>
        <dd>
          <ul class="help-list">
            <li><strong>状态筛选</strong>：分类栏下方提供「全部 / 未拥有 / 已拥有」切换，点击「未拥有」可直接查看当前存档还未收集的所有装备，分类标签上的数字也会实时同步为未收集数量。</li>
            <li><strong>批量选择模式</strong>：点击工具栏的「批量选择」按钮进入多选模式，直接点击多张物品卡片进行勾选，在底部浮动操作条中选择存放位置后，点击「一键获得」即可逐件写入存档。个别物品失败（如数量超过上限）不影响其他物品，失败的物品和原因会逐条列出，并保留在多选里，可以直接再点「一键获得」重试。</li>
          </ul>
        </dd>
        <dt>为什么获得不朽武器推荐选「放入储藏室」？升阶到 5++ 的机制是什么？</dt>
        <dd>
          这是由于《赛博朋克 2077》2.0+ 官方脚本的<strong>等级校准机制</strong>（<code>RescaleStashedIconicsToPlayerLevel</code>）：
          <ul class="help-list">
            <li>游戏机制规定：不朽武器升阶到 5++（传说++）需要角色达到 51 级以上并拥有《往日之影》，且武器的掉落等级必须经过游戏引擎校准。</li>
            <li>直接放入背包的注入武器缺少掉落时的时间戳校准，在工坊升到 5 阶后会无法继续升级到 5++。</li>
            <li><strong>解决方案</strong>：选择<strong>「放入储藏室」</strong>，修改后进游戏从载具后备箱或公寓储藏点将武器取出，游戏原生脚本就会自动触发等级校准，使其完美解锁 5++ 升阶！</li>
            <li>如果存档里还没有储藏室记录（从没往载具后备箱或公寓储藏处放过东西），「放入储藏室」会暂时不可选；在游戏里往储藏处放一件东西并存档后即可使用。</li>
          </ul>
        </dd>
        <dt>我们的设计理念：为什么没有载具、任务物品、技能点？</dt>
        <dd>
          这是本工具核心的<strong>「极致安全、免注入、防坏档」</strong>设计理念：
          <ul class="help-list">
            <li><strong>载具（车辆/摩托）</strong>：游戏底层载具归属于独立状态机系统，强行在存档注入极易破坏召唤逻辑导致坏档。最安全、符合游戏规则的做法是：在物品库最后一栏「货币」里给背包添加任意欧元，然后在游戏内「好车中间人」终端全款买齐，体验原汁原味的提车仪式感且绝不坏档。</li>
            <li><strong>任务剧情物品</strong>：主线特定芯片、超梦头环等带有任务脚本锁，强行注入背包会导致剧情触发紊乱或主线卡关。为了保护玩家百小时存档安全，工具特意严格过滤。</li>
            <li><strong>技能点 / 属性点</strong>：属于人物角色成长系统，本工具专注于打造安全、直观、即插即用的纯外部物品库，不碰复杂的角色内部状态。</li>
          </ul>
        </dd>
        <dt>物品库全吗？为什么有些物品搜不到？</dt>
        <dd>
          全网最全！工具在启动时<strong>实时动态扫描本机游戏官方 REDmod 数据库</strong>，完整收录 13 大类 3,900+ 件物品，包括全系不朽武器、不朽近战、全义体与高级图纸，一件不漏。若搜不到请注意：
          <ul class="help-list">
            <li><strong>搜官方全名</strong>：请使用官方标准中文名或简短关键词检索（例如搜「四季宝」、「寡妇制造者」）。</li>
            <li><strong>同名款式与品阶折叠</strong>：多品阶装备或同款多涂装（原版/霓虹/军用等）自动智能合并为同一张卡片，点击「获得」可在确认弹窗中自由切换款式与品阶（默认即为最高阶传说++）。</li>
            <li><strong>资料片专属隔离</strong>：未购买安装《往日之影》DLC 时，工具会自动隐藏资料片专属物品，防止生成后游戏识别异常。</li>
          </ul>
        </dd>
        <dt>核心理念：为什么不做数值修改（攻击力、无敌、秒杀等）？</dt>
        <dd>
          <strong>尊重游戏本体的游玩体验</strong>是本工具的核心立项宗旨：
          <ul class="help-list">
            <li><strong>纯净调取，拒绝魔改</strong>：本工具只严格按官方数据结构规范，调取游戏本体原生存在的装备、外观与资源，绝不修改武器攻击力、暴击率、角色血量或无敌秒杀等底层数值。</li>
            <li><strong>保留探索乐趣</strong>：我们希望帮助玩家轻松获取心仪的外观搭配、补齐错过的标志性收藏，同时最大程度保留夜之城的战斗平衡、build 构筑乐趣与原汁原味的沉浸感。</li>
          </ul>
        </dd>
      </dl>`,
    about: `
      <div class="help-about">
        <h3>游戏工具 · Cyberpunk 2077 物品库</h3>
        <span id="help-version" class="help-version">v</span>
      </div>
      <h4>作者</h4>
      <div class="help-author">
        <span>作者：<strong>魔法苦茶籽</strong></span>
        <button class="ghost small" type="button" data-author-open>B 站主页</button>
      </div>
      <p class="help-note">本工具免费。游戏更新后需要等待工具适配，最新版本与更新请关注作者 B 站动态。</p>
      <h4>运行环境</h4>
      <ul id="help-env" class="help-list">
        <li>Windows 10/11 64 位</li>
        <li>Microsoft Edge WebView2 运行时</li>
        <li>游戏 2.31 / 2.5 版本存档（有没有往日之影都可以）</li>
        <li>官方免费 DLC REDmod</li>
      </ul>
      <h4>声明</h4>
      <ul class="help-list">
        <li>本工具只修改存档文件，不注入、不修改游戏进程，也不改动游戏目录。</li>
        <li>物品名称、分类与图标均在运行时从你本机的游戏文件读取，本工具不附带任何游戏素材。</li>
        <li>非官方工具，与 CD PROJEKT RED 无关。仅供单机游戏使用。</li>
        <li>修改存档有风险，建议重要存档先加入备份区。</li>
      </ul>`
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
        <dd>可以。工具會偵測遊戲目錄：沒有自由幻局時，物品庫只列本體物品，資料片的物品不顯示、也不會生成。設定裡遊戲目錄一行會顯示偵測結果。</dd>
        <dt>存檔顯示「不支援」？</dt>
        <dd>分兩種情況：
          <ul class="help-list">
            <li><strong>舊版本的存檔</strong>：在遊戲裡讀取後重新存檔即可。</li>
            <li><strong>遊戲更新後的新版本存檔</strong>：工具需要適配新版本，暫時不能修改（這是保護存檔的安全設計）。請關注作者 B 站獲取更新。</li>
          </ul>
        </dd>
        <dt>怎麼備份和還原？</dt>
        <dd>選取存檔後點「加入備份區」；在「備份區」頁籤裡可以查看、還原或刪除備份。獲得物品不會自動備份。</dd>
        <dt>會不會把存檔改壞？</dt>
        <dd>每次寫入前都會逐位元組校驗：除了新增或修改的物品，其他內容只要有任何變化就放棄寫入，原存檔保持不變。</dd>
        <dt>如何查看已擁有/未擁有的物品？如何多選批次新增？</dt>
        <dd>
          <ul class="help-list">
            <li><strong>狀態篩選</strong>：分類列下方提供「全部 / 未擁有 / 已擁有」切換，點擊「未擁有」可直接查看目前存檔尚未收集的所有裝備，分類標籤上的數字也會即時同步為未收集數量。</li>
            <li><strong>批次選擇模式</strong>：點擊工具列的「批次選擇」按鈕進入多選模式，直接點選多張物品卡片進行勾選，在底部浮動操作列中選擇存放位置後，點擊「一鍵獲得」即可逐件寫入存檔。個別物品失敗（如數量超過上限）不影響其他物品，失敗的物品和原因會逐條列出，並保留在多選裡，可以直接再點「一鍵獲得」重試。</li>
          </ul>
        </dd>
        <dt>為什麼獲得不朽武器推薦選「放入儲藏室」？升階到 5++ 的機制是什麼？</dt>
        <dd>
          這是由於《賽博朋克 2077》2.0+ 官方腳本的<strong>等級校準機制</strong>（<code>RescaleStashedIconicsToPlayerLevel</code>）：
          <ul class="help-list">
            <li>遊戲機制規定：不朽武器升階到 5++（傳說++）需要角色達到 51 級以上並擁有《自由幻局》，且武器的掉落等級必須經過遊戲引擎校準。</li>
            <li>直接放入背包的注入武器缺少掉落時的時間戳校準，在工坊升到 5 階後會無法繼續升級到 5++。</li>
            <li><strong>解決方案</strong>：選擇<strong>「放入儲藏室」</strong>，修改後進遊戲從載具後備箱或公寓儲藏點將武器取出，遊戲原生腳本就會自動觸發等級校準，使其完美解鎖 5++ 升階！</li>
            <li>如果存檔裡還沒有儲藏室記錄（從沒往載具後備箱或公寓儲藏處放過東西），「放入儲藏室」會暫時無法選擇；在遊戲裡往儲藏處放一件東西並存檔後即可使用。</li>
          </ul>
        </dd>
        <dt>我們的設計理念：為什麼沒有載具、任務物品、技能點？</dt>
        <dd>
          這是本工具核心的<strong>「極致安全、免注入、防壞檔」</strong>設計理念：
          <ul class="help-list">
            <li><strong>載具（汽車／機車）</strong>：遊戲底層的載具屬於獨立的狀態機系統，強行注入存檔極易破壞召喚邏輯而導致壞檔。最安全、也符合遊戲規則的做法是：在物品庫最後一欄「貨幣」裡給背包新增任意歐元，再到遊戲內的「好車中間人」（Autofixer）終端全額購買，體驗原汁原味的提車儀式感，而且絕不壞檔。</li>
            <li><strong>任務劇情物品</strong>：主線的特定晶片、超夢頭環等帶有任務腳本鎖，強行注入背包會導致劇情觸發錯亂或主線卡關。為了保護玩家上百小時的存檔，工具特意嚴格過濾。</li>
            <li><strong>技能點／屬性點</strong>：屬於角色成長系統，本工具專注於打造安全、直觀、隨插即用的純外部物品庫，不碰複雜的角色內部狀態。</li>
          </ul>
        </dd>
        <dt>物品庫齊全嗎？為什麼有些物品搜不到？</dt>
        <dd>
          全網最全！工具在啟動時<strong>即時掃描本機遊戲的官方 REDmod 資料</strong>，完整收錄 13 大類 3,900+ 件物品，包括全系列不朽武器、不朽近戰武器、全部改造裝置與高級製作規範，一件不漏。若搜不到請注意：
          <ul class="help-list">
            <li><strong>搜尋正式名稱</strong>：請使用遊戲內的正式名稱或簡短關鍵字搜尋。</li>
            <li><strong>同名款式與品階合併</strong>：多品階裝備或同款多塗裝（原版／霓虹／軍用等）會自動合併為同一張卡片，點「獲得」後可在確認視窗中自由切換款式與品階（預設即為最高階傳說++）。</li>
            <li><strong>資料片專屬物品</strong>：沒有安裝《自由幻局》時，工具會自動隱藏資料片專屬物品，避免生成後遊戲無法識別。</li>
          </ul>
        </dd>
        <dt>核心理念：為什麼不做數值修改（攻擊力、無敵、秒殺等）？</dt>
        <dd>
          <strong>尊重遊戲本體的遊玩體驗</strong>是本工具的核心宗旨：
          <ul class="help-list">
            <li><strong>只取原生物品，不魔改</strong>：本工具嚴格按照官方資料結構，只取用遊戲本體原生存在的裝備、外觀與資源，絕不修改武器攻擊力、暴擊率、角色血量或無敵、秒殺等底層數值。</li>
            <li><strong>保留探索樂趣</strong>：我們希望幫助玩家輕鬆取得心儀的外觀搭配、補齊錯過的標誌性收藏，同時最大程度保留夜之城的戰鬥平衡、Build 構築樂趣與原汁原味的沉浸感。</li>
          </ul>
        </dd>
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
        <dd>Yes. The tool checks your game folder: without Phantom Liberty, the item library lists only base-game items, and expansion items are neither shown nor generated. The Game Directory row in the settings shows what was detected.</dd>
        <dt>Save slot shows "Unsupported"?</dt>
        <dd>
          <ul class="help-list">
            <li><strong>Older game save</strong>: Load and re-save it once in game (2.31 / 2.5) to upgrade the format.</li>
            <li><strong>Newer game update</strong>: Wait for a tool update (safety lock to protect your save).</li>
          </ul>
        </dd>
        <dt>How do I back up and restore?</dt>
        <dd>Select a save and click "Add Backup"; in the "Backups" tab you can view, restore or delete backups. Obtaining items does not create a backup automatically.</dd>
        <dt>Will this corrupt my save file?</dt>
        <dd>Every write is verified byte by byte first: if anything other than the items being added or changed would differ, the write is abandoned and the original save stays unchanged.</dd>
        <dt>How do I view owned / unowned items? How do I batch add multiple items?</dt>
        <dd>
          <ul class="help-list">
            <li><strong>Ownership Filter</strong>: Switch between "All / Unowned / Owned" below the category bar. Clicking "Unowned" displays all equipment not yet collected in this save, and category counts will dynamically show uncollected amounts.</li>
            <li><strong>Batch Mode</strong>: Click "Batch Select" in the toolbar to enter multi-select mode. Click multiple item cards to check them, configure destination in the floating bottom bar, and click "Obtain" to write them into the save one by one. If an item fails (for example, it would exceed the quantity cap), the others are still written; failed items are listed with the reason and stay selected, so you can click "Obtain" again to retry.</li>
          </ul>
        </dd>
        <dt>Why is "Stash" recommended for Iconic weapons? How does Tier 5++ upgrading work?</dt>
        <dd>
          This is due to Cyberpunk 2077's native script <strong>level scaling mechanism</strong> (<code>RescaleStashedIconicsToPlayerLevel</code>):
          <ul class="help-list">
            <li>Under REDengine 2.0+, upgrading iconic weapons to Tier 5++ requires Phantom Liberty, player level 51+, and engine-calibrated loot tier data.</li>
            <li>Injected iconic weapons directly placed into personal backpack lack the game's drop loot calibration, capping manual crafting upgrades at Tier 5.</li>
            <li><strong>Solution</strong>: Choose <strong>"Stash (Vehicle Trunk / Apartment)"</strong>. Upon retrieving the weapon from your car trunk or apartment stash in game, the game's native script triggers level calibration, unlocking upgrades up to Tier 5++!</li>
            <li>If the save has no stash record yet (nothing has ever been put into a vehicle trunk or apartment stash), the Stash option is unavailable; put any item into a stash in game and save, and it becomes available.</li>
          </ul>
        </dd>
        <dt>Design philosophy: why no vehicles, quest items or skill points?</dt>
        <dd>
          The tool is built around one principle: <strong>maximum safety, no injection, no broken saves</strong>.
          <ul class="help-list">
            <li><strong>Vehicles (cars / bikes)</strong>: vehicle ownership lives in a separate state-machine system, and forcing vehicles into a save can easily break summoning and corrupt the save. The safe way that also follows the game's rules: add as many eurodollars as you like from the last category, "Currency", then buy the vehicle outright at an Autofixer terminal in game. You get the full delivery experience with zero risk.</li>
            <li><strong>Quest items</strong>: main-quest shards, braindance wreaths and similar items carry quest-script locks; injecting them can scramble quest triggers or block the main story. To protect saves with hundreds of hours in them, the tool deliberately filters them out.</li>
            <li><strong>Perk / attribute points</strong>: these belong to the character progression system. The tool focuses on being a safe, straightforward, plug-and-play item library and does not touch complex character state.</li>
          </ul>
        </dd>
        <dt>Is the item library complete? Why can't I find some items?</dt>
        <dd>
          As complete as it gets: at startup the tool <strong>scans the official REDmod data on your PC</strong> and lists 3,900+ items in 13 categories, including every iconic weapon, iconic melee weapon, all cyberware and advanced crafting specs. If you can't find something:
          <ul class="help-list">
            <li><strong>Search the official name</strong>: use the item's official in-game name or a short keyword (for example "Skippy" or "Widow Maker").</li>
            <li><strong>Variants and tiers are merged</strong>: items with several tiers or paint variants (original / neon / military, etc.) share one card; click "Get" and pick the variant and tier in the dialog (the highest tier, Legendary++, is selected by default).</li>
            <li><strong>Expansion items</strong>: without Phantom Liberty installed, expansion-only items are hidden so the game never receives items it cannot recognize.</li>
          </ul>
        </dd>
        <dt>Why no stat edits (damage, god mode, one-hit kills)?</dt>
        <dd>
          The tool is built to <strong>respect the base game's experience</strong>:
          <ul class="help-list">
            <li><strong>Native items only, no rebalancing</strong>: following the official data structures, the tool only adds equipment, outfits and resources that exist in the base game. It never changes weapon damage, crit chance, health, or anything like god mode or one-hit kills.</li>
            <li><strong>Keep the fun of exploring</strong>: the aim is to help you get the outfits you like and the iconic items you missed, while keeping Night City's combat balance, build crafting and immersion intact.</li>
          </ul>
        </dd>
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
        <li>Editing saves carries risk; add important saves to the backups first.</li>
      </ul>`
  }
};
