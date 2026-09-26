th06ncprac · 东方红魔乡：新典 离线练习控制器 · 轻量版（Lite）
构建时间：2026-09-26

成绩与 Replay 提示
- 本工具目前不能生成或保存游戏 Replay。
- 使用本工具修改开局分数、资源或跳转 Boss 后打出的成绩，可能不被游戏、排行榜或社区的成绩审核规则承认。提交正式成绩前请先确认相关规定。
- 与 thprac 在其支持游戏中的功能相比，本工具只提供新典的部分 Practice 辅助；不支持任意道中段落/符卡入口、Replay、游戏内快捷菜单或其他游戏。
- 本工具是 thprac 尚未适配新典期间的临时过渡方案。Replay 需要适配新典的记录格式和播放流程并验证可重现性，目前未完成，因此不生成 Replay，也不保证成绩可认证。thprac 正式支持后建议改用 thprac。

与完整便携版的区别
- 本包不含 .NET 运行时（完整便携版自带约 171 MB 的 runtime 目录）。
- 因此运行本包需要目标机器已安装 .NET 10 Desktop Runtime (x64)。
  检测方法：命令行执行 dotnet --list-runtimes，应能看到 Microsoft.WindowsDesktop.App 10.x。
  下载地址：https://dotnet.microsoft.com/download/dotnet/10.0 （选 Desktop Runtime，x64）
- 未安装时 Start-Practice.bat 会提示；也可改用完整便携版 th06ncprac-Portable-*（自带运行时，无需安装任何东西）。

使用方法
1. 本文件夹要与 th06nc.exe 所在目录相邻——当前布局已经是：..\th06nc.exe。
2. 双击本文件夹最外层的 th06ncprac.exe，可直接打开图形界面而不弹出终端窗口。
3. 后续操作与完整版完全一致：启动/连接游戏 → 进 Practice 关卡菜单按第一次 Z 弹出开局设置 → 确认 → 回游戏按第二次 Z。

目录说明
- dist\      控制台核心，运行记录写入 dist\logs
- th06ncprac.exe   图形界面启动入口
- config\    entrances.json（Boss 入口表）、settings.json（开局默认值）、game-target.json（游戏路径）
- config\ 和 dist\config\ 都要保留：核心按自身目录读取 dist\config\entrances.json。

版本兼容性
控制器不按 SHA-256 拒绝游戏版本。目前识别已研究的旧版与 Steam 版两种内存布局；遇到未知布局会停止连接，避免向未经定位的地址写入。Steam 版已由用户确认开局弹窗、Boss 跳转及 R 重开后资源保持正常；其他组合尚未逐一实测。

界面
已包含 2026-09-26 的界面修改：黑白灰 + 浅蓝配色、大标题容器加高、「打开日志目录」按钮不再被裁。
