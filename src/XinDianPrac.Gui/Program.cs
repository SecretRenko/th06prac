using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace XinDianPrac.Gui;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        var gamePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            File.Exists(Path.Combine(AppContext.BaseDirectory, "config", "entrances.json")) ? ".." : "..\\..", "th06nc.exe"));
        var identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(gamePath.ToUpperInvariant())));
        using var singleInstance = new Mutex(true, $@"Local\XinDianPrac-{identity}", out var firstInstance);
        if (!firstInstance)
        {
            MessageBox.Show("这个游戏已有一个练习控制器窗口，请使用已打开的窗口。", "练习控制器已运行");
            return;
        }
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}

internal sealed class MainForm : Form
{
    private const int BaselineDataRva = 0x323000;
    private int menuRva = 0x50A0D0;
    private int modeRva = 0xC21D9C;
    private int stageRva = 0x4F1E84;
    private int cursorRva = 0xC07268;
    private readonly string workspace = ResolveWorkspace();
    private string? configuredGamePath;
    private readonly Label status = new();
    private readonly Label modeHint = new();
    private readonly NumericUpDown lives = Number(9);
    private readonly NumericUpDown bombs = Number(9);
    private readonly NumericUpDown power = Number(128);
    private readonly NumericUpDown score = Number(999999990);
    private readonly ComboBox stage = new();
    private readonly RadioButton wholeStage = new();
    private readonly RadioButton boss = new();
    private readonly Button startMonitor = Button("开始监视", true);
    private readonly Button stopMonitor = Button("停止监视");
    private readonly Button launch = Button("启动/连接游戏", true);
    private readonly Button chooseGame = Button("选择游戏程序");
    private readonly Button fullUnlock = Button("全开档");
    private readonly Button probe = Button("读取当前状态");
    private readonly RichTextBox log = new();
    private readonly System.Windows.Forms.Timer statusTimer = new() { Interval = 2000 };
    private readonly System.Windows.Forms.Timer readyTimer = new() { Interval = 80 };
    private Process? monitor;
    private bool commandBusy;
    private nint gameMemory;
    private nint gameBase;
    private int gamePid;
    private bool readyPrompted;
    private bool practiceStageMenuSeen;
    private DateTime practiceStageMenuSeenAt;
    private int practiceSelectedStageIndex = -1;
    private DateTime readyMenuSince;
    private string? hashLoggedPath;
    private ReadySelection? pendingStart;
    private DateTime menuReturnSince;
    private (int Menu, int Mode, int Stage, int Cursor)? lastReadyState;
    private sealed record ReadySelection(int Stage, int Score, byte Lives, byte Bombs, ushort Power, bool Boss);

    private string GameExe => ResolveGameExe();
    private string CoreExe => Path.Combine(workspace, "dist", "XinDianPrac.exe");
    private string CoreDll => Path.Combine(workspace, "dist", "XinDianPrac.dll");
    private string BundledDotnet => Path.Combine(workspace, "runtime", "dotnet.exe");
    private string SettingsPath => Path.Combine(workspace, "config", "settings.json");
    private string GameTargetPath => Path.Combine(workspace, "config", "game-target.json");

    private string ResolveGameExe()
    {
        if (!string.IsNullOrWhiteSpace(configuredGamePath))
        {
            var configured = Path.GetFullPath(Path.IsPathRooted(configuredGamePath)
                ? configuredGamePath : Path.Combine(workspace, configuredGamePath));
            if (File.Exists(configured)) return configured;
        }
        var sibling = Path.GetFullPath(Path.Combine(workspace, "..", "th06nc.exe"));
        if (File.Exists(sibling)) return sibling;
        return Path.Combine(workspace, "isolated-game", "th06nc.exe");
    }

    private void LoadGameTarget()
    {
        try
        {
            if (File.Exists(GameTargetPath))
                configuredGamePath = JsonDocument.Parse(File.ReadAllText(GameTargetPath)).RootElement.GetProperty("gamePath").GetString();
        }
        catch (Exception ex) { WriteLog($"读取游戏路径设置失败：{ex.Message}"); }
    }

    private void SaveGameTarget(string fullPath)
    {
        var gameRoot = Path.GetFullPath(Path.Combine(workspace, ".."));
        var normalized = Path.GetFullPath(fullPath);
        var rootPrefix = gameRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        configuredGamePath = normalized.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase)
            ? Path.GetRelativePath(workspace, normalized) : normalized;
        hashLoggedPath = normalized;
        Directory.CreateDirectory(Path.GetDirectoryName(GameTargetPath)!);
        File.WriteAllText(GameTargetPath, JsonSerializer.Serialize(new { gamePath = configuredGamePath }, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
        if (gameMemory != 0) { CloseHandle(gameMemory); gameMemory = 0; }
        var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(normalized)));
        WriteLog($"已选择游戏：{normalized}；SHA-256={hash}（仅记录，不限制版本）。");
        RefreshStatus();
    }

    private static string ResolveWorkspace()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        for (var depth = 0; depth < 8 && directory is not null; depth++, directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "config", "entrances.json")) &&
                (File.Exists(Path.Combine(directory.FullName, "dist", "XinDianPrac.exe")) ||
                 File.Exists(Path.Combine(directory.FullName, "dist", "XinDianPrac.dll"))))
                return directory.FullName;
        }
        throw new DirectoryNotFoundException(
            "找不到练习控制器工作目录。请从控制器目录内的 dist-ui\\ 启动本程序；" +
            "该目录需包含 config\\entrances.json 与 dist\\XinDianPrac.exe（或 dist\\XinDianPrac.dll）。");
    }

    private static Encoding PipeEncoding()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(936);
    }

    private static NumericUpDown Number(int max) => new()
    {
        Minimum = 0, Maximum = max, Width = 88, Font = new Font("Microsoft YaHei UI", 12),
        TextAlign = HorizontalAlignment.Center
    };

    [DllImport("kernel32.dll", SetLastError = true)] private static extern nint OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(nint handle);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool ReadProcessMemory(nint process, nint address, byte[] data, nuint size, out nuint count);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint window);

    private int ReadGameInt(int rva)
    {
        var data = new byte[4];
        return gameMemory != 0 && ReadProcessMemory(gameMemory, gameBase + rva, data, 4, out var read) && read == 4
            ? BitConverter.ToInt32(data) : -1;
    }

    private void AttachReadyWatcher()
    {
        if (gameMemory != 0) return;
        var target = GameExe;
        if (!File.Exists(target)) return;
        if (!string.Equals(hashLoggedPath, target, StringComparison.OrdinalIgnoreCase))
        {
            hashLoggedPath = target;
            var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(target)));
            WriteLog($"目标游戏 SHA-256：{hash}（仅记录，不限制版本）。");
        }
        var processes = Process.GetProcessesByName("th06nc");
        try
        {
            var matches = processes.Where(p =>
            {
                try { return string.Equals(p.MainModule?.FileName, GameExe, StringComparison.OrdinalIgnoreCase); }
                catch { return false; }
            }).ToArray();
            if (matches.Length != 1) return;
            (int Rva, int VirtualSize) dataSection;
            try { dataSection = ReadDataSection(target); }
            catch (Exception ex)
            {
                WriteLog($"无法定位游戏 .data 区段：{ex.Message}");
                return;
            }
            var module = matches[0].MainModule!;
            var steamLayout = dataSection is { Rva: 0x36D000, VirtualSize: 0x908884 };
            var oldLayout = dataSection is { Rva: 0x323000, VirtualSize: 0x906660 };
            if ((!steamLayout && !oldLayout) || module.ModuleMemorySize < dataSection.Rva + dataSection.VirtualSize)
            {
                WriteLog($"尚未定位此游戏的内存布局，无法读取菜单状态；.data RVA=0x{dataSection.Rva:X}，大小=0x{dataSection.VirtualSize:X}。");
                return;
            }
            menuRva = 0x50A0D0 + (steamLayout ? 0xFA0 : 0);
            modeRva = 0xC21D9C + (steamLayout ? 0x2220 : 0);
            stageRva = 0x4F1E84 + (steamLayout ? 0xC50 : 0);
            cursorRva = 0xC07268 + (steamLayout ? 0x2220 : 0);
            gamePid = matches[0].Id;
            gameBase = module.BaseAddress + dataSection.Rva - BaselineDataRva;
            gameMemory = OpenProcess(0x1010, false, gamePid);
            if (gameMemory == 0) WriteLog("无法读取游戏菜单状态；请检查控制器权限。");
            else WriteLog($"已连接关卡确认监视：PID {gamePid}；{(steamLayout ? "Steam" : "旧版")}布局；" +
                $"菜单={ReadGameInt(menuRva)}、模式={ReadGameInt(modeRva)}、关卡={ReadGameInt(stageRva)}、光标={ReadGameInt(cursorRva)}。");
        }
        finally { foreach (var process in processes) process.Dispose(); }
    }

    private static (int Rva, int VirtualSize) ReadDataSection(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);
        if (reader.ReadUInt16() != 0x5A4D) throw new InvalidOperationException("目标文件不是有效的 Windows EXE。");
        stream.Position = 0x3C;
        var peOffset = reader.ReadInt32();
        if (peOffset < 0 || peOffset > stream.Length - 24) throw new InvalidOperationException("EXE 的 PE 头位置无效。");
        stream.Position = peOffset;
        if (reader.ReadUInt32() != 0x00004550) throw new InvalidOperationException("EXE 的 PE 标记无效。");
        stream.Position = peOffset + 6;
        var sectionCount = reader.ReadUInt16();
        stream.Position = peOffset + 20;
        var optionalHeaderSize = reader.ReadUInt16();
        var sectionTable = checked((long)peOffset + 24 + optionalHeaderSize);
        for (var i = 0; i < sectionCount; i++)
        {
            stream.Position = sectionTable + i * 40L;
            var name = Encoding.ASCII.GetString(reader.ReadBytes(8)).TrimEnd('\0');
            var virtualSize = reader.ReadInt32();
            var virtualAddress = reader.ReadInt32();
            if (name == ".data") return (virtualAddress, virtualSize);
        }
        throw new InvalidOperationException("目标 EXE 不包含 .data 区段，无法定位游戏状态。");
    }

    private void ReadyTick()
    {
        if (gameMemory == 0) AttachReadyWatcher();
        if (gameMemory == 0) return;
        var menu = ReadGameInt(menuRva);
        var mode = ReadGameInt(modeRva);
        var stageIndex = ReadGameInt(stageRva);
        var cursor = ReadGameInt(cursorRva);
        var readyState = (Menu: menu, Mode: mode, Stage: stageIndex, Cursor: cursor);
        var stateChanged = readyState != lastReadyState;
        if (stateChanged)
        {
            if (mode == 1 || menu is 9 or 10)
                WriteLog($"关卡确认状态：菜单 {menu}、模式 {mode}、关卡 {stageIndex}、光标 {cursor}。");
            lastReadyState = readyState;
        }
        if (menu < 0 || mode < 0)
        {
            CloseHandle(gameMemory); gameMemory = 0; gamePid = 0; gameBase = 0;
            readyPrompted = false; practiceStageMenuSeen = false; practiceStageMenuSeenAt = default;
            practiceSelectedStageIndex = -1; readyMenuSince = default; pendingStart = null;
            return;
        }
        if (pendingStart is { } selected && mode == 2 && ReadGameInt(stageRva) == selected.Stage)
        {
            pendingStart = null;
            StartMonitoring(selected);
            return;
        }
        if (mode == 1 && menu == 9 && stageIndex == 0)
        {
            practiceStageMenuSeen = true;
            practiceStageMenuSeenAt = DateTime.UtcNow;
            if (cursor is >= 0 and <= 5) practiceSelectedStageIndex = cursor;
        }

        if (menu != 10)
        {
            readyPrompted = false;
            readyMenuSince = default;
            if (mode == 1 && menu == 9 && pendingStart is not null)
            {
                if (menuReturnSince == default) menuReturnSince = DateTime.UtcNow;
                if (DateTime.UtcNow - menuReturnSince > TimeSpan.FromSeconds(5))
                {
                    WriteLog("已返回关卡菜单，取消上次开局设置。");
                    pendingStart = null;
                }
            }
            else menuReturnSince = default;
            if (mode != 1 || (menu != 9 && DateTime.UtcNow - practiceStageMenuSeenAt > TimeSpan.FromSeconds(2)))
            {
                practiceStageMenuSeen = false;
                practiceSelectedStageIndex = -1;
            }
            return;
        }
        menuReturnSince = default;
        if (readyMenuSince == default) readyMenuSince = DateTime.UtcNow;
        if (DateTime.UtcNow - readyMenuSince < TimeSpan.FromMilliseconds(750)) return;
        if (!practiceStageMenuSeen || readyPrompted || monitor is { HasExited: false } || commandBusy || mode != 1 || stageIndex != 0) return;
        var index = practiceSelectedStageIndex;
        if (index is < 0 or > 5) return;
        readyPrompted = true;
        practiceStageMenuSeen = false;
        practiceSelectedStageIndex = -1;
        ShowReadyConfirmation(index + 1);
    }

    private static Button Button(string text, bool primary = false)
    {
        var font = new Font("Microsoft YaHei UI", 9);
        return new Button
        {
            Text = text, Width = Math.Max(132, TextRenderer.MeasureText(text, font).Width + 34), Height = 38,
            BackColor = primary ? Color.FromArgb(205, 229, 246) : Color.FromArgb(236, 238, 241),
            ForeColor = primary ? Color.FromArgb(20, 56, 82) : Color.FromArgb(42, 45, 50),
            FlatStyle = FlatStyle.Flat, Margin = new Padding(0, 0, 10, 0), Font = font,
            FlatAppearance = { BorderSize = 1, BorderColor = primary ? Color.FromArgb(146, 194, 226) : Color.FromArgb(205, 210, 216) }
        };
    }

    private static Label Label(string text, int width = 0) => new()
    {
        Text = text, AutoSize = width == 0, Width = width, Height = 32,
        TextAlign = ContentAlignment.MiddleLeft,
        Font = new Font("Microsoft YaHei UI", 9)
    };

    private static FlowLayoutPanel Row(params Control[] controls)
    {
        var row = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false, AutoScroll = false, Padding = new Padding(0, 3, 0, 0) };
        row.Controls.AddRange(controls);
        return row;
    }

    private static GroupBox Group(string title, Control content, int height)
    {
        var box = new GroupBox { Text = title, Dock = DockStyle.Top, Height = height,
            Padding = new Padding(14, 18, 14, 10), Font = new Font("Microsoft YaHei UI", 9, FontStyle.Bold),
            ForeColor = Color.FromArgb(56, 60, 66) };
        box.Controls.Add(content);
        return box;
    }

    public MainForm()
    {
        Text = "th06ncprac";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(760, 520);
        Size = new Size(900, 620);
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Color.FromArgb(255, 255, 255);
        Font = new Font("Microsoft YaHei UI", 9);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3,
            Padding = new Padding(22, 14, 22, 16) };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 84));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 145));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(root);

        var header = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 };
        header.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        header.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        header.Controls.Add(new Label { Text = "烘馍香新典prac", Dock = DockStyle.Fill, AutoSize = false,
            Font = new Font("Microsoft YaHei UI", 18, FontStyle.Bold), ForeColor = Color.FromArgb(28, 30, 34) }, 0, 0);
        status.Text = "正在检查游戏…";
        status.Dock = DockStyle.Fill;
        status.ForeColor = Color.FromArgb(120, 124, 130);
        header.Controls.Add(status, 0, 1);
        root.Controls.Add(header, 0, 0);

        var gamePanel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 };
        gamePanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        gamePanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        launch.Click += async (_, _) => await LaunchGameAsync();
        chooseGame.Click += (_, _) => ChooseGameFile();
        fullUnlock.Click += (_, _) => MessageBox.Show(this,
            "打开游戏内\"score-Lunatic记录查询\"，输入\"classiclager\"，即可全开档",
            "全开档", MessageBoxButtons.OK, MessageBoxIcon.Information);
        probe.Click += async (_, _) => await RunOnceAsync("probe");
        gamePanel.Controls.Add(Row(launch, chooseGame, fullUnlock, probe), 0, 0);
        gamePanel.Controls.Add(Label("选择/启动兼容的 th06nc.exe；Practice 菜单第一次 Z 后设置开局。"), 0, 1);
        root.Controls.Add(Group("游戏", gamePanel, 140), 0, 1);

        var logsPanel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 };
        logsPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        logsPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var openLogs = Button("打开日志目录");
        openLogs.Click += (_, _) =>
        {
            var folder = Path.Combine(workspace, "dist", "logs");
            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
        };
        logsPanel.Controls.Add(Row(openLogs, Label("停止监视只停止控制器；游戏保持运行。游戏内按 R 可重开当前练习。")), 0, 0);
        log.Dock = DockStyle.Fill;
        log.ReadOnly = true;
        log.BackColor = Color.White;
        log.ForeColor = Color.FromArgb(45, 48, 53);
        log.BorderStyle = BorderStyle.FixedSingle;
        log.Font = new Font("Consolas", 9);
        log.WordWrap = true;
        logsPanel.Controls.Add(log, 0, 1);
        var logsGroup = Group("运行记录", logsPanel, 300);
        logsGroup.Dock = DockStyle.Fill;
        root.Controls.Add(logsGroup, 0, 2);

        LoadGameTarget();
        if (!File.Exists(GameExe)) TryAdoptRunningGame();
        LoadSettings();
        LoadStages();
        UpdateHint();
        RefreshStatus();
        statusTimer.Tick += (_, _) => RefreshStatus();
        statusTimer.Start();
        readyTimer.Tick += (_, _) => ReadyTick();
        readyTimer.Start();
        FormClosing += (_, _) =>
        {
            readyTimer.Stop();
            if (gameMemory != 0) CloseHandle(gameMemory);
            StopMonitoring();
        };
        WriteLog("界面已启动。Practice 关卡菜单第一次 Z 后会弹出开局设置；确认后回到游戏按第二次 Z。");
    }

    private void LoadSettings()
    {
        try
        {
            using var json = JsonDocument.Parse(File.ReadAllText(SettingsPath));
            var data = json.RootElement;
            lives.Value = data.GetProperty("lives").GetInt32();
            bombs.Value = data.GetProperty("bombs").GetInt32();
            power.Value = data.GetProperty("power").GetInt32();
            if (data.TryGetProperty("score", out var savedScore)) score.Value = savedScore.GetInt32();
        }
        catch (Exception ex) { WriteLog($"读取资源设置失败：{ex.Message}"); }
    }

    private void LoadStages()
    {
        try
        {
            using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(workspace, "config", "entrances.json")));
            foreach (var item in json.RootElement.GetProperty("bossEntrances").EnumerateArray())
            {
                var number = item.GetProperty("stage").GetInt32();
                if (number <= 6) stage.Items.Add(new StageOption(number, item.GetProperty("bossName").GetString() ?? "Boss"));
            }
            if (stage.Items.Count > 0) stage.SelectedIndex = 0;
        }
        catch (Exception ex) { WriteLog($"读取 Boss 入口表失败：{ex.Message}"); }
    }

    private void SaveSettings()
    {
        try
        {
            var values = new { score = (int)score.Value, lives = (int)lives.Value, bombs = (int)bombs.Value, power = (int)power.Value };
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(values, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
            WriteLog($"开局设置已保存：{values.score} 分、{values.lives} 残、{values.bombs} Bomb、{values.power} Power。");
        }
        catch (Exception ex) { ShowError($"保存资源失败：{ex.Message}"); }
    }

    private void UpdateHint()
    {
        modeHint.Text = boss.Checked
            ? "Boss：跟随游戏内所选难度、角色与 A/B 机体；先在对应 Stage 开场暂停。"
            : "整面：在游戏中选择关卡并进入实际游玩画面，按 Esc 暂停后开始监视。";
    }

    private void ShowReadyConfirmation(int selectedStage)
    {
        using var dialog = new Form
        {
            Text = $"确认 Stage {selectedStage} 开局设置",
            StartPosition = FormStartPosition.CenterScreen,
            Size = new Size(480, 390), MinimumSize = new Size(460, 370),
            TopMost = true, BackColor = BackColor, Font = Font,
            FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false
        };
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 7,
            Padding = new Padding(22, 18, 22, 16) };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 125));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var row = 0; row < 7; row++) panel.RowStyles.Add(new RowStyle(SizeType.Absolute, row == 0 ? 48 : 43));
        dialog.Controls.Add(panel);
        panel.Controls.Add(new Label { Text = $"Stage {selectedStage} · 第二次 Z 开始", AutoSize = true,
            Font = new Font(Font.FontFamily, 13, FontStyle.Bold) }, 0, 0);
        panel.SetColumnSpan(panel.GetControlFromPosition(0, 0)!, 2);
        var scoreInput = Number(999999990); scoreInput.Value = score.Value; scoreInput.Increment = 10;
        scoreInput.ThousandsSeparator = true; scoreInput.Width = 180;
        var livesInput = Number(9); livesInput.Value = lives.Value;
        var bombsInput = Number(9); bombsInput.Value = bombs.Value;
        var powerInput = Number(128); powerInput.Value = power.Value;
        void AddField(int row, string name, Control input)
        {
            panel.Controls.Add(Label(name), 0, row);
            panel.Controls.Add(input, 1, row);
        }
        AddField(1, "初始分数", scoreInput);
        AddField(2, "残机", livesInput);
        AddField(3, "Bomb", bombsInput);
        AddField(4, "Power", powerInput);
        var bossInput = new CheckBox { Text = "直接跳转到本关 Boss 登场", AutoSize = true, Checked = boss.Checked };
        panel.Controls.Add(bossInput, 0, 5); panel.SetColumnSpan(bossInput, 2);
        var confirm = Button("确认设置", true); confirm.DialogResult = DialogResult.OK;
        var cancel = Button("取消（游戏按 Esc）"); cancel.DialogResult = DialogResult.Cancel;
        panel.Controls.Add(Row(confirm, cancel), 0, 6);
        panel.SetColumnSpan(panel.GetControlFromPosition(0, 6)!, 2);
        dialog.AcceptButton = confirm; dialog.CancelButton = cancel;
        WriteLog($"检测到 Stage {selectedStage} 的游戏内准备画面，等待确认开局设置。");
        var result = dialog.ShowDialog(this);
        if (result == DialogResult.OK && ReadGameInt(menuRva) == 10)
        {
            score.Value = scoreInput.Value; lives.Value = livesInput.Value;
            bombs.Value = bombsInput.Value; power.Value = powerInput.Value;
            boss.Checked = bossInput.Checked; wholeStage.Checked = !bossInput.Checked;
            pendingStart = new ReadySelection(selectedStage, (int)score.Value, (byte)lives.Value,
                (byte)bombs.Value, (ushort)power.Value, bossInput.Checked);
            SaveSettings();
            WriteLog($"Stage {selectedStage} 已确认。请回到游戏按第二次 Z 开始。");
        }
        else WriteLog("开局设置未确认。请在游戏准备画面按 Esc 返回关卡菜单。");
        try
        {
            using var process = Process.GetProcessById(gamePid);
            if (process.MainWindowHandle != 0) SetForegroundWindow(process.MainWindowHandle);
        }
        catch { /* The game may have exited while the dialog was open. */ }
    }

    private void RefreshStatus()
    {
        if (!File.Exists(GameExe)) { status.Text = "未找到 th06nc.exe。选择游戏程序，或把控制器放到游戏根目录中。"; return; }
        var processes = Process.GetProcessesByName("th06nc");
        try
        {
            var ids = processes.Where(p =>
            {
                try { return string.Equals(p.MainModule?.FileName, GameExe, StringComparison.OrdinalIgnoreCase); }
                catch { return false; }
            }).Select(p => p.Id).ToArray();
            status.Text = ids.Length switch
            {
                0 => $"游戏已选择：{Path.GetFileName(Path.GetDirectoryName(GameExe))} · 点击“启动/连接游戏”启动。",
                1 => $"游戏运行中 · PID {ids[0]}" + (monitor is { HasExited: false } ? " · 修改器已连接" : ""),
                _ => $"该路径有 {ids.Length} 个游戏进程；请保留一个。"
            };
        }
        finally { foreach (var p in processes) p.Dispose(); }
    }

    private async Task LaunchGameAsync()
    {
        if (commandBusy) return;
        try
        {
            if (!File.Exists(GameExe))
            {
                if (!TryAdoptRunningGame())
                {
                    ChooseGameFile();
                    if (!File.Exists(GameExe)) return;
                }
            }
            var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(GameExe)));
            WriteLog($"启动目标 SHA-256：{hash}（不进行版本拦截）。");
            using var process = Process.GetProcessesByName("th06nc").FirstOrDefault(p =>
            {
                try { return string.Equals(p.MainModule?.FileName, GameExe, StringComparison.OrdinalIgnoreCase); }
                catch { return false; }
            });
            if (process is not null) { WriteLog($"游戏已运行，修改器将连接该进程，PID {process.Id}。"); return; }
            Process.Start(new ProcessStartInfo(GameExe) { WorkingDirectory = Path.GetDirectoryName(GameExe)!, UseShellExecute = true });
            WriteLog("已启动游戏。控制器会监视其 Practice 关卡确认画面。");
            RefreshStatus();
        }
        catch (Exception ex) { ShowError(ex.Message); }
        finally { commandBusy = false; launch.Enabled = true; RefreshStatus(); }
    }

    private bool TryAdoptRunningGame()
    {
        var candidates = new List<string>();
        foreach (var process in Process.GetProcessesByName("th06nc"))
        {
            try
            {
                var path = process.MainModule?.FileName;
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                    candidates.Add(Path.GetFullPath(path));
            }
            catch { }
            finally { process.Dispose(); }
        }
        var unique = candidates.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (unique.Length == 1) { SaveGameTarget(unique[0]); return true; }
        if (unique.Length > 1) WriteLog("发现多个 th06nc.exe 进程，请用“选择游戏程序”指定要连接的游戏。");
        return false;
    }

    private void ChooseGameFile()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "选择要连接的 th06nc.exe",
            Filter = "新典游戏程序 (th06nc.exe)|th06nc.exe|可执行文件 (*.exe)|*.exe",
            FileName = "th06nc.exe",
            CheckFileExists = true,
            Multiselect = false
        };
        var candidate = GameExe;
        if (File.Exists(candidate)) dialog.InitialDirectory = Path.GetDirectoryName(candidate);
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            SaveGameTarget(dialog.FileName);
        }
    }

    private Process CreateTool(params string[] arguments)
    {
        var useBundledRuntime = File.Exists(BundledDotnet) && File.Exists(CoreDll);
        if (!useBundledRuntime && !File.Exists(CoreExe)) throw new FileNotFoundException("找不到控制器核心程序。", CoreExe);
        var outputEncoding = PipeEncoding();
        var start = new ProcessStartInfo(useBundledRuntime ? BundledDotnet : CoreExe)
        {
            WorkingDirectory = workspace, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = outputEncoding, StandardErrorEncoding = outputEncoding
        };
        if (useBundledRuntime) start.ArgumentList.Add(CoreDll);
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        start.Environment["XINDIANPRAC_ISOLATED"] = "0";
        start.Environment["XINDIANPRAC_GAME_PATH"] = GameExe;
        var process = new Process { StartInfo = start, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) WriteLog(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) WriteLog(e.Data); };
        return process;
    }

    private async Task RunOnceAsync(params string[] arguments)
    {
        if (commandBusy || monitor is { HasExited: false })
        {
            WriteLog("请先停止当前命令或监视器。");
            return;
        }
        commandBusy = true;
        fullUnlock.Enabled = probe.Enabled = startMonitor.Enabled = false;
        try
        {
            using var process = CreateTool(arguments);
            process.Start(); process.BeginOutputReadLine(); process.BeginErrorReadLine();
            await process.WaitForExitAsync();
            WriteLog(process.ExitCode == 0 ? "操作完成。" : $"操作失败，错误码 {process.ExitCode}。请查看上方原因。");
        }
        catch (Exception ex) { ShowError(ex.Message); }
        finally
        {
            commandBusy = false;
            fullUnlock.Enabled = probe.Enabled = startMonitor.Enabled = true;
            RefreshStatus();
        }
    }

    private void StartMonitoring(ReadySelection? selected = null)
    {
        if (commandBusy || monitor is { HasExited: false }) return;
        if (selected is null && boss.Checked && stage.SelectedItem is not StageOption) { ShowError("请先选择 Boss 关卡。"); return; }
        SaveSettings();
        try
        {
            var values = new[] { ((int)lives.Value).ToString(), ((int)bombs.Value).ToString(), ((int)power.Value).ToString() };
            var arguments = selected is not null
                ? new[] { "monitor-start", selected.Stage.ToString(), selected.Score.ToString(),
                    selected.Lives.ToString(), selected.Bombs.ToString(), selected.Power.ToString(), selected.Boss ? "1" : "0" }
                : boss.Checked
                ? new[] { "monitor-boss", ((StageOption)stage.SelectedItem!).Number.ToString() }.Concat(values).ToArray()
                : new[] { "monitor-set" }.Concat(values).ToArray();
            monitor = CreateTool(arguments);
            monitor.Exited += (_, _) =>
            {
                if (IsDisposed || !IsHandleCreated) return;
                BeginInvoke(() =>
                {
                    if (IsDisposed) return;
                    WriteLog($"监视已结束，错误码 {monitor?.ExitCode}。");
                    startMonitor.Enabled = true; stopMonitor.Enabled = false;
                    fullUnlock.Enabled = probe.Enabled = true;
                    RefreshStatus();
                });
            };
            monitor.Start(); monitor.BeginOutputReadLine(); monitor.BeginErrorReadLine();
            startMonitor.Enabled = false; stopMonitor.Enabled = true;
            fullUnlock.Enabled = probe.Enabled = false;
            WriteLog(selected is not null ? $"自动开始 Stage {selected.Stage} 开局监视，初始分数 {selected.Score}。" :
                boss.Checked ? $"开始 Stage {((StageOption)stage.SelectedItem!).Number} Boss 监视。" : "开始整面监视。");
            RefreshStatus();
        }
        catch (Exception ex)
        {
            monitor?.Dispose(); monitor = null;
            ShowError(ex.Message);
        }
    }

    private void StopMonitoring()
    {
        try
        {
            if (monitor is { HasExited: false })
            {
                monitor.Kill();
                WriteLog("已停止本窗口启动的监视器；游戏保持运行。");
            }
        }
        catch (Exception ex) { WriteLog($"停止监视时发生错误：{ex.Message}"); }
        startMonitor.Enabled = true; stopMonitor.Enabled = false;
        fullUnlock.Enabled = probe.Enabled = true;
        RefreshStatus();
    }

    private void WriteLog(string message)
    {
        if (IsDisposed) return;
        if (InvokeRequired)
        {
            if (IsHandleCreated) BeginInvoke(() => WriteLog(message));
            return;
        }
        log.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
        log.SelectionStart = log.TextLength;
        log.ScrollToCaret();
    }

    private void ShowError(string message)
    {
        WriteLog("错误：" + message);
        MessageBox.Show(this, message, "练习控制器", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    private sealed record StageOption(int Number, string Name)
    {
        public override string ToString() => $"Stage {Number}  {Name}";
    }
}
