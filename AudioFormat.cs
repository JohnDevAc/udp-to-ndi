using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace UdpNdi;

public sealed record AudioFormat(int SampleRate, int Channels)
{
    public string Summary => $"{SampleRate / 1000.0:0.###} kHz · {Channels} ch";
}
internal sealed record AudioPacket(AudioFormat Format, int Samples, double Timestamp)
{
    public int BufferSize => checked(Format.Channels * Samples * sizeof(float));
}
internal static class AudioInfoParser
{
    static readonly Regex Frame = new(@"\bpts:(-?\d+)\s+pts_time:\S+.*\bfmt:flt\s+channels:(\d+).*\brate:(\d+)\s+nb_samples:(\d+)", RegexOptions.Compiled);
    public static AudioPacket? Parse(string line)
    {
        if (!line.Contains("ashowinfo_")) return null;
        var match = Frame.Match(line);
        if (!match.Success) return null;
        int rate = int.Parse(match.Groups[3].Value);
        var packet = new AudioPacket(new(rate, int.Parse(match.Groups[2].Value)), int.Parse(match.Groups[4].Value), long.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) / (double)rate);
        if (packet.Format.SampleRate is < 8000 or > 384000 || packet.Format.Channels is < 1 or > 64 || packet.Samples is < 1 or > 384000 || !double.IsFinite(packet.Timestamp)) throw new InvalidDataException("Unsupported detected audio format.");
        return packet;
    }
}

// One timeline for both output threads, preserving their source PTS offsets.
internal sealed class MediaClock
{
    readonly object gate = new();
    bool started;
    double originPts, originClock;
    long originUtc;
    public async Task<long> WaitAsync(double pts, CancellationToken token)
    {
        double delay;
        long timecode;
        lock (gate)
        {
            var now = (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency;
            if (!started || Math.Abs(pts - originPts - (now - originClock)) > 5)
            {
                started = true; originPts = pts; originClock = now + 0.15;
                originUtc = (DateTime.UtcNow - DateTime.UnixEpoch).Ticks + 1_500_000;
            }
            delay = originClock + pts - originPts - now;
            timecode = originUtc + (long)((pts - originPts) * 10_000_000);
        }
        if (delay > 0) await Task.Delay(TimeSpan.FromSeconds(delay), token);
        return timecode;
    }
}
