using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace XinDianPrac;

internal static class Program
{
    // 发行包布局：<游戏根>\XinDianPrac-*\dist\XinDianPrac.exe → <游戏根>\th06nc.exe
    // 开发布局：  <工作目录>\dist\XinDianPrac.exe             → <工作目录>\isolated-game\th06nc.exe
    private static string ResolveGamePath(bool isolated)
    {
        var controllerRoot = new DirectoryInfo(AppContext.BaseDirectory).Parent?.FullName
                             ?? AppContext.BaseDirectory;
        var sibling = Path.GetFullPath(Path.Combine(controllerRoot, "..", "th06nc.exe"));
        var localIsolated = Path.Combine(controllerRoot, "isolated-game", "th06nc.exe");
        if (isolated) return File.Exists(localIsolated) ? localIsolated : sibling;
        if (File.Exists(sibling)) return sibling;
        return File.Exists(localIsolated) ? localIsolated : sibling;
    }
    private static class Address
    {
        internal static int Lives { get; private set; } = 0x4FF0F0;
        internal static int Bombs { get; private set; } = 0x4FF0F1;
        internal static int Power { get; private set; } = 0x4F1E88;
        internal static int ScoreCandidate { get; private set; } = 0x4F2790;
        internal static int Stage { get; private set; } = 0x4F1E84;
        internal static int Difficulty { get; private set; } = 0x4F27C0;
        internal static int Character { get; private set; } = 0x4F1E80;
        internal static int Shot { get; private set; } = 0x4F1E81;
        internal static int Practice { get; private set; } = 0x4F27B4;
        internal static int PracticeB5 { get; private set; } = 0x4F27B5;
        internal static int PracticeC4 { get; private set; } = 0x4F27C4;
        internal static int Mode { get; private set; } = 0xC21D9C;
        internal static int Menu { get; private set; } = 0x50A0D0;
        internal static int Cursor { get; private set; } = 0xC07268;
        internal static int DefaultLives { get; private set; } = 0xC21DB4;
        internal static int DefaultBombs { get; private set; } = 0xC21DB5;
        internal static int C21DBB { get; private set; } = 0xC21DBB;
        internal static int PracticeUnlockBase { get; private set; } = 0x4FF105;
        internal static int TimelinePrevious { get; private set; } = 0xBADF48;
        internal static int TimelineCurrent { get; private set; } = 0xBADF4C;
        internal const int Data = 0x323000;
        internal static int MemoryDataSize { get; private set; } = 0x906660;

        internal static string Configure(int dataRva, int dataSize)
        {
            if (dataRva == Data && dataSize == 0x906660) return "旧版布局";
            if (dataRva != 0x36D000 || dataSize != 0x908884)
                throw new InvalidOperationException($"尚未定位此游戏的内存布局（.data RVA=0x{dataRva:X}，大小=0x{dataSize:X}），拒绝写入。");

            // Offsets within .data were matched against the old and Steam builds,
            // then checked in Steam's menu, ready screen and paused Stage 1.
            const int gameplayDelta = 0xC50;
            const int menuDelta = 0xFA0;
            const int runtimeDelta = 0x2220;
            Lives += gameplayDelta; Bombs += gameplayDelta; Power += gameplayDelta;
            ScoreCandidate += gameplayDelta; Stage += gameplayDelta;
            Difficulty += gameplayDelta; Character += gameplayDelta; Shot += gameplayDelta;
            Practice += gameplayDelta; PracticeB5 += gameplayDelta; PracticeC4 += gameplayDelta;
            PracticeUnlockBase += gameplayDelta;
            Menu += menuDelta;
            Mode += runtimeDelta; Cursor += runtimeDelta;
            DefaultLives += runtimeDelta; DefaultBombs += runtimeDelta; C21DBB += runtimeDelta;
            TimelinePrevious += runtimeDelta; TimelineCurrent += runtimeDelta;
            MemoryDataSize = dataSize;
            return "Steam 布局";
        }
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
    private sealed record BossEntrance(int Stage, string BossName, int EventFrame, string Verification);
    private sealed record EntranceFile(BossEntrance[] BossEntrances);
    private static readonly Lazy<IReadOnlyDictionary<int, BossEntrance>> BossEntrances = new(() =>
    {
        var path = Path.Combine(AppContext.BaseDirectory, "config", "entrances.json");
        var data = JsonSerializer.Deserialize<EntranceFile>(File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("入口配置文件为空。");
        if (data.BossEntrances.Length != 7 ||
            data.BossEntrances.Any(e => e.Stage is < 1 or > 7 || e.EventFrame is < 1000 or > 20000 ||
                                         string.IsNullOrWhiteSpace(e.BossName)))
            throw new InvalidOperationException("入口配置文件不符合目标版本或格式约束。");
        var entries = data.BossEntrances.ToDictionary(e => e.Stage);
        if (entries.Count != 7) throw new InvalidOperationException("入口配置文件存在重复关卡。");
        return entries;
    });

    private readonly record struct GameState(int Mode, byte Practice, byte Variant, byte VariantEcho,
        int Stage, int Difficulty, byte Character, byte Shot,
        byte Lives, byte Bombs, ushort Power)
    {
        public string VariantName => Variant == 0 && VariantEcho == 0 ? "标准模式" :
            Variant == 1 && VariantEcho == 1 ? "挑战模式" : $"未知模式({Variant}/{VariantEcho})";
        public string Entrance => $"stage={Stage} difficulty={Difficulty} character={Character} shot={Shot} variant={VariantName}";
        public string Resources => $"lives={Lives} bombs={Bombs} power={Power}";
    }

    private static GameState ReadGameState(SafeProcessHandle handle, long baseAddress) => new(
        Native.ReadInt32(handle, baseAddress + Address.Mode),
        Native.ReadByte(handle, baseAddress + Address.Practice),
        Native.ReadByte(handle, baseAddress + Address.PracticeC4),
        Native.ReadByte(handle, baseAddress + Address.C21DBB),
        Native.ReadInt32(handle, baseAddress + Address.Stage),
        Native.ReadInt32(handle, baseAddress + Address.Difficulty),
        Native.ReadByte(handle, baseAddress + Address.Character),
        Native.ReadByte(handle, baseAddress + Address.Shot),
        Native.ReadByte(handle, baseAddress + Address.Lives),
        Native.ReadByte(handle, baseAddress + Address.Bombs),
        Native.ReadUInt16(handle, baseAddress + Address.Power));

    private static void MonitorResources(Process process, SafeProcessHandle handle, long baseAddress,
        byte requestedLives, byte requestedBombs, ushort requestedPower, string fileHash, int bossStage,
        int? requestedScore = null)
    {
        var initial = ReadGameState(handle, baseAddress);
        if (initial.Mode != 2 || initial.Practice != 1 || initial.Variant > 1 || initial.VariantEcho != initial.Variant ||
            initial.Stage is < 1 or > 7 ||
            initial.Difficulty is < 0 or > 4 || initial.Character > 1 || initial.Shot > 1 ||
            initial.Lives > 9 || initial.Bombs > 9 || initial.Power > 128)
            throw new InvalidOperationException("请先进入实际游玩的 Practice 关卡，再启动监视器。当前游戏状态不符合预期。");
        BossEntrance? bossEntrance = bossStage == 0 ? null : BossEntrances.Value[bossStage];
        var bossTargetFrame = bossEntrance?.EventFrame - 1 ?? 0;
        if (bossStage != 0)
        {
            var time = Native.ReadInt32(handle, baseAddress + Address.TimelineCurrent);
            if (initial.Stage != bossStage || initial.Difficulty is < 0 or > 3 ||
                (time is > 1000 && time < bossTargetFrame))
                throw new InvalidOperationException("Boss 入口仅支持隔离副本四种常规难度的对应关卡开场或已进入 Boss 段的状态。");
        }

        var resourcesAddress = checked(baseAddress + Address.Lives);
        var powerAddress = checked(baseAddress + Address.Power);
        var scoreAddress = checked(baseAddress + Address.ScoreCandidate);
        Native.RequireWritable(handle, resourcesAddress, 2);
        Native.RequireWritable(handle, powerAddress, 2);
        if (requestedScore.HasValue) Native.RequireWritable(handle, scoreAddress, 4);

        var logDirectory = Path.Combine(AppContext.BaseDirectory, "logs");
        Directory.CreateDirectory(logDirectory);
        var logPath = Path.Combine(logDirectory, $"monitor-{DateTime.Now:yyyyMMdd-HHmmss-fff}-{process.Id}.log");
        using var log = new StreamWriter(new FileStream(logPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
        { AutoFlush = true };
        void Report(string message)
        {
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}";
            Console.WriteLine(line);
            log.WriteLine(line);
        }
        void CheckEntrance(GameState state)
        {
            if (state.Practice != 1 || state.Stage != initial.Stage || state.Difficulty != initial.Difficulty ||
                (state.Mode == 2 && (state.Variant != initial.Variant || state.VariantEcho != initial.VariantEcho)) ||
                state.Character != initial.Character || state.Shot != initial.Shot)
                throw new InvalidOperationException($"关卡参数发生变化（{state.Entrance}），监视器停止，未继续写入。");
        }
        void Apply(GameState observed, string reason)
        {
            CheckEntrance(observed);
            if (observed.Mode != 2 || observed.Lives > 9 || observed.Bombs > 9 || observed.Power > 128)
                throw new InvalidOperationException("游戏状态或资源值异常，拒绝恢复资源。");
            if (observed.Lives != requestedLives || observed.Bombs != requestedBombs || observed.Power != requestedPower)
            {
                Native.RequireWritable(handle, resourcesAddress, 2);
                Native.RequireWritable(handle, powerAddress, 2);
                Native.WriteExact(handle, resourcesAddress,
                    [requestedLives, requestedBombs], [observed.Lives, observed.Bombs]);
                try
                {
                    Native.WriteExact(handle, powerAddress,
                        BitConverter.GetBytes(requestedPower), BitConverter.GetBytes(observed.Power));
                }
                catch
                {
                    Native.TryRestore(handle, resourcesAddress,
                        [requestedLives, requestedBombs], [observed.Lives, observed.Bombs]);
                    throw;
                }
            }
            if (requestedScore.HasValue)
            {
                var currentScore = Native.ReadInt32(handle, scoreAddress);
                if (currentScore is < 0 or > 999999990)
                    throw new InvalidOperationException($"当前分数 {currentScore} 异常，拒绝写入。");
                if (currentScore != requestedScore.Value)
                    Native.WriteExact(handle, scoreAddress, BitConverter.GetBytes(requestedScore.Value), BitConverter.GetBytes(currentScore));
            }
            var readback = ReadGameState(handle, baseAddress);
            CheckEntrance(readback);
            if (readback.Mode != 2 || readback.Lives != requestedLives ||
                readback.Bombs != requestedBombs || readback.Power != requestedPower ||
                (requestedScore.HasValue && Native.ReadInt32(handle, scoreAddress) != requestedScore.Value))
                throw new InvalidOperationException("恢复资源后的状态回读不一致，监视器停止。");
            Report($"{reason}: {observed.Resources} -> {readback.Resources}" +
                (requestedScore.HasValue ? $" score={requestedScore.Value}" : ""));
        }
        void WarpToBoss()
        {
            var timerRva = Address.TimelinePrevious;
            var address = checked(baseAddress + timerRva);
            Native.RequireWritable(handle, address, 8);
            for (int attempt = 1; attempt <= 100; attempt++)
            {
                var time = Native.ReadBytes(handle, address, 8);
                var previous = BitConverter.ToInt32(time, 0);
                var current = BitConverter.ToInt32(time, 4);
                if (current is < 0 or > 1000)
                    throw new InvalidOperationException($"开局时间线候选值异常：{previous}/{current}，拒绝跳转。");
                if (current == 0 || previous != current - 1)
                {
                    Thread.Sleep(20);
                    continue;
                }
                try
                {
                    Native.WriteExact(handle, address,
                        [.. BitConverter.GetBytes(bossTargetFrame - 1), .. BitConverter.GetBytes(bossTargetFrame)], time);
                    Report($"恢复 Stage {bossStage} {bossEntrance!.BossName} 登场入口：时间线 {previous}/{current} -> {bossTargetFrame - 1}/{bossTargetFrame}");
                    return;
                }
                catch (InvalidOperationException) when (attempt < 100)
                {
                    Thread.Sleep(20);
                }
            }
            throw new InvalidOperationException("等待有效的开局时间线超过 2 秒，未执行 Boss 跳转。");
        }

        Report($"监视启动 PID={process.Id} SHA256={fileHash} {initial.Entrance} " +
            $"入口={(bossStage == 0 ? "整面" : $"Stage {bossStage} {bossEntrance!.BossName} Boss ({bossEntrance.Verification})")} " +
            $"目标 lives={requestedLives} bombs={requestedBombs} power={requestedPower}");
        Apply(initial, "初次设置");
        if (bossStage != 0 && Native.ReadInt32(handle, baseAddress + Address.TimelineCurrent) <= 1000) WarpToBoss();
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancelHandler = (_, e) =>
        {
            e.Cancel = true;
            cancellation.Cancel();
        };
        Console.CancelKeyPress += cancelHandler;
        try
        {
            var previousMode = 2;
            var restartPending = false;
            var restartCount = 0;
            Report("监视中；游戏自带快速重开返回相同关卡后恢复资源。按 Ctrl+C 停止。");
            while (!cancellation.IsCancellationRequested && !process.HasExited)
            {
                var current = ReadGameState(handle, baseAddress);
                if (current.Mode == 1 && current.Practice == 0 && current.Stage == 0)
                {
                    Report("游戏已返回菜单，停止监视。");
                    break;
                }
                CheckEntrance(current);
                if (previousMode == 2 && (current.Mode is 0 or 12))
                {
                    restartPending = true;
                    Report($"检测到关卡切换状态 {current.Mode}，等待重开完成。");
                }
                else if (restartPending && (previousMode is 0 or 12) && current.Mode == 2)
                {
                    // The game assigns default resources on entry. Wait several frames before restoring them.
                    Thread.Sleep(100);
                    var settled = ReadGameState(handle, baseAddress);
                    CheckEntrance(settled);
                    if (settled.Mode != 2)
                        throw new InvalidOperationException("重开后的关卡状态不稳定，监视器停止。");
                    Apply(settled, $"快速重开 #{++restartCount}");
                    if (bossStage != 0) WarpToBoss();
                    restartPending = false;
                }
                else if (current.Mode is not (0 or 2 or 12))
                {
                    throw new InvalidOperationException($"游戏进入未验证的状态 {current.Mode}，监视器停止。");
                }
                previousMode = current.Mode;
                Thread.Sleep(20);
            }
            Report(process.HasExited ? "游戏进程已退出。" : "用户停止监视器。");
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
        }
    }

    private static int Main(string[] args)
    {
        try
        {
            bool probe = args.Length == 1 && args[0] == "probe";
            bool inspectUnlocks = args.Length == 1 && args[0] == "inspect-unlocks";
            bool set = args.Length == 7 && args[0] == "set";
            int scoreTestValue = 0;
            bool scoreTest = args.Length == 2 && args[0] == "score-test" &&
                int.TryParse(args[1], out scoreTestValue) && scoreTestValue is >= 0 and <= 999999990 && scoreTestValue % 10 == 0;
            bool snapshot = args.Length == 2 && args[0] == "snapshot" &&
                args[1].Length is >= 1 and <= 40 && args[1].All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
            int watchSeconds = 0;
            bool watch = args.Length == 2 && args[0] == "watch" && int.TryParse(args[1], out watchSeconds) && watchSeconds is >= 1 and <= 600;
            bool monitorSet = args.Length == 4 && args[0] == "monitor-set";
            int requestedStartStage = 0, requestedStartScore = 0, requestedStartBoss = 0;
            bool monitorStart = args.Length == 7 && args[0] == "monitor-start" &&
                int.TryParse(args[1], out requestedStartStage) && requestedStartStage is >= 1 and <= 6 &&
                int.TryParse(args[2], out requestedStartScore) && requestedStartScore is >= 0 and <= 999999990 &&
                int.TryParse(args[6], out requestedStartBoss) && requestedStartBoss is 0 or 1;
            bool monitorBossStage6 = args.Length == 4 && args[0] == "monitor-boss-stage6";
            int monitorBossStage = 0;
            bool monitorBoss = args.Length == 5 && args[0] == "monitor-boss" &&
                int.TryParse(args[1], out monitorBossStage) && BossEntrances.Value.ContainsKey(monitorBossStage) && monitorBossStage <= 6;
            bool unlockCopy = args.Length == 1 && args[0] == "unlock-copy-stage6";
            bool unlockCurrent = args.Length == 1 && args[0] == "unlock-copy-current";
            bool warpTest = args.Length == 1 && args[0] == "warp-test-stage6-boss";
            bool warpTestStage1 = args.Length == 1 && args[0] == "warp-test-stage1-boss";
            int genericWarpStage = 0;
            bool genericWarpTest = args.Length == 2 && args[0] == "warp-test-boss" &&
                int.TryParse(args[1], out genericWarpStage) && BossEntrances.Value.ContainsKey(genericWarpStage) && genericWarpStage <= 6;
            bool isolated = Environment.GetEnvironmentVariable("XINDIANPRAC_ISOLATED") == "1";
            if (!probe && !inspectUnlocks && !set && !scoreTest && !snapshot && !watch && !monitorSet && !monitorStart && !monitorBossStage6 && !monitorBoss && !unlockCopy && !unlockCurrent && !warpTest && !warpTestStage1 && !genericWarpTest)
            {
                Console.WriteLine("用法：XinDianPrac probe（只读）");
                Console.WriteLine("      XinDianPrac inspect-unlocks（只读调查 Practice 关卡上限）");
                Console.WriteLine("      XinDianPrac set <残机> <Bomb> <Power> <预期当前残机> <预期当前Bomb> <预期当前Power>");
                Console.WriteLine("      XinDianPrac score-test <分数>（仅隔离副本，分数字段调查）");
                Console.WriteLine("      XinDianPrac snapshot <名称>（只读调查，保存当前 .data 内存快照）");
                Console.WriteLine("      XinDianPrac watch <秒数>（只读记录重开时的状态变化）");
                Console.WriteLine("      XinDianPrac monitor-set <残机> <Bomb> <Power>（监视原作快速重开并恢复资源）");
                Console.WriteLine("      XinDianPrac monitor-start <关卡1-6> <分数> <残机> <Bomb> <Power> <Boss0或1>（仅隔离副本）");
                Console.WriteLine("      XinDianPrac monitor-boss-stage6 <残机> <Bomb> <Power>（仅隔离副本 Boss 重开测试）");
                Console.WriteLine("      XinDianPrac monitor-boss <关卡1-6> <残机> <Bomb> <Power>（仅隔离副本）");
                Console.WriteLine("      XinDianPrac unlock-copy-stage6（仅隔离副本，调查用途）");
                Console.WriteLine("      XinDianPrac unlock-copy-current（仅隔离副本，当前难度/机体的 Practice 菜单）");
                Console.WriteLine("      XinDianPrac warp-test-stage6-boss（仅隔离副本，实验性调查）");
                Console.WriteLine("      XinDianPrac warp-test-stage1-boss（仅隔离副本，实验性调查）");
                Console.WriteLine("      XinDianPrac warp-test-boss <关卡1-6>（仅隔离副本，实验性调查）");
                return 2;
            }
            if ((unlockCopy || unlockCurrent || warpTest || warpTestStage1 || genericWarpTest || monitorBossStage6 || monitorBoss || scoreTest) && !isolated)
                throw new InvalidOperationException("此实验命令只允许在隔离副本运行。请设置 XINDIANPRAC_ISOLATED=1。");

            byte requestedLives = 0, requestedBombs = 0, expectedLives = 0, expectedBombs = 0;
            ushort requestedPower = 0, expectedPower = 0;
            if (set || monitorSet || monitorBossStage6 || monitorBoss || monitorStart)
            {
                var resourceArg = monitorStart ? 3 : monitorBoss ? 2 : 1;
                if (!byte.TryParse(args[resourceArg], out requestedLives) || requestedLives > 9 ||
                    !byte.TryParse(args[resourceArg + 1], out requestedBombs) || requestedBombs > 9 ||
                    !ushort.TryParse(args[resourceArg + 2], out requestedPower) || requestedPower > 128 ||
                    (set && (!byte.TryParse(args[4], out expectedLives) || expectedLives > 9 ||
                             !byte.TryParse(args[5], out expectedBombs) || expectedBombs > 9 ||
                             !ushort.TryParse(args[6], out expectedPower) || expectedPower > 128)))
                    throw new InvalidOperationException("资源参数无效：残机和 Bomb 限 0–9，Power 限 0–128。");
            }

            var configuredGamePath = Environment.GetEnvironmentVariable("XINDIANPRAC_GAME_PATH");
            var expectedPath = Path.GetFullPath(!string.IsNullOrWhiteSpace(configuredGamePath)
                ? configuredGamePath : ResolveGamePath(isolated));
            if (!File.Exists(expectedPath))
                throw new InvalidOperationException($"找不到目标游戏：{expectedPath}");

            var fileHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(expectedPath)));
            var processes = Process.GetProcessesByName("th06nc");
            try
            {
                var matches = processes.Where(p =>
                {
                    try { return string.Equals(Path.GetFullPath(p.MainModule!.FileName), expectedPath, StringComparison.OrdinalIgnoreCase); }
                    catch { return false; }
                }).ToArray();

                if (matches.Length != 1)
                    throw new InvalidOperationException($"目标路径下应恰好有一个游戏进程，实际找到 {matches.Length} 个。");

                using var process = matches[0];
                var module = process.MainModule ?? throw new InvalidOperationException("无法读取游戏主模块。");
                var moduleBase = module.BaseAddress.ToInt64();
                var imageSize = module.ModuleMemorySize;
                var dataSection = ReadDataSection(expectedPath);
                var layout = Address.Configure(dataSection.Rva, dataSection.VirtualSize);
                if (moduleBase <= 0 || dataSection.Rva < 0 || dataSection.VirtualSize < Address.MemoryDataSize ||
                    imageSize < dataSection.Rva + Address.MemoryDataSize)
                    throw new InvalidOperationException("模块基址或映像大小异常，停止读取。");
                var baseAddress = checked(moduleBase + dataSection.Rva - Address.Data);
                Console.WriteLine($"地址布局：{layout}；.data RVA=0x{dataSection.Rva:X}。");

                using var handle = Native.Open(process.Id, set || scoreTest || monitorSet || monitorStart || monitorBossStage6 || monitorBoss || unlockCopy || unlockCurrent || warpTest || warpTestStage1 || genericWarpTest);
                if (scoreTest)
                {
                    var state = ReadGameState(handle, baseAddress);
                    if (state.Mode != 2 || state.Practice != 1 || state.Stage is < 1 or > 6)
                        throw new InvalidOperationException("仅在隔离副本的 Practice 实际游玩或暂停状态下测试分数。");
                    var address = baseAddress + Address.ScoreCandidate;
                    var before = Native.ReadInt32(handle, address);
                    if (before is < 0 or > 999999990)
                        throw new InvalidOperationException($"分数候选值 {before} 异常，拒绝写入。");
                    Native.RequireWritable(handle, address, 4);
                    Native.WriteExact(handle, address, BitConverter.GetBytes(scoreTestValue), BitConverter.GetBytes(before));
                    Console.WriteLine($"隔离副本分数候选 RVA 0x{Address.ScoreCandidate:X}: {before} → {scoreTestValue}。请检查 HUD；仅用于定位。");
                    return 0;
                }
                if (unlockCurrent)
                {
                    var state = ReadGameState(handle, baseAddress);
                    if (state.Mode != 1 || state.Practice != 0 || state.Stage != 0 ||
                        state.Difficulty is < 0 or > 3 || state.Character > 1 || state.Shot > 1)
                        throw new InvalidOperationException("请在隔离副本的 Practice 关卡选择菜单，选定难度与机体后运行此命令。");
                    var address = baseAddress + Address.PracticeUnlockBase + state.Difficulty + 24 * (state.Shot + 2 * state.Character);
                    var current = Native.ReadByte(handle, address);
                    var target = state.Difficulty == 0 ? 5 : 6;
                    if ((current is < 1 or > 6) && current != 99)
                        throw new InvalidOperationException($"当前关卡上限值 {current} 异常，拒绝写入。");
                    if (current < target)
                    {
                        Native.RequireWritable(handle, address, 1);
                        Native.WriteExact(handle, address, [(byte)target], [current]);
                    }
                    Console.WriteLine(current == 99
                        ? $"隔离副本 {state.Entrance} 已处于游戏自带的全开值 99，无需写入。"
                        : $"隔离副本 {state.Entrance} 的关卡菜单上限 {current} → {Math.Max(current, target)}。请退回上一层再进入 Practice 以刷新菜单。");
                    return 0;
                }
                if (inspectUnlocks)
                {
                    var allUnlocked = true;
                    for (var character = 0; character < 2; character++)
                    for (var shot = 0; shot < 2; shot++)
                    {
                        var values = new int[4];
                        for (var difficulty = 0; difficulty < 4; difficulty++)
                            values[difficulty] = Native.ReadByte(handle, baseAddress + Address.PracticeUnlockBase + difficulty + 24 * (shot + 2 * character));
                        allUnlocked &= values.All(v => v == 99);
                        Console.WriteLine($"character={character} shot={shot} Easy/Normal/Hard/Lunatic={string.Join('/', values)}");
                    }
                    Console.WriteLine(allUnlocked
                        ? "游戏内建全开值 99 已覆盖 16 个难度／机体槽位；此命令没有写入内存。"
                        : "此命令没有写入内存；各槽位的非 99 上限仍需结合菜单验证。");
                    return 0;
                }
                if (warpTest || warpTestStage1 || genericWarpTest)
                {
                    var state = ReadGameState(handle, baseAddress);
                    var expectedStage = genericWarpTest ? genericWarpStage : warpTestStage1 ? 1 : 6;
                    var targetFrame = BossEntrances.Value[expectedStage].EventFrame - 1;
                    if (state.Mode != 2 || state.Practice != 1 || state.Stage != expectedStage || state.Difficulty is < 0 or > 3)
                        throw new InvalidOperationException($"仅支持隔离副本常规难度 Stage {expectedStage} 游玩 / 暂停状态下的时间线测试。");
                    var timelinePreviousRva = Address.TimelinePrevious;
                    var address = checked(baseAddress + timelinePreviousRva);
                    var previous = Native.ReadInt32(handle, address);
                    var current = Native.ReadInt32(handle, address + 4);
                    if (current is < 0 or > 1000 || previous != current - 1)
                        throw new InvalidOperationException($"时间线候选值异常：previous={previous}, current={current}，拒绝跳转。");
                    Native.RequireWritable(handle, address, 8);
                    byte[] expected = [.. BitConverter.GetBytes(previous), .. BitConverter.GetBytes(current)];
                    byte[] target = [.. BitConverter.GetBytes(targetFrame - 1), .. BitConverter.GetBytes(targetFrame)];
                    Native.WriteExact(handle, address, target, expected);
                    Console.WriteLine($"实验性：隔离副本 RVA 0x{timelinePreviousRva:X}/0x{timelinePreviousRva + 4:X} " +
                        $"从 {previous}/{current} 暂时设为 {targetFrame - 1}/{targetFrame}。请解除暂停观察 Boss 登场；如异常请退出副本。此功能尚未验证。");
                    return 0;
                }
                if (monitorSet || monitorBossStage6 || monitorBoss || monitorStart)
                {
                    if (monitorStart && ReadGameState(handle, baseAddress).Stage != requestedStartStage)
                        throw new InvalidOperationException("实际关卡与确认窗口选择的关卡不一致，拒绝开始监视。");
                    MonitorResources(process, handle, baseAddress, requestedLives, requestedBombs, requestedPower,
                        fileHash, monitorStart ? (requestedStartBoss == 1 ? requestedStartStage : 0) :
                        monitorBossStage6 ? 6 : monitorBoss ? monitorBossStage : 0,
                        monitorStart ? requestedStartScore : null);
                    return 0;
                }
                if (watch)
                {
                    string ReadState() => $"state={Native.ReadInt32(handle, baseAddress + Address.Mode)} " +
                        $"practice={Native.ReadByte(handle, baseAddress + Address.Practice)} " +
                        $"stage={Native.ReadInt32(handle, baseAddress + Address.Stage)} " +
                        $"difficulty={Native.ReadInt32(handle, baseAddress + Address.Difficulty)} " +
                        $"character={Native.ReadByte(handle, baseAddress + Address.Character)} " +
                        $"shot={Native.ReadByte(handle, baseAddress + Address.Shot)} " +
                        $"lives={Native.ReadByte(handle, baseAddress + Address.Lives)} " +
                        $"bombs={Native.ReadByte(handle, baseAddress + Address.Bombs)} " +
                        $"power={Native.ReadUInt16(handle, baseAddress + Address.Power)}";
                    var previous = "";
                    var clock = Stopwatch.StartNew();
                    Console.WriteLine($"只读监视 PID {process.Id} {watchSeconds} 秒。请现在使用游戏自带快速重开。");
                    while (clock.Elapsed.TotalSeconds < watchSeconds && !process.HasExited)
                    {
                        var current = ReadState();
                        if (current != previous)
                        {
                            Console.WriteLine($"{clock.ElapsedMilliseconds,6}ms {current}");
                            previous = current;
                        }
                        Thread.Sleep(20);
                    }
                    return 0;
                }
                if (unlockCopy)
                {
                    var difficulty = Native.ReadInt32(handle, baseAddress + Address.Difficulty);
                    var character = Native.ReadByte(handle, baseAddress + Address.Character);
                    var shot = Native.ReadByte(handle, baseAddress + Address.Shot);
                    var practice = Native.ReadByte(handle, baseAddress + Address.Practice);
                    var selectedStage = Native.ReadInt32(handle, baseAddress + Address.Stage);
                    var state = Native.ReadInt32(handle, baseAddress + Address.Mode);
                    if (difficulty != 1 || character != 0 || shot != 0 ||
                        !((state == 1 && practice == 0 && selectedStage == 0) || (state == 2 && practice == 1 && selectedStage == 1)))
                        throw new InvalidOperationException("隔离副本当前不是预期的 Normal 灵梦 A 菜单或 Stage 1 状态，拒绝解锁。");
                    var unlockAddress = baseAddress + Address.PracticeUnlockBase + 1;
                    var currentMax = Native.ReadByte(handle, unlockAddress);
                    if (currentMax is < 1 or > 6)
                        throw new InvalidOperationException($"隔离副本关卡上限值 {currentMax} 异常，拒绝解锁。");
                    Native.RequireWritable(handle, unlockAddress, 1);
                    if (currentMax < 6) Native.WriteExact(handle, unlockAddress, [6], [currentMax]);
                    Console.WriteLine($"只在隔离副本的进程内把 Normal 灵梦 A 的可见关卡上限从 {currentMax} 改为 6。请确认菜单显示；隔离副本的 score.dat 可以独立变化。");
                    return 0;
                }
                if (snapshot)
                {
                    var buffer = new byte[Address.MemoryDataSize];
                    for (int offset = 0; offset < buffer.Length; offset += 0x10000)
                    {
                        var part = Native.ReadBytes(handle, checked(baseAddress + Address.Data + offset), Math.Min(0x10000, buffer.Length - offset));
                        part.CopyTo(buffer, offset);
                    }
                    var folder = Path.Combine(AppContext.BaseDirectory, "investigation", "snapshots");
                    Directory.CreateDirectory(folder);
                    var output = Path.Combine(folder, $"{args[1]}.bin");
                    if (File.Exists(output)) throw new InvalidOperationException($"快照已存在，不覆盖：{output}");
                    File.WriteAllBytes(output, buffer);
                    Console.WriteLine($"只读内存快照：{output}（PID {process.Id}，.data RVA 0x{dataSection.Rva:X}，{buffer.Length} 字节）");
                    return 0;
                }
                byte lives = Native.ReadByte(handle, checked(baseAddress + Address.Lives));
                byte bombs = Native.ReadByte(handle, checked(baseAddress + Address.Bombs));
                ushort power = Native.ReadUInt16(handle, checked(baseAddress + Address.Power));

                Console.WriteLine($"进程 PID：{process.Id}");
                Console.WriteLine($"模块基址：0x{baseAddress:X}，映像大小：0x{imageSize:X}");
                Console.WriteLine($"版本 SHA-256：{fileHash}");
                Console.WriteLine($"候选资源值：残机={lives}，Bomb={bombs}，Power={power}");
                if (probe)
                {
                    var observedMode = ReadGameState(handle, baseAddress);
                    Console.WriteLine($"菜单状态[0x{Address.Menu:X}]={Native.ReadInt32(handle, baseAddress + Address.Menu)}，" +
                        $"关卡光标[0x{Address.Cursor:X}]={Native.ReadInt32(handle, baseAddress + Address.Cursor)}");
                    Console.WriteLine(observedMode.Mode == 2 && observedMode.Practice == 1
                        ? $"Practice 玩法：{observedMode.VariantName}。"
                        : "Practice 玩法：当前不在实际游玩状态，暂不判断。");
                    Console.WriteLine($"调查候选：默认残机/Bomb={Native.ReadByte(handle, baseAddress + Address.DefaultLives)}/{Native.ReadByte(handle, baseAddress + Address.DefaultBombs)}，" +
                        $"难度候选[0x4F27C0]={Native.ReadInt32(handle, baseAddress + Address.Difficulty)}，" +
                        $"状态候选[0xC21D9C]={Native.ReadInt32(handle, baseAddress + Address.Mode)}");
                    Console.WriteLine($"调查候选：角色/机体?[0x4F1E80/81]={Native.ReadByte(handle, baseAddress + Address.Character)}/{Native.ReadByte(handle, baseAddress + Address.Shot)}，" +
                        $"标志[0xC21DBB]={Native.ReadByte(handle, baseAddress + Address.C21DBB)}");
                    Console.WriteLine($"调查候选：当前关卡索引?[0x4F1E84]={Native.ReadInt32(handle, baseAddress + Address.Stage)}，" +
                        $"练习标志?[0x4F27B4/B5/C4]={Native.ReadByte(handle, baseAddress + Address.Practice)}/{Native.ReadByte(handle, baseAddress + Address.PracticeB5)}/{Native.ReadByte(handle, baseAddress + Address.PracticeC4)}");
                    Console.WriteLine($"调查候选：Normal 灵梦 A 关卡上限?[0x4FF106]={Native.ReadByte(handle, baseAddress + Address.PracticeUnlockBase + 1)}");
                    Console.WriteLine($"调查候选：敌机时间线?[0xBADF48/4C]={Native.ReadInt32(handle, baseAddress + Address.TimelinePrevious)}/{Native.ReadInt32(handle, baseAddress + Address.TimelineCurrent)}");
                    Console.WriteLine("此命令没有写入内存。");
                    return 0;
                }

                var gameState = ReadGameState(handle, baseAddress);
                if (gameState.Mode != 2 || gameState.Practice != 1 || gameState.Stage is < 1 or > 7 ||
                    gameState.Difficulty is < 0 or > 4 || gameState.Character > 1 || gameState.Shot > 1)
                    throw new InvalidOperationException("当前不是已验证的 Practice 实际游玩状态，拒绝设置资源。");

                if (lives != expectedLives || bombs != expectedBombs || power != expectedPower)
                    throw new InvalidOperationException("当前资源与命令中指定的预期值不同，拒绝写入。请重新 probe。");

                var resourcesAddress = checked(baseAddress + Address.Lives);
                var powerAddress = checked(baseAddress + Address.Power);
                Native.RequireWritable(handle, resourcesAddress, 2);
                Native.RequireWritable(handle, powerAddress, 2);
                Native.WriteExact(handle, resourcesAddress, [requestedLives, requestedBombs], [expectedLives, expectedBombs]);
                try
                {
                    Native.WriteExact(handle, powerAddress, BitConverter.GetBytes(requestedPower), BitConverter.GetBytes(expectedPower));
                }
                catch
                {
                    // Revert the first field only if the game has not changed it since our write.
                    Native.TryRestore(handle, resourcesAddress, [requestedLives, requestedBombs], [expectedLives, expectedBombs]);
                    throw;
                }

                Console.WriteLine($"写入后回读：残机={Native.ReadByte(handle, resourcesAddress)}，Bomb={Native.ReadByte(handle, resourcesAddress + 1)}，Power={Native.ReadUInt16(handle, powerAddress)}");
                Console.WriteLine("只修改了游戏进程内存；原始 EXE 未改动。请检查 HUD 是否同步显示。");
                return 0;
            }
            finally
            {
                foreach (var process in processes) process.Dispose();
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"检查失败：{ex.Message}");
            return 1;
        }
    }
}

internal static class Native
{
    private const uint ProcessVmRead = 0x0010;
    private const uint ProcessVmWrite = 0x0020;
    private const uint ProcessVmOperation = 0x0008;
    private const uint ProcessQueryLimitedInformation = 0x1000;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint access, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadProcessMemory(SafeProcessHandle process, nint address, [Out] byte[] buffer, nuint size, out nuint bytesRead);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteProcessMemory(SafeProcessHandle process, nint address, byte[] buffer, nuint size, out nuint bytesWritten);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nuint VirtualQueryEx(SafeProcessHandle process, nint address, out MemoryBasicInformation info, nuint length);

    internal static SafeProcessHandle Open(int processId, bool write)
    {
        var access = ProcessVmRead | ProcessQueryLimitedInformation;
        if (write) access |= ProcessVmWrite | ProcessVmOperation;
        var handle = OpenProcess(access, false, processId);
        if (handle.IsInvalid)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "无法打开游戏进程");
        return handle;
    }

    internal static void RequireWritable(SafeProcessHandle handle, long address, int count)
    {
        if (VirtualQueryEx(handle, (nint)address, out var info, (nuint)Marshal.SizeOf<MemoryBasicInformation>()) == 0)
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"无法验证内存页 0x{address:X}");
        var end = checked(address + count);
        var regionEnd = checked(info.BaseAddress.ToInt64() + (long)info.RegionSize);
        var protection = info.Protect & 0xFF;
        if (info.State != 0x1000 || address < info.BaseAddress.ToInt64() || end > regionEnd ||
            (info.Protect & 0x100) != 0 || protection is not (0x04 or 0x08 or 0x40 or 0x80))
            throw new InvalidOperationException($"目标地址 0x{address:X} 不在已提交的可写内存页中，拒绝写入。");
    }

    internal static byte[] ReadBytes(SafeProcessHandle handle, long address, int count)
    {
        var data = new byte[count];
        if (!ReadProcessMemory(handle, (nint)address, data, (nuint)count, out var bytesRead) || bytesRead != (nuint)count)
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"读取地址 0x{address:X} 失败");
        return data;
    }

    internal static byte ReadByte(SafeProcessHandle handle, long address) => ReadBytes(handle, address, 1)[0];
    internal static ushort ReadUInt16(SafeProcessHandle handle, long address) => BitConverter.ToUInt16(ReadBytes(handle, address, 2));
    internal static int ReadInt32(SafeProcessHandle handle, long address) => BitConverter.ToInt32(ReadBytes(handle, address, 4));

    internal static void WriteExact(SafeProcessHandle handle, long address, byte[] value, byte[] expected)
    {
        if (!ReadBytes(handle, address, expected.Length).SequenceEqual(expected))
            throw new InvalidOperationException($"写入前地址 0x{address:X} 的值已改变，拒绝写入。");
        if (!WriteProcessMemory(handle, (nint)address, value, (nuint)value.Length, out var bytesWritten) || bytesWritten != (nuint)value.Length)
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"写入地址 0x{address:X} 失败");
        if (!ReadBytes(handle, address, value.Length).SequenceEqual(value))
            throw new InvalidOperationException($"地址 0x{address:X} 写入后回读不一致。");
    }

    internal static void TryRestore(SafeProcessHandle handle, long address, byte[] current, byte[] original)
    {
        try { WriteExact(handle, address, original, current); }
        catch { /* Report the original failure without masking it. */ }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryBasicInformation
    {
        public nint BaseAddress;
        public nint AllocationBase;
        public uint AllocationProtect;
        public nuint RegionSize;
        public uint State;
        public uint Protect;
        public uint Type;
    }
}

internal sealed class SafeProcessHandle : Microsoft.Win32.SafeHandles.SafeHandleZeroOrMinusOneIsInvalid
{
    private SafeProcessHandle() : base(true) { }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(nint handle);

    protected override bool ReleaseHandle() => CloseHandle(handle);
}
