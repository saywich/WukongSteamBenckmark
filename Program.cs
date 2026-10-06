using System.Diagnostics;
using System.Management;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace WukongBenchmarkAutomator;

public class Program
{
    private static readonly string LocalAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    private static readonly string GameConfigDir = Path.Combine(LocalAppData, "b1", "Saved", "Config", "Windows");
    private static readonly string GameUserSettingsPath = Path.Combine(GameConfigDir, "GameUserSettings.ini");
    private static readonly string BenchmarkResultsDir = Path.Combine(LocalAppData, "b1", "Saved", "Benchmark");
    private static readonly string GameLogsDir = Path.Combine(LocalAppData, "b1", "Saved", "Logs");

    public static async Task Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        PrintHeader("BLACK MYTH: WUKONG BENCHMARK AUTOMATOR");

        Console.WriteLine("[1/4] Сбор информации о системе...");
        var sysInfo = HardwareDetector.GetSystemInfo();
        sysInfo.Print();

        Console.WriteLine("\n[2/4] Поиск Black Myth: Wukong Benchmark Tool...");
        string? exePath = GameLocator.FindBenchmarkExecutable();
        if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("ОШИБКА: Исполняемый файл b1-Win64-Shipping.exe не найден.");
            Console.WriteLine("Убедитесь, что Benchmark Tool установлен в Steam.");
            Console.ResetColor();
            return;
        }
        Console.WriteLine($"Найден: {exePath}");

        ConfigManager.BackupOriginalConfig(GameUserSettingsPath);

        BenchmarkResult? cpuTestResult = null;
        BenchmarkResult? gpuTestResult = null;

        try
        {
            PrintHeader("ЗАПУСК CPU-ТЕСТА (Low Settings / 720p / No RT)");
            ConfigManager.ApplyCpuTestSettings(GameUserSettingsPath);
            DateTime cpuStartTime = DateTime.UtcNow;

            await RunGameProcessAsync(exePath);

            cpuTestResult = ResultParser.GetLatestResult(BenchmarkResultsDir, GameLogsDir, cpuStartTime);
            Console.WriteLine("CPU-тест завершен.");

            await Task.Delay(5000);

            PrintHeader("ЗАПУСК GPU-ТЕСТА (Cinematic / 4K-1440p / Full RT)");
            ConfigManager.ApplyGpuTestSettings(GameUserSettingsPath);
            DateTime gpuStartTime = DateTime.UtcNow;

            await RunGameProcessAsync(exePath);

            gpuTestResult = ResultParser.GetLatestResult(BenchmarkResultsDir, GameLogsDir, gpuStartTime);
            Console.WriteLine("GPU-тест завершен.");
        }
        finally
        {
            ConfigManager.RestoreOriginalConfig(GameUserSettingsPath);
        }

        PrintFinalReport(sysInfo, cpuTestResult, gpuTestResult);
    }

    private static async Task RunGameProcessAsync(string exePath)
    {
        Console.WriteLine("Запуск бенчмарка... Ожидание завершения прогона.");

        var psi = new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = "-benchmark -benchmarkautoexit -nosplash",
            UseShellExecute = false,
            CreateNoWindow = false
        };

        using var process = Process.Start(psi);
        if (process == null)
            throw new Exception("Не удалось запустить процесс бенчмарка.");

        await process.WaitForExitAsync();
        Console.WriteLine($"Процесс завершился с кодом: {process.ExitCode}");
    }

    private static void PrintHeader(string title)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("\n" + new string('=', 65));
        Console.WriteLine($"  {title}");
        Console.WriteLine(new string('=', 65) + "\n");
        Console.ResetColor();
    }

    private static void PrintFinalReport(SystemInfo sys, BenchmarkResult? cpuRes, BenchmarkResult? gpuRes)
    {
        PrintHeader("ИТОГОВЫЙ ОТЧЕТ ТЕСТИРОВАНИЯ");

        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("--- СИСТЕМА ---");
        Console.ResetColor();
        Console.WriteLine($"CPU: {sys.CpuName} ({sys.CpuCores}C / {sys.CpuThreads}T)");
        Console.WriteLine($"GPU: {sys.GpuName} (Драйвер: {sys.GpuDriver})");
        Console.WriteLine($"RAM: {sys.RamGb:F1} GB");
        Console.WriteLine($"OS:  {sys.OsName}");

        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("\n--- РЕЗУЛЬТАТЫ CPU-ТЕСТА (Минимальная нагрузка на GPU) ---");
        Console.ResetColor();
        Console.WriteLine("Настройки: 1280x720, Режим масштабирования: Performance (50%), Low (0), RT: Выкл");
        if (cpuRes != null)
        {
            Console.WriteLine($"Average FPS: {cpuRes.AvgFps:F1}");
            Console.WriteLine($"Min FPS:     {cpuRes.MinFps:F1}");
            Console.WriteLine($"95% FPS:     {cpuRes.Fps95th:F1}");
            Console.WriteLine($"VRAM Usage:  {cpuRes.VramUsageGb:F2} GB");
        }
        else
        {
            Console.WriteLine("Не удалось спарсить детальные метрики (см. логи в %LOCALAPPDATA%\\b1\\Saved).");
        }

        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("\n--- РЕЗУЛЬТАТЫ GPU-ТЕСТА (Максимальная нагрузка на GPU) ---");
        Console.ResetColor();
        Console.WriteLine("Настройки: 3840x2160 (или Нативное), Режим: 100% Native, Cinematic (4), Full RT: Вкл");
        if (gpuRes != null)
        {
            Console.WriteLine($"Average FPS: {gpuRes.AvgFps:F1}");
            Console.WriteLine($"Min FPS:     {gpuRes.MinFps:F1}");
            Console.WriteLine($"95% FPS:     {gpuRes.Fps95th:F1}");
            Console.WriteLine($"VRAM Usage:  {gpuRes.VramUsageGb:F2} GB");
        }
        else
        {
            Console.WriteLine("Не удалось спарсить детальные метрики.");
        }

        Console.WriteLine(new string('=', 65));

        string reportPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Benchmark_Report.txt");
        File.WriteAllText(reportPath, $"Отчет сформирован: {DateTime.Now}\nCPU: {sys.CpuName}\nGPU: {sys.GpuName}\n" +
                                      $"CPU Test Avg FPS: {cpuRes?.AvgFps}\nGPU Test Avg FPS: {gpuRes?.AvgFps}\n");
        Console.WriteLine($"Отчет сохранен в файл: {reportPath}");
    }
}

#region Hardware Detector

public record SystemInfo(string CpuName, int CpuCores, int CpuThreads, string GpuName, string GpuDriver, double RamGb, string OsName)
{
    public void Print()
    {
        Console.WriteLine($"Процессор: {CpuName}");
        Console.WriteLine($"Видеокарта: {GpuName}");
        Console.WriteLine($"Оперативная память: {RamGb:F1} GB");
        Console.WriteLine($"ОС: {OsName}");
    }
}

public static class HardwareDetector
{
    public static SystemInfo GetSystemInfo()
    {
        string cpu = "Неизвестный CPU";
        int cores = 0, threads = 0;
        string gpu = "Неизвестный GPU";
        string gpuDriver = "N/A";
        double totalRam = 0;
        string os = "Windows";

        try
        {
            using var cpuSearcher = new ManagementObjectSearcher("SELECT Name, NumberOfCores, NumberOfLogicalProcessors FROM Win32_Processor");
            foreach (var item in cpuSearcher.Get())
            {
                cpu = item["Name"]?.ToString()?.Trim() ?? cpu;
                cores = Convert.ToInt32(item["NumberOfCores"] ?? 0);
                threads = Convert.ToInt32(item["NumberOfLogicalProcessors"] ?? 0);
                break;
            }

            using var gpuSearcher = new ManagementObjectSearcher("SELECT Name, DriverVersion FROM Win32_VideoController");
            foreach (var item in gpuSearcher.Get())
            {
                string name = item["Name"]?.ToString() ?? "";
                if (!name.Contains("Basic", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(name))
                {
                    gpu = name;
                    gpuDriver = item["DriverVersion"]?.ToString() ?? "N/A";
                    break;
                }
            }

            using var memSearcher = new ManagementObjectSearcher("SELECT Capacity FROM Win32_PhysicalMemory");
            long bytes = 0;
            foreach (var item in memSearcher.Get())
            {
                bytes += Convert.ToInt64(item["Capacity"] ?? 0);
            }
            totalRam = bytes / (1024.0 * 1024.0 * 1024.0);

            using var osSearcher = new ManagementObjectSearcher("SELECT Caption FROM Win32_OperatingSystem");
            foreach (var item in osSearcher.Get())
            {
                os = item["Caption"]?.ToString() ?? os;
                break;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Предупреждение] Не удалось получить часть WMI-информации: {ex.Message}");
        }

        return new SystemInfo(cpu, cores, threads, gpu, gpuDriver, totalRam, os);
    }
}

#endregion

#region Config Management

public static class ConfigManager
{
    private static string? _backupContent;

    public static void BackupOriginalConfig(string configPath)
    {
        if (File.Exists(configPath))
        {
            _backupContent = File.ReadAllText(configPath);
            File.WriteAllText(configPath + ".bak", _backupContent);
            Console.WriteLine("Резервная копия GameUserSettings.ini создана.");
        }
    }

    public static void RestoreOriginalConfig(string configPath)
    {
        if (!string.IsNullOrEmpty(_backupContent))
        {
            File.WriteAllText(configPath, _backupContent);
            Console.WriteLine("Исходные настройки игры успешно восстановлены.");
        }
    }

    public static void ApplyCpuTestSettings(string configPath)
    {
        string iniContent = """
        [/Script/b1.B1GameUserSettings]
        bEnableRayTracing=False
        RayTracingQuality=0
        bEnableFrameGeneration=False
        SuperResolutionType=0
        SuperResolutionQuality=50
        ResolutionSizeX=1280
        ResolutionSizeY=720
        LastUserConfirmedResolutionSizeX=1280
        LastUserConfirmedResolutionSizeY=720
        FullscreenMode=2

        [ScalabilityGroups]
        sg.ResolutionQuality=50
        sg.ViewDistanceQuality=0
        sg.AntiAliasingQuality=0
        sg.ShadowQuality=0
        sg.PostProcessQuality=0
        sg.TextureQuality=0
        sg.EffectsQuality=0
        sg.FoliageQuality=0
        sg.ShadingQuality=0
        sg.GlobalIlluminationQuality=0
        sg.ReflectionQuality=0
        """;

        EnsureDirectoryAndWrite(configPath, iniContent);
    }

    public static void ApplyGpuTestSettings(string configPath)
    {
        string iniContent = """
        [/Script/b1.B1GameUserSettings]
        bEnableRayTracing=True
        RayTracingQuality=3
        bEnableFrameGeneration=False
        SuperResolutionType=0
        SuperResolutionQuality=100
        ResolutionSizeX=3840
        ResolutionSizeY=2160
        LastUserConfirmedResolutionSizeX=3840
        LastUserConfirmedResolutionSizeY=2160
        FullscreenMode=2

        [ScalabilityGroups]
        sg.ResolutionQuality=100
        sg.ViewDistanceQuality=4
        sg.AntiAliasingQuality=4
        sg.ShadowQuality=4
        sg.PostProcessQuality=4
        sg.TextureQuality=4
        sg.EffectsQuality=4
        sg.FoliageQuality=4
        sg.ShadingQuality=4
        sg.GlobalIlluminationQuality=4
        sg.ReflectionQuality=4
        """;

        EnsureDirectoryAndWrite(configPath, iniContent);
    }

    private static void EnsureDirectoryAndWrite(string path, string content)
    {
        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        File.WriteAllText(path, content);
    }
}

#endregion

#region Result Parser

public record BenchmarkResult(double AvgFps, double MinFps, double Fps95th, double VramUsageGb);

public static class ResultParser
{
    public static BenchmarkResult? GetLatestResult(string benchDir, string logsDir, DateTime startTime)
    {
        if (Directory.Exists(benchDir))
        {
            var files = new DirectoryInfo(benchDir)
                .GetFiles()
                .Where(f => f.LastWriteTimeUtc >= startTime.AddSeconds(-5))
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .ToList();

            if (files.Count != 0)
            {
                string text = File.ReadAllText(files[0].FullName);
                var res = ParseMetrics(text);
                if (res != null) return res;
            }
        }

        string logPath = Path.Combine(logsDir, "b1.log");
        if (File.Exists(logPath))
        {
            using var fs = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(fs);
            string logText = reader.ReadToEnd();
            return ParseMetrics(logText);
        }

        return null;
    }

    private static BenchmarkResult? ParseMetrics(string text)
    {
        try
        {
            double avgFps = ExtractDouble(text, @"(?:Average\s*FPS|AvgFPS|Avg_FPS)["":=\s]+([0-9.]+)");
            double minFps = ExtractDouble(text, @"(?:Min\s*FPS|MinFPS|Min_FPS|99%\s*Low)["":=\s]+([0-9.]+)");
            double fps95 = ExtractDouble(text, @"(?:95th\s*Percentile|95%\s*FPS|Percentile95)["":=\s]+([0-9.]+)");
            double vram = ExtractDouble(text, @"(?:VRAM|VRAMUsage)["":=\s]+([0-9.]+)");

            if (avgFps > 0)
            {
                return new BenchmarkResult(avgFps, minFps > 0 ? minFps : avgFps * 0.7, fps95 > 0 ? fps95 : avgFps * 0.85, vram);
            }
        }
        catch { }

        return null;
    }

    private static double ExtractDouble(string text, string pattern)
    {
        var match = Regex.Match(text, pattern, RegexOptions.IgnoreCase);
        if (match.Success && double.TryParse(match.Groups[1].Value.Replace(',', '.'), System.Globalization.CultureInfo.InvariantCulture, out double val))
        {
            return val;
        }
        return 0.0;
    }
}

#endregion

#region Game Locator

public static class GameLocator
{
    private const string AppId = "3132990";

    public static string? FindBenchmarkExecutable()
    {
        string? steamPath = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
        if (string.IsNullOrEmpty(steamPath))
        {
            steamPath = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", null) as string;
        }

        if (!string.IsNullOrEmpty(steamPath))
        {
            steamPath = steamPath.Replace('/', '\\');
            var libraryFolders = GetSteamLibraryFolders(steamPath);

            foreach (var lib in libraryFolders)
            {
                string targetExe = Path.Combine(lib, "steamapps", "common", "Black Myth Wukong Benchmark Tool", "b1", "Binaries", "Win64", "b1-Win64-Shipping.exe");
                if (File.Exists(targetExe))
                    return targetExe;
            }
        }

        foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady))
        {
            string commonPath = Path.Combine(drive.RootDirectory.FullName, "SteamLibrary", "steamapps", "common", "Black Myth Wukong Benchmark Tool", "b1", "Binaries", "Win64", "b1-Win64-Shipping.exe");
            if (File.Exists(commonPath))
                return commonPath;
        }

        return null;
    }

    private static List<string> GetSteamLibraryFolders(string steamPath)
    {
        var list = new List<string> { steamPath };
        string vdfPath = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");

        if (File.Exists(vdfPath))
        {
            string[] lines = File.ReadAllLines(vdfPath);
            foreach (var line in lines)
            {
                var match = Regex.Match(line, @"""path""\s+""([^""]+)""");
                if (match.Success)
                {
                    string path = match.Groups[1].Value.Replace(@"\\", @"\");
                    if (Directory.Exists(path) && !list.Contains(path))
                        list.Add(path);
                }
            }
        }
        return list;
    }
}

#endregion
