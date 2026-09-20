using System.Diagnostics;

namespace UdpNdi;

internal static class RecoveryTest
{
    public static int Run()
    {
        Directory.CreateDirectory("test-results");
        using var log = new StreamWriter("test-results/recovery-test.txt", false) { AutoFlush = true };
        try { RunAsync(log).GetAwaiter().GetResult(); log.WriteLine("PASS: recovery tests completed."); return 0; }
        catch (Exception ex) { log.WriteLine("FAIL: " + ex); return 1; }
    }
    static async Task RunAsync(TextWriter log)
    {
        var runner = new SlotRunner();
        var messages = new System.Collections.Concurrent.ConcurrentQueue<string>(); runner.Log += messages.Enqueue;
        var ffmpeg = Path.GetFullPath("tools/ffmpeg.exe");
        Process? source = null; Task<string>? errors = null;
        async Task Wait(Func<bool> condition, int seconds, string check)
        {
            var time = Stopwatch.StartNew();
            while (!condition() && time.Elapsed.TotalSeconds < seconds) await Task.Delay(100);
            while (messages.TryDequeue(out var message)) if (message.Contains("20 seconds") || message.Contains("detected")) log.WriteLine(message);
            if (!condition()) throw new Exception(check + $"; state={runner.State}; frames={runner.Frames}; audio={runner.AudioSamples}; detail={runner.Detail}");
            log.WriteLine("PASS: " + check);
        }
        void StartSource()
        {
            var info = new ProcessStartInfo(ffmpeg) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
            foreach (var arg in new[] { "-hide_banner", "-loglevel", "error", "-re", "-f", "lavfi", "-i", "testsrc2=size=320x180:rate=25", "-f", "lavfi", "-i", "sine=frequency=1000:sample_rate=48000", "-c:v", "libx264", "-preset", "ultrafast", "-tune", "zerolatency", "-threads", "1", "-g", "25", "-c:a", "aac", "-ac", "2", "-f", "mpegts", "udp://127.0.0.1:25300?pkt_size=1316" }) info.ArgumentList.Add(arg);
            source = Process.Start(info)!; errors = source.StandardError.ReadToEndAsync();
        }
        async Task StopSource()
        {
            if (source is null) return;
            if (!source.HasExited) source.Kill(true);
            await source.WaitForExitAsync(); if (errors is not null) await errors;
            source.Dispose(); source = null;
        }
        try
        {
            runner.Start(new SlotSettings { Name = "UDP-NDI-recovery-test", Input = "UDP MPEG-TS", Address = "127.0.0.1", Port = 25300 }, ffmpeg);
            await Wait(() => runner.Retries >= 2 && runner.Running, 55, "Source absent: slot survives two full timeout/retry cycles");
            var sender = runner.Sender;
            StartSource();
            await Wait(() => runner.Frames >= 10 && runner.AudioSamples > 10000, 25, "Video and audio start automatically when source appears");
            await StopSource();
            int retryCount = runner.Retries;
            await Wait(() => runner.Retries > retryCount && runner.Running, 30, "Source failure starts another retry without stopping the slot");
            long oldFrames = runner.Frames, oldAudio = runner.AudioSamples;
            StartSource();
            await Wait(() => runner.Frames > oldFrames + 10 && runner.AudioSamples > oldAudio + 10000, 25, "Video and audio both recover automatically");
            if (runner.Sender != sender) throw new Exception("NDI sender changed during recovery.");
            log.WriteLine("PASS: NDI sender instance remains available across the outage.");
            var stopWatch = Stopwatch.StartNew(); await runner.StopAsync();
            if (runner.Running || stopWatch.Elapsed.TotalSeconds > 5) throw new Exception("Manual stop did not finish promptly.");
            log.WriteLine("PASS: manual Stop cancels retries and media processing promptly.");
        }
        finally { await StopSource(); await runner.StopAsync(); }
    }
}
