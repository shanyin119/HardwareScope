using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Microsoft.Win32;

namespace HardwareScope.Services;

public static class HardwareAccessDriverService
{
    public const string DriverName = "PawnIO 2.2";
    public const string DownloadUrl = "https://github.com/namazso/PawnIO.Setup/releases/download/2.2.0/PawnIO_setup.exe";
    private const string ExpectedSha256 = "1F519A22E47187F70A1379A48CA604981C4FCF694F4E65B734AAA74A9FBA3032";
    private const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO";
    private const string ServiceKey = @"SYSTEM\CurrentControlSet\Services\PawnIO";

    public static bool IsInstalled()
    {
        try
        {
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using var key = baseKey.OpenSubKey(UninstallKey);
                if (key is not null) return true;
            }
            using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var service = machine.OpenSubKey(ServiceKey);
            if (service is not null) return true;
        }
        catch
        {
            // 注册表受限时由温度扫描结果继续判断，不让检测流程中断。
        }
        return false;
    }

    public static bool IsDriverAvailable()
    {
        try
        {
            using var handle = CreateFile(
                @"\\?\GLOBALROOT\Device\PawnIO",
                0xC0000000,
                0x00000003,
                IntPtr.Zero,
                3,
                0x00000080,
                IntPtr.Zero);
            return !handle.IsInvalid;
        }
        catch
        {
            return false;
        }
    }

    public static void TryStartInstalledDriver()
    {
        if (!IsInstalled() || IsDriverAvailable()) return;
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.SystemDirectory, "sc.exe"),
                Arguments = "start PawnIO",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            });
            process?.WaitForExit(4000);
        }
        catch
        {
            // 未安装或被系统策略阻止时由界面给出明确诊断，不中断其他温度读取。
        }
    }

    public static async Task<int> DownloadAndRunInstallerAsync(CancellationToken cancellationToken = default)
    {
        var downloadDirectory = Path.Combine(Path.GetTempPath(), "别离检测工具");
        Directory.CreateDirectory(downloadDirectory);
        var installerPath = Path.Combine(downloadDirectory, "PawnIO_setup_2.2.0.exe");

        if (!File.Exists(installerPath) || !HasExpectedHash(installerPath))
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            var bytes = await client.GetByteArrayAsync(DownloadUrl, cancellationToken);
            var actualHash = Convert.ToHexString(SHA256.HashData(bytes));
            if (!actualHash.Equals(ExpectedSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("下载的温度驱动校验失败，已停止安装。请稍后重试。");
            await File.WriteAllBytesAsync(installerPath, bytes, cancellationToken);
        }

        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = installerPath,
            UseShellExecute = true,
            Verb = "runas"
        }) ?? throw new InvalidOperationException("无法启动温度驱动安装程序。");
        await process.WaitForExitAsync(cancellationToken);
        return process.ExitCode;
    }

    private static bool HasExpectedHash(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(stream))
                .Equals(ExpectedSha256, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);
}
