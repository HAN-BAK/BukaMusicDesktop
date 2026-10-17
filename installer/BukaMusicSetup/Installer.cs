using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;

namespace BukaMusicSetup;

/// <summary>
/// 安装逻辑：从缤纷云下载程序包 → 解压到目标目录 → 建快捷方式 → 写卸载信息。
/// 界面在 <see cref="MainWindow"/>，命令行参数在 <see cref="App"/>。
/// </summary>
internal static class Installer
{
    private const string Endpoint = "https://s3.bitiful.net";
    private const string Host = "s3.bitiful.net";
    private const string Bucket = "bukamusic";
    private const string Region = "cn-east-1";
    private const string AccessKey = "Y0kOrrw8wOAo2OfyWbSrbKlp";
    private const string SecretKey = "88oHn5aqdHcwkE0w0nmrK1Wipj8DxA5";
    private const string AppExe = "BukaMusicDesktop.exe";
    private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\BukaMusicDesktop";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(30) };

    public static string DefaultDir() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BukaMusicDesktop");

    /// <summary>把最新版本的程序覆盖安装到 dir。</summary>
    public static async Task<string> InstallAsync(
        string dir, IProgress<(int Percent, string Text)>? progress, CancellationToken token)
    {
        progress?.Report((0, "正在获取版本信息…"));
        var latest = await FetchLatestAsync(token);
        if (latest == null || string.IsNullOrEmpty(latest.Value.Key))
            throw new Exception("无法获取最新版本信息，请检查网络");

        progress?.Report((2, $"正在下载 {latest.Value.Version}…"));
        string tempDir = Path.Combine(Path.GetTempPath(),
            "BukaMusicSetup-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tempDir);
        string zip = Path.Combine(tempDir, "app.zip");
        await DownloadAsync(latest.Value.Key, latest.Value.Size, zip,
            percent => progress?.Report((2 + percent * 78 / 100,
                $"正在下载 {latest.Value.Version}… {percent}%")), token);

        progress?.Report((82, "正在解压…"));
        string extractDir = Path.Combine(tempDir, "app");
        ZipFile.ExtractToDirectory(zip, extractDir, overwriteFiles: true);
        string source = FindAppDir(extractDir)
            ?? throw new Exception("安装包里没有找到 " + AppExe);

        progress?.Report((88, "正在安装…"));
        foreach (var process in Process.GetProcessesByName("BukaMusicDesktop"))
        {
            try { process.Kill(); process.WaitForExit(4000); } catch { }
        }
        Directory.CreateDirectory(dir);
        CopyDirectory(source, dir);

        try
        {
            string self = Environment.ProcessPath ?? "";
            if (self.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(self, Path.Combine(dir, "Uninstall.exe"), overwrite: true);
            }
        }
        catch { }

        progress?.Report((95, "正在创建快捷方式…"));
        CreateShortcuts(dir);
        WriteUninstallEntry(dir, latest.Value.Version);
        try
        {
            File.Delete(zip);
            Directory.Delete(extractDir, true);
            Directory.Delete(tempDir, true);
        }
        catch { }
        progress?.Report((100, "安装完成"));
        return dir;
    }

    private static async Task<(string Version, string Key, long Size)?> FetchLatestAsync(
        CancellationToken token)
    {
        string json = await Http.GetStringAsync(Presign("Windows/latest.json"), token);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        string version = root.TryGetProperty("version", out var v) ? v.GetString() ?? "" : "";
        string key = root.TryGetProperty("key", out var k) ? k.GetString() ?? "" : "";
        long size = root.TryGetProperty("size", out var s) ? s.GetInt64() : 0;
        return (version, key, size);
    }

    private static async Task DownloadAsync(string key, long size, string path,
                                            Action<int> progress, CancellationToken token)
    {
        using var response = await Http.GetAsync(Presign(key),
            HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        long total = response.Content.Headers.ContentLength ?? size;
        await using var input = await response.Content.ReadAsStreamAsync(token);
        await using var output = File.Create(path);
        byte[] buffer = new byte[128 * 1024];
        long done = 0;
        int last = -1;
        int n;
        while ((n = await input.ReadAsync(buffer, token)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, n), token);
            done += n;
            if (total > 0)
            {
                int percent = (int)(done * 100 / total);
                if (percent != last)
                {
                    last = percent;
                    progress(percent);
                }
            }
        }
    }

    private static string? FindAppDir(string root)
    {
        if (File.Exists(Path.Combine(root, AppExe))) return root;
        foreach (string dir in Directory.GetDirectories(root))
        {
            if (File.Exists(Path.Combine(dir, AppExe))) return dir;
        }
        string? found = Directory.GetFiles(root, AppExe, SearchOption.AllDirectories).FirstOrDefault();
        return found == null ? null : Path.GetDirectoryName(found);
    }

    private static void CopyDirectory(string source, string target)
    {
        foreach (string dir in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(dir.Replace(source, target));
        }
        foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            string dest = file.Replace(source, target);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(file, dest, overwrite: true);
        }
    }

    private static void CreateShortcuts(string dir)
    {
        try
        {
            string exe = Path.Combine(dir, AppExe);
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string startMenu = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Programs), "BukaMusic");
            Directory.CreateDirectory(startMenu);
            CreateShortcut(Path.Combine(desktop, "BukaMusic 控制台.lnk"), exe);
            CreateShortcut(Path.Combine(startMenu, "BukaMusic 控制台.lnk"), exe);
            CreateShortcut(Path.Combine(startMenu, "卸载 BukaMusic 控制台.lnk"),
                Path.Combine(dir, "Uninstall.exe"), "--uninstall");
        }
        catch { }
    }

    private static void CreateShortcut(string link, string target, string args = "")
    {
        Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType == null) return;
        dynamic shell = Activator.CreateInstance(shellType)!;
        dynamic shortcut = shell.CreateShortcut(link);
        shortcut.TargetPath = target;
        shortcut.WorkingDirectory = Path.GetDirectoryName(target);
        shortcut.Arguments = args;
        shortcut.IconLocation = target;
        shortcut.Save();
    }

    private static void WriteUninstallEntry(string dir, string version)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(UninstallKey);
            key.SetValue("DisplayName", "BukaMusic 控制台");
            key.SetValue("DisplayVersion", version);
            key.SetValue("Publisher", "HAN-BAK");
            key.SetValue("InstallLocation", dir);
            key.SetValue("DisplayIcon", Path.Combine(dir, AppExe));
            key.SetValue("UninstallString", $"\"{Path.Combine(dir, "Uninstall.exe")}\" --uninstall");
            key.SetValue("NoModify", 1, Microsoft.Win32.RegistryValueKind.DWord);
            key.SetValue("NoRepair", 1, Microsoft.Win32.RegistryValueKind.DWord);
        }
        catch { }
    }

    public static void Uninstall(string? dir)
    {
        dir ??= DefaultDir();
        if (!Directory.Exists(dir))
        {
            MessageBox.Show("没有找到安装目录：" + dir, "BukaMusic");
            return;
        }
        if (MessageBox.Show("确定要卸载 BukaMusic 控制台吗？\n" + dir, "卸载 BukaMusic",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }
        foreach (var process in Process.GetProcessesByName("BukaMusicDesktop"))
        {
            try { process.Kill(); process.WaitForExit(4000); } catch { }
        }
        try { Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false); } catch { }
        try
        {
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            File.Delete(Path.Combine(desktop, "BukaMusic 控制台.lnk"));
            string startMenu = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Programs), "BukaMusic");
            if (Directory.Exists(startMenu)) Directory.Delete(startMenu, true);
        }
        catch { }
        try
        {
            Process.Start(new ProcessStartInfo("cmd.exe",
                $"/c ping 127.0.0.1 -n 3 >nul & rmdir /s /q \"{dir}\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
            });
        }
        catch { }
        MessageBox.Show("已卸载。", "BukaMusic");
    }

    // ------------------------------------------------------------------
    // 缤纷云（S3 SigV4 预签名）
    // ------------------------------------------------------------------

    private static string Presign(string key)
    {
        string amzDate = DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        string date = amzDate[..8];
        string scope = $"{date}/{Region}/s3/aws4_request";
        string credential = $"{AccessKey}/{scope}";
        string canonicalUri = "/" + Bucket + "/" + EncodePath(key);
        string canonicalQuery =
            "X-Amz-Algorithm=AWS4-HMAC-SHA256" +
            "&X-Amz-Credential=" + Encode(credential) +
            "&X-Amz-Date=" + amzDate +
            "&X-Amz-Expires=3600" +
            "&X-Amz-SignedHeaders=host";
        string canonicalRequest =
            "GET\n" + canonicalUri + "\n" + canonicalQuery + "\n" +
            "host:" + Host + "\n\nhost\nUNSIGNED-PAYLOAD";
        string stringToSign =
            "AWS4-HMAC-SHA256\n" + amzDate + "\n" + scope + "\n" +
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalRequest))).ToLowerInvariant();
        byte[] kDate = Hmac(Encoding.UTF8.GetBytes("AWS4" + SecretKey), date);
        byte[] kRegion = Hmac(kDate, Region);
        byte[] kService = Hmac(kRegion, "s3");
        byte[] kSigning = Hmac(kService, "aws4_request");
        string signature = Convert.ToHexString(Hmac(kSigning, stringToSign)).ToLowerInvariant();
        return Endpoint + canonicalUri + "?" + canonicalQuery + "&X-Amz-Signature=" + signature;
    }

    private static byte[] Hmac(byte[] key, string value)
    {
        using var hmac = new HMACSHA256(key);
        return hmac.ComputeHash(Encoding.UTF8.GetBytes(value));
    }

    private static string EncodePath(string key)
    {
        var sb = new StringBuilder();
        foreach (string part in key.Split('/'))
        {
            if (sb.Length > 0) sb.Append('/');
            sb.Append(Encode(part));
        }
        return sb.ToString();
    }

    private static string Encode(string value)
    {
        var sb = new StringBuilder();
        foreach (byte b in Encoding.UTF8.GetBytes(value))
        {
            char c = (char)b;
            if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9')
                || c == '-' || c == '_' || c == '.' || c == '~')
            {
                sb.Append(c);
            }
            else
            {
                sb.Append('%').Append(b.ToString("X2"));
            }
        }
        return sb.ToString();
    }
}
