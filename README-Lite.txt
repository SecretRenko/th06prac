th06ncprac · 东方红魔乡：新典 离线练习控制器 · 轻量版（Lite）
构建时间：2026-09-26

与完整便携版的区别
- 本包不含 .NET 运行时（完整便携版自带约 171 MB 的 runtime 目录），总体积约 0.24 MB。
- 因此运行本包需要目标机器已安装 .NET 10 Desktop Runtime (x64)。
  检测方法：命令行执行 dotnet --list-runtimes，应能看到 Microsoft.WindowsDesktop.App 10.x。
  下载地址：https://dotnet.microsoft.com/download/dotnet/10.0 （选 Desktop Runtime，x64）
- 未安装时 Start-Practice.bat 会提示；也可改用完整便携版 th06ncprac-Portable-*（自带运行时，无需安装任何东西）。

使用方法
1. 本文件夹要与 th06nc.exe 所在目录相邻——当前布局已经是：..\th06nc.exe。
2. 双击 Start-Practice.bat。
3. 后续操作与完整版完全一致：启动/连接游戏 → 进 Practice 关卡菜单按第一次 Z 弹出开局设置 → 确认 → 回游戏按第二次 Z。

目录说明
- dist\      控制台核心，运行记录写入 dist\logs
- dist-ui\   图形界面
- config\    entrances.json（Boss 入口表）、settings.json（开局默认值）、game-target.json（游戏路径）
- config\ 和 dist\config\ 都要保留：核心按自身目录读取 dist\config\entrances.json。

版本要求
只支持 SHA-256 为 07850C8C6E469C0E82C13423E6D0D096A88D693455BDACACBB44C0AA3BCCE473 的 th06nc.exe（与完整版一致）。

界面
已包含 2026-09-26 的界面修改：黑白灰 + 浅蓝配色、大标题容器加高、「打开日志目录」按钮不再被裁。
