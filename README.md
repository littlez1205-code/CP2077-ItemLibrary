# 赛博朋克 2077 物品库（CP2077-ItemLibrary）

在游戏外修改《赛博朋克 2077》的存档，把想要的物品放进背包：按类型列出全部可获得物品，直接点击物品卡片确认获得，回游戏读档就有了。

- **只改存档**：不注入、不修改游戏进程，也不改动游戏目录。
- **中文名 + 原生图标 + 完整属性详情**：物品名称、分类、原生图标、武器核心属性（武器类型、单发伤害、射速/攻速、弹丸数、弹匣容量、装弹时间）、义体容量、特殊效果词条与背景说明（Lore）均在运行时从你本机的游戏文件与官方数据表读取，本项目不附带任何游戏素材。
- **写入前逐字节校验**：除了新增或修改的物品，存档其他内容只要有任何变化就放弃写入，原存档保持不变。
- **完全离线**：不联网、不收集任何数据。
- 覆盖服装、远程 / 近战武器（含标志性武器）、手雷、义体、快速破解、模组、消耗品、弹药、材料、配方、钱。

> 作者：**魔法苦茶籽** · B 站：<https://space.bilibili.com/3546366456367644>（更新消息在 B 站动态发布）

## 下载与使用

到本仓库的 [Releases](../../releases) 下载 `GameTool-CP2077-v<版本>-win-x64.zip`，解压后双击「赛博朋克2077物品库.exe」。压缩包内附《使用说明.txt》。

**系统要求**

- Windows 10 / 11（64 位），Microsoft Edge WebView2 运行时（Windows 11 自带）
- 游戏版本 **2.31 / 2.5** 的存档；往日之影可有可无（没有时只显示本体物品）
- 官方免费 DLC **REDmod**（Steam / GOG / Epic 均可获取），物品名称与属性表来自它

**使用步骤**

1. 第一次打开时选择存档目录（通常是 `%USERPROFILE%\Saved Games\CD Projekt Red\Cyberpunk 2077`）和游戏目录。
2. 选中要修改的存档，建议先点「加入备份区」。
3. 搜索或按分类找到物品，直接点击整张物品卡片并在弹窗中确认获得。
4. 回到游戏读取这个存档。

首次运行如果出现「Windows 已保护你的电脑」，点「更多信息」→「仍要运行」：这是个人作品，没有购买商业代码签名。

## 从源码构建

需要 [.NET 10 SDK](https://dotnet.microsoft.com/)（见 `global.json`）。

```powershell
dotnet build src/GameTool.slnx -c Release
dotnet test src/GameTool.slnx -c Release --no-build
# 发布包（自包含单文件 + 使用说明 + SHA-256），输出到 artifacts/release/
powershell -NoProfile -ExecutionPolicy Bypass -File src\release\package-release.ps1
```

## 目录结构

| 目录 | 内容 |
|---|---|
| `src/GameTool.Core` | 与具体游戏无关的内核：适配器接口、补丁引擎、严格校验、存档监听 |
| `src/GameTool.Adapters.Cyberpunk2077` | 2077 存档容器（LZ4 分块）、节点表、背包与物品结构、物品生成、名称与图标读取 |
| `src/GameTool.App` | 应用服务层与桌面端消息桥 |
| `src/GameTool.Desktop` | WPF + WebView2 桌面端（界面在 `wwwroot/`） |
| `src/GameTool.Cli` | 命令行工具（开发与研究用） |
| `src/GameTool.Tests` | 单元测试 |
| `src/release` | 打包脚本与使用说明 |
| `docs/formats` | 存档格式说明（Kaitai Struct） |

## 声明

- 非官方工具，与 CD PROJEKT RED 无关。《赛博朋克 2077》及相关名称、素材归其权利人所有。
- 仅供单机游戏使用。修改存档有风险，请自行备份，后果自负。
- 本工具免费，禁止倒卖。

## 许可证

[GPL-3.0](LICENSE)。你可以使用、修改和再分发本项目，但分发修改版时必须同样以 GPL-3.0 开源，并保留原作者署名。

附加许可（GPL-3.0 第 7 条）：本程序运行时会加载用户本机游戏目录中自带的 Oodle 解压库（`oo2ext`）来读取游戏文本。作为版权持有人，作者允许本程序及其修改版这样做，该库不随本项目分发，也不受本许可证约束。
