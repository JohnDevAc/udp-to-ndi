using System.Globalization;
using System.Text.RegularExpressions;

namespace UdpNdi;

public sealed record VideoFormat(int Width, int Height, int RateN, int RateD, int SarN, int SarD, bool Interlaced)
{
    public float Aspect => (float)((double)Width * SarN / (Height * (double)SarD));
    public int BufferSize => checked(Width * Height * 2);
    public string Summary => $"{Width}×{Height}{(Interlaced ? "i" : "p")} {(double)RateN / RateD:0.###}";
    public string Description => $"{Width}×{Height}, {RateN}/{RateD} fps, {(Interlaced ? "interlaced (top field first)" : "progressive")}, pixel aspect {SarN}:{SarD}, display aspect {Aspect:0.###}:1";
}

// showinfo is placed after the last video filter. Each record describes exactly
// one packed raw frame in stdout; no resizing or frame-rate conversion follows it.
internal sealed class FrameInfoParser
{
    static readonly Regex Config = new(@"config in time_base: (\d+)/(\d+), frame_rate: (\d+)/(\d+)", RegexOptions.Compiled);
    static readonly Regex Frame = new(@"\bn:\s*\d+.*\bfmt:uyvy422\b.*\bsar:(\d+)/(\d+)\s+s:(\d+)x(\d+)\s+i:([PTB])", RegexOptions.Compiled);
    static readonly Regex Duration = new(@"\bduration:\s*(\d+)\b", RegexOptions.Compiled);
    static readonly Regex Pts = new(@"\bpts_time:(\S+)", RegexOptions.Compiled);
    static readonly Regex ExactPts = new(@"\bpts:\s*(-?\d+)\b", RegexOptions.Compiled);
    public double Timestamp { get; private set; }
    int rateN, rateD, timeN, timeD;
    public VideoFormat? Parse(string line)
    {
        if (!line.Contains("showinfo_", StringComparison.Ordinal)) return null;
        var config = Config.Match(line);
        if (config.Success)
        {
            timeN = Value(config, 1); timeD = Value(config, 2);
            rateN = Value(config, 3); rateD = Value(config, 4); return null;
        }
        var frame = Frame.Match(line);
        if (!frame.Success) return null;
        var pts = Pts.Match(line);
        var exactPts = ExactPts.Match(line);
        Timestamp = exactPts.Success && timeD > 0 ? long.Parse(exactPts.Groups[1].Value, CultureInfo.InvariantCulture) * (double)timeN / timeD : pts.Success && double.TryParse(pts.Groups[1].Value, CultureInfo.InvariantCulture, out var seconds) && double.IsFinite(seconds) ? seconds : Timestamp + (rateN > 0 ? (double)rateD / rateN : 0);
        int n = rateN, d = rateD;
        var duration = Duration.Match(line);
        if (duration.Success && Value(duration, 1) > 0 && timeN > 0 && timeD > 0)
        {
            int durationD = checked(timeN * Value(duration, 1));
            // Demuxer nominal rate can remain stale after an encoder changes rate.
            // Keep exact nominal fractions when duration only differs by timestamp rounding.
            if (n <= 0 || d <= 0 || Math.Abs(Value(duration, 1) - (double)timeD * d / timeN / n) > 1.0)
            {
                n = timeD; d = durationD;
                int a = n, b = d; while (b != 0) { int remainder = a % b; a = b; b = remainder; }
                n /= a; d /= a;
            }
        }
        if (n <= 0 || d <= 0) throw new InvalidDataException("The source does not provide a usable frame rate or frame duration.");
        int sarN = Value(frame, 1), sarD = Value(frame, 2);
        if (sarN == 0 || sarD == 0) { sarN = 1; sarD = 1; }
        var result = new VideoFormat(Value(frame, 3), Value(frame, 4), n, d, sarN, sarD, frame.Groups[5].Value != "P");
        if (result.Width < 2 || result.Width > 8192 || result.Height < 1 || result.Height > 4320 || result.Width % 2 != 0 || (double)n / d > 240)
            throw new InvalidDataException("Unsupported detected video format. Use an even width, up to 8192×4320 and 240 fps.");
        return result;
    }
    static int Value(Match match, int group) => int.Parse(match.Groups[group].Value, CultureInfo.InvariantCulture);
}
