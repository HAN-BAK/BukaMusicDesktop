using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace BukaMusicDesktop.Core;

/// <summary>Version information published at Windows/latest.json.</summary>
public sealed class DesktopUpdateInfo
{
    public string Version { get; set; } = "";
    public int VersionCode { get; set; }
    public string ZipKey { get; set; } = "";
    public string SetupKey { get; set; } = "";
    public long Size { get; set; }
    public string Notes { get; set; } = "";
}

/// <summary>
/// Update check / download for the Windows console. Packages live in the
/// Bitiful S4 bucket; the bucket is private, so requests are signed with
/// SigV4 query authentication at run time.
/// </summary>
public static class DesktopUpdate
{
    private const string Endpoint = "https://s3.bitiful.net";
    private const string Host = "s3.bitiful.net";
    private const string Bucket = "bukamusic";
    private const string Region = "cn-east-1";
    private const string AccessKey = "Y0kOrrw8wOAo2OfyWbSrbKlp";
    private const string SecretKey = "88oHn5aqdHcwkE0w0nmrK1Wipj8DxA5";
    private const string LatestKey = "Windows/latest.json";
    private const long UrlTtlSeconds = 3600;

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(30) };

    public static string CurrentVersion
    {
        get
        {
            var v = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);
            return $"{v.Major}.{v.Minor}.{Math.Max(0, v.Build)}";
        }
    }

    public static int CurrentVersionCode
    {
        get
        {
            var v = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);
            return v.Major * 100 + v.Minor * 10 + Math.Max(0, v.Build);
        }
    }

    /// <summary>Reads Windows/latest.json, or null when unreachable.</summary>
    public static async Task<DesktopUpdateInfo?> CheckAsync()
    {
        try
        {
            string json = await Http.GetStringAsync(Presign(LatestKey)).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            return new DesktopUpdateInfo
            {
                Version = root.TryGetProperty("version", out var ver) ? ver.GetString() ?? "" : "",
                VersionCode = root.TryGetProperty("versionCode", out var code) ? code.GetInt32() : 0,
                ZipKey = root.TryGetProperty("key", out var key) ? key.GetString() ?? "" : "",
                SetupKey = root.TryGetProperty("setupKey", out var setup) ? setup.GetString() ?? "" : "",
                Size = root.TryGetProperty("size", out var size) ? size.GetInt64() : 0,
                Notes = root.TryGetProperty("notes", out var notes) ? notes.GetString() ?? "" : "",
            };
        }
        catch (Exception ex)
        {
            LogBus.Warn("检查更新失败：" + ex.Message);
            return null;
        }
    }

    /// <summary>Downloads the setup program; returns the saved path or null.</summary>
    public static async Task<string?> DownloadSetupAsync(DesktopUpdateInfo info,
                                                        IProgress<int>? progress = null)
    {
        if (info == null || string.IsNullOrEmpty(info.SetupKey)) return null;
        try
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "BukaMusicUpdate");
            Directory.CreateDirectory(dir);
            string target = Path.Combine(dir, "BukaMusicSetup.exe");
            string part = target + ".part";
            using (var response = await Http.GetAsync(Presign(info.SetupKey),
                       HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                long total = response.Content.Headers.ContentLength ?? info.Size;
                await using var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                await using var output = File.Create(part);
                byte[] buffer = new byte[128 * 1024];
                long done = 0;
                int last = -1;
                int n;
                while ((n = await input.ReadAsync(buffer).ConfigureAwait(false)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, n)).ConfigureAwait(false);
                    done += n;
                    if (total > 0)
                    {
                        int percent = (int)(done * 100 / total);
                        if (percent != last)
                        {
                            last = percent;
                            progress?.Report(percent);
                        }
                    }
                }
            }
            if (File.Exists(target)) File.Delete(target);
            File.Move(part, target);
            return target;
        }
        catch (Exception ex)
        {
            LogBus.Error("下载更新失败：" + ex.Message);
            return null;
        }
    }

    /// <summary>Runs the downloaded setup (silent update into the current folder).</summary>
    public static void RunSetup(string path)
    {
        string installDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        Process.Start(new ProcessStartInfo(path)
        {
            UseShellExecute = true,
            Arguments = $"--silent --dir \"{installDir}\"",
        });
    }

    // ------------------------------------------------------------------
    // S3 SigV4 presign (GET)
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
            "&X-Amz-Expires=" + UrlTtlSeconds +
            "&X-Amz-SignedHeaders=host";
        string canonicalRequest =
            "GET\n" + canonicalUri + "\n" + canonicalQuery + "\n" +
            "host:" + Host + "\n\nhost\nUNSIGNED-PAYLOAD";
        string stringToSign =
            "AWS4-HMAC-SHA256\n" + amzDate + "\n" + scope + "\n" + Sha256Hex(canonicalRequest);
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

    private static string Sha256Hex(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

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
