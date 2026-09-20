using System.Net;
using System.Text.Json;

namespace UdpNdi;

public sealed record SlotSettings
{
    public string Name { get; set; } = "UDP 01";
    public string Input { get; set; } = "RTP H264";
    public string Address { get; set; } = "0.0.0.0";
    public int Port { get; set; } = 5000;
    public int Payload { get; set; } = 96;
    public string Interface { get; set; } = "";
    public string SdpPath { get; set; } = "";
    public string Fmtp { get; set; } = "";
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name) || Name.Length > 100) throw new ArgumentException("Enter an NDI name (1–100 characters).");
        if (Input is not ("RTP H264" or "RTP H265" or "RTP MPEG-TS" or "UDP MPEG-TS" or "SDP file")) throw new ArgumentException("Unsupported input type.");
        if (Input == "SDP file") { if (!File.Exists(SdpPath)) throw new ArgumentException("Choose an existing SDP file."); }
        else if (!IPAddress.TryParse(Address, out var ip) || ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) throw new ArgumentException("Use an IPv4 local address, 0.0.0.0, or a multicast group.");
        if (Interface.Length > 0 && (!IPAddress.TryParse(Interface, out var nic) || nic.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)) throw new ArgumentException("The interface must be a local IPv4 address, or blank for automatic.");
        if (Port is < 1024 or > 65534) throw new ArgumentException("Use a port from 1024 to 65534; RTP also uses the following port for RTCP.");
        if (Payload is < 0 or > 127) throw new ArgumentException("RTP payload type must be 0–127.");
        if (Fmtp.Contains('\n') || Fmtp.Contains('\r')) throw new ArgumentException("Enter FMTP parameters on one line.");
    }
    public string Sdp()
    {
        var codec = Input == "RTP H265" ? "H265" : Input == "RTP MPEG-TS" ? "MP2T" : "H264";
        var address = Address;
        if (IPAddress.Parse(address).GetAddressBytes()[0] is >= 224 and <= 239) address += "/32";
        return $"v=0\r\no=- 0 0 IN IP4 127.0.0.1\r\ns=UDP to NDI\r\nc=IN IP4 {address}\r\nt=0 0\r\nm=video {Port} RTP/AVP {Payload}\r\na=rtpmap:{Payload} {codec}/90000\r\n" +
            (Fmtp.Length > 0 ? $"a=fmtp:{Payload} {Fmtp}\r\n" : "");
    }
}

public sealed class AppSettings
{
    public string Ffmpeg { get; set; } = Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg.exe");
    public List<SlotSettings> Slots { get; set; } = Enumerable.Range(1, 10).Select(i => new SlotSettings { Name = $"UDP {i:00}", Port = 5000 + (i - 1) * 2 }).ToList();
    public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UdpToNdi");
    public static string FilePath => Path.Combine(Folder, "settings.json");
    public static AppSettings Load()
    {
        if (!File.Exists(FilePath)) return new();
        var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? throw new InvalidDataException("Empty settings file.");
        if (settings.Slots is null || settings.Slots.Count != 10 || settings.Slots.Any(x => x is null)) throw new InvalidDataException("Settings must contain exactly ten slots.");
        var bundledFfmpeg = Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg.exe");
        if (!File.Exists(settings.Ffmpeg) && File.Exists(bundledFfmpeg)) settings.Ffmpeg = bundledFfmpeg;
        return settings;
    }
    public void Save()
    {
        Directory.CreateDirectory(Folder);
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, FilePath, true);
    }
}
