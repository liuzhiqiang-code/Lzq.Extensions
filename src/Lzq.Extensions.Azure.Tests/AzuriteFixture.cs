using System.Diagnostics;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Xunit;

namespace LzqNet.Extensions.Azure.Tests;

/// <summary>
/// Azurite 测试夹具，在测试运行前启动 Azurite，运行后关闭
/// </summary>
public class AzuriteFixture : IAsyncLifetime
{
    private Process? _azuriteProcess;
    private string? _azuriteDataPath;
    private const int BlobPort = 10000;
    private const int QueuePort = 10001;
    private const int TablePort = 10002;
    private List<Process> _existingAzuriteProcesses = new();

    public async Task InitializeAsync()
    {
        Console.WriteLine("=== Azurite 初始化开始 ===");

        // 检查 Azurite 是否已安装
        if (!IsAzuriteInstalled())
        {
            await InstallAzuriteAsync();
        }

        // 检查并关闭已运行的 Azurite
        if (IsAzuriteRunning())
        {
            await StopExistingAzuriteAsync();
        }

        // 启动新的 Azurite 实例（带跳过 API 版本检查参数）
        await StartAzuriteAsync();

        // 等待 Azurite 完全启动
        await WaitForAzuriteReadyAsync();

        Console.WriteLine("=== Azurite 初始化完成 ===");
    }

    public async Task DisposeAsync()
    {
        // 测试完成后关闭我们启动的 Azurite
        await StopAzuriteAsync();
        Console.WriteLine("=== Azurite 已关闭 ===");
    }

    private bool IsAzuriteInstalled()
    {
        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = GetAzuriteCommand(),
                    Arguments = "--version",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true
                }
            };
            process.Start();
            process.WaitForExit(5000);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private async Task InstallAzuriteAsync()
    {
        Console.WriteLine("正在安装 Azurite...");

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = GetPackageManager(),
                Arguments = GetInstallArguments(),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };

        process.Start();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            var error = await process.StandardError.ReadToEndAsync();
            throw new Exception($"Azurite 安装失败: {error}");
        }

        Console.WriteLine("Azurite 安装完成");
    }

    private bool IsAzuriteRunning()
    {
        try
        {
            // 检查端口是否被占用
            using var tcpClient = new TcpClient();
            var result = tcpClient.BeginConnect("127.0.0.1", BlobPort, null, null);
            var success = result.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(1));
            if (success)
            {
                tcpClient.EndConnect(result);
                return true;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    private async Task StopExistingAzuriteAsync()
    {
        Console.WriteLine("检测到 Azurite 正在运行，正在关闭...");

        try
        {
            // 方法1: 查找并结束占用端口的进程
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Windows: 使用 netstat 查找占用端口的进程
                var netstatProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "netstat",
                        Arguments = "-ano | findstr :10000",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true
                    }
                };
                netstatProcess.Start();
                var output = await netstatProcess.StandardOutput.ReadToEndAsync();
                await netstatProcess.WaitForExitAsync();

                // 解析 PID
                var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                var pids = new HashSet<int>();
                foreach (var line in lines)
                {
                    var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 5 && parts[4].All(char.IsDigit))
                    {
                        pids.Add(int.Parse(parts[4]));
                    }
                }

                // 结束进程
                foreach (var pid in pids)
                {
                    try
                    {
                        var process = Process.GetProcessById(pid);
                        if (process.ProcessName.Contains("node", StringComparison.OrdinalIgnoreCase) ||
                            process.ProcessName.Contains("azurite", StringComparison.OrdinalIgnoreCase))
                        {
                            process.Kill();
                            await process.WaitForExitAsync();
                            Console.WriteLine($"已结束进程: {process.ProcessName} (PID: {pid})");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"结束进程失败: {ex.Message}");
                    }
                }
            }
            else
            {
                // Linux/Mac: 使用 lsof 查找进程
                var lsofProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "lsof",
                        Arguments = "-ti :10000",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true
                    }
                };
                lsofProcess.Start();
                var output = await lsofProcess.StandardOutput.ReadToEndAsync();
                await lsofProcess.WaitForExitAsync();

                var pids = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var pid in pids)
                {
                    try
                    {
                        var process = Process.GetProcessById(int.Parse(pid));
                        process.Kill();
                        await process.WaitForExitAsync();
                        Console.WriteLine($"已结束进程: {process.ProcessName} (PID: {pid})");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"结束进程失败: {ex.Message}");
                    }
                }
            }

            // 等待端口释放
            await Task.Delay(2000);
            Console.WriteLine("已关闭旧的 Azurite 实例");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"关闭旧 Azurite 时出错: {ex.Message}");
            // 继续执行，尝试启动新的
        }
    }

    private async Task StartAzuriteAsync()
    {
        Console.WriteLine("正在启动 Azurite...");

        _azuriteDataPath = Path.Combine(Path.GetTempPath(), "azurite-data", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_azuriteDataPath);

        // 添加 --skipApiVersionCheck 参数以兼容新版本的 Azure SDK
        var arguments = $"--silent --skipApiVersionCheck --location \"{_azuriteDataPath}\" --debug \"{_azuriteDataPath}/debug.log\"";

        Console.WriteLine($"启动命令: {GetAzuriteCommand()} {arguments}");

        _azuriteProcess = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = GetAzuriteCommand(),
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };

        _azuriteProcess.Start();

        // 读取启动输出以便调试
        _ = Task.Run(async () =>
        {
            await Task.Delay(1000);
            if (!_azuriteProcess.HasExited)
            {
                var output = await _azuriteProcess.StandardOutput.ReadToEndAsync();
                var error = await _azuriteProcess.StandardError.ReadToEndAsync();
                if (!string.IsNullOrEmpty(output))
                    Console.WriteLine($"Azurite 输出: {output}");
                if (!string.IsNullOrEmpty(error))
                    Console.WriteLine($"Azurite 错误: {error}");
            }
        });

        // 等待 Azurite 启动
        await Task.Delay(3000);

        Console.WriteLine("Azurite 已启动 (已跳过 API 版本检查)");
    }

    private async Task WaitForAzuriteReadyAsync()
    {
        var maxAttempts = 10;
        var attempt = 0;

        while (attempt < maxAttempts)
        {
            if (IsAzuriteRunning())
            {
                Console.WriteLine("Azurite 已就绪");
                return;
            }

            Console.WriteLine($"等待 Azurite 启动... ({attempt + 1}/{maxAttempts})");
            await Task.Delay(1000);
            attempt++;
        }

        throw new Exception("Azurite 启动超时");
    }

    private async Task StopAzuriteAsync()
    {
        if (_azuriteProcess != null && !_azuriteProcess.HasExited)
        {
            Console.WriteLine("正在关闭 Azurite...");
            try
            {
                _azuriteProcess.Kill();
                await _azuriteProcess.WaitForExitAsync();
                _azuriteProcess.Dispose();
                Console.WriteLine("Azurite 已关闭");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"关闭 Azurite 时出错: {ex.Message}");
            }
        }

        if (_azuriteDataPath != null && Directory.Exists(_azuriteDataPath))
        {
            try
            {
                Directory.Delete(_azuriteDataPath, true);
                Console.WriteLine($"已清理数据目录: {_azuriteDataPath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"清理数据目录失败: {ex.Message}");
            }
        }
    }

    private string GetAzuriteCommand()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return "azurite.cmd";
        }
        return "azurite";
    }

    private string GetPackageManager()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return "cmd.exe";
        }
        return "npm";
    }

    private string GetInstallArguments()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return "/c npm install -g azurite@latest";
        }
        return "install -g azurite@latest";
    }
}