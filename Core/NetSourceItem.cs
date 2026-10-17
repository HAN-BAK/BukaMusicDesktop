namespace BukaMusicDesktop.Core;

/// <summary>One network music path stored on the Android device.</summary>
public sealed class NetSourceItem
{
    public string Id { get; set; } = "";
    public string Type { get; set; } = "SMB";
    public string Name { get; set; } = "";
    public string Host { get; set; } = "";
    public int Port { get; set; }
    public string Path { get; set; } = "";
    public string User { get; set; } = "";
    public string Password { get; set; } = "";
    public string Domain { get; set; } = "";

    public string TypeLabel => Type switch
    {
        "WEBDAV" => "WebDAV",
        "FTP" => "FTP",
        _ => "SMB",
    };

    public string Title => string.IsNullOrWhiteSpace(Name)
        ? $"{TypeLabel} · {Host}" : $"{Name}";

    public string Summary
    {
        get
        {
            string where = Host + (Port > 0 ? ":" + Port : "") + "/" + Path;
            return TypeLabel + " · " + where.TrimEnd('/');
        }
    }

    public NetSourceItem Clone() => (NetSourceItem)MemberwiseClone();
}
