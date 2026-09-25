东方红魔乡：新典 离线练习控制器（便携版）

使用方法
1. 将 XinDianPrac-Portable 文件夹放在游戏根目录内，与 th06nc.exe 所在目录相邻。例如：
   游戏根目录\th06nc.exe
   游戏根目录\XinDianPrac-Portable\Start-Practice.bat
2. 双击 Start-Practice.bat。控制器会自动连接同目录的 th06nc.exe；若游戏已运行，也会尝试连接该进程。
3. 若游戏放在其他目录，点击“选择游戏程序”并选择要连接的 th06nc.exe，再点击“启动/连接游戏”。
4. 进入 Practice 关卡菜单后，按第一次 Z 显示开局设置；确认后按第二次 Z 开始。

版本要求
只支持 SHA-256 为以下值的 th06nc.exe：
07850C8C6E469C0E82C13423E6D0D096A88D693455BDACACBB44C0AA3BCCE473
不同版本会被拒绝连接，因为内存地址尚未验证兼容。

安全边界
控制器只读游戏文件并校验 SHA-256，练习时对选定游戏进程内存读写分数、资源和 Boss 时间线。它不修改 th06nc.exe 或其他游戏文件，也不附带游戏程序或存档。
本便携包面向 Windows x64，包含构建时使用的 .NET 10 与 Windows Desktop 运行环境，无需另外安装 .NET。

配置与日志
config\settings.json 保存确认窗口的默认分数与资源。
dist\logs 保存每次进关、重开和 Boss 跳转的记录。
可将整个控制器文件夹移到其他游戏根目录；若仍指向旧位置，在设置目录旁的 config\game-target.json 中将 gamePath 改为 ..\th06nc.exe，或在界面重新选择游戏程序。
