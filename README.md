# th06ncprac

一个用于《东方红魔乡：新典》（Touhou Koumakyou: New Classic）的简易练习辅助工具。

本项目的定位很简单：**在 thprac 尚未正式适配《红魔乡：新典》期间，提供一个只包含常用练习功能的临时替代品。**

它并不试图完整复刻 thprac，也不是 thprac 的官方移植或分支。

## 功能

目前支持：

- Practice 开局自定义
  - 分数
  - 残机
  - Bomb
  - Power
- Stage 1–6 Boss 跳转
- 使用游戏原生 `R` 快速重开后自动恢复资源设置
- 自动读取游戏中选择的难度、角色和机体
- 自动连接 `th06nc.exe`

本工具不会修改游戏文件，仅在运行期间读写游戏进程内存。

## 下载

见 [Releases](https://github.com/SecretRenko/th06prac/releases) 页面。

| 版本 | 体积 | 说明 |
| --- | ---: | --- |
| **Lite** | 约 0.24 MB | 需要自备 .NET 10 Desktop Runtime (x64) |
| **Portable** | 约 171 MB | 自带运行时，无需安装任何东西 |

两种版本都解压到游戏根目录、与 `th06nc.exe` 相邻，双击 `Start-Practice.bat` 启动。

## 使用

1. 启动《东方红魔乡：新典》。
2. 运行本工具。
3. 在游戏中进入 `Practice` 并选择需要练习的关卡。
4. 第一次按 `Z` 进入关卡时，会弹出资源设置窗口。
5. 设置分数、残机、Bomb、Power，以及是否直接进入 Boss。
6. 返回游戏并继续，即可按照指定资源开始练习。

之后在暂停菜单使用游戏自带的 `R` 重开时，本工具会自动恢复当前练习设置。

## 当前限制

- 暂不支持任意道中节点跳转。
- 暂不支持单独选择 Boss 非符 / 符卡入口。
- Extra 尚未接入。
- Boss 跳转仍属于简单实现，不能保证所有背景、BGM 和前置状态都与正常流程完全一致。
- 游戏更新后内存地址可能发生变化，因此新版本可能需要重新适配。目前只对接 SHA-256 为 `07850C8C6E469C0E82C13423E6D0D096A88D693455BDACACBB44C0AA3BCCE473` 的 `th06nc.exe`，版本不符时工具会拒绝连接。

如果你需要完整的高级练习功能，请优先关注 thprac 后续对《红魔乡：新典》的正式支持。

## 运行环境

- Windows x64
- 《东方红魔乡：新典》
- Lite 版本需要 `.NET 10 Desktop Runtime (x64)`
- Portable 版本包含运行时，无需额外安装 .NET

## 作者与致谢

- **作者**：SecretRenko（[GitHub](https://github.com/SecretRenko)）
- **AI 辅助**：本项目的代码编写、逆向分析与文档整理过程中，使用了 **ChatGPT**（OpenAI）与 **DeepSeek**（深度求索）作为辅助工具。
- **逆向工程资料**：感谢 **Renko6626**（[GitHub](https://github.com/Renko6626)）提供的《东方红魔乡》逆向工程资料，为本项目的内存结构分析和功能实现提供了重要参考。
- **生态项目**：同时感谢 **thprac** 项目及其开发者长期以来为东方 Project 练习工具生态所做的工作。

## Disclaimer

本项目为非官方第三方工具，与上海爱丽丝幻乐团及 thprac 项目无隶属关系。
