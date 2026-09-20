using System.Diagnostics;
using System.Runtime.InteropServices;

namespace UdpNdi;

internal static class SelfTest
{
    public static int RunTen()
    {
        Directory.CreateDirectory("test-results");
        using var file = new StreamWriter("test-results/ten-stream-test.txt", false) { AutoFlush = true };
        var output = TextWriter.Synchronized(file);
        try
        {
            Ndi.Initialize();
            int ready = 0;
            var allReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            async Task Hold()
            {
                if (Interlocked.Increment(ref ready) == 10) allReady.TrySetResult();
                await allReady.Task.WaitAsync(TimeSpan.FromSeconds(60));
                await Task.Delay(10000);
            }
            Task.WhenAll(Enumerable.Range(0, 10).Select(i => Task.Run(() => TestStream(
                Path.GetFullPath("tools/ffmpeg.exe"), "127.0.0.1", "UDP MPEG-TS", 25400 + i * 2,
                output, 640, 360, 25, audioRate: 48000, audioChannels: 2, hold: Hold)))).GetAwaiter().GetResult();
            output.WriteLine("PASS: ten concurrent 640×360 / 25 fps UDP audio/video streams received through ten NDI outputs; all held live together for 10 seconds.");
            return 0;
        }
        catch (Exception ex) { output.WriteLine("FAIL: " + ex); return 1; }
    }
    public static int Run(bool formatChangeOnly = false)
    {
        Directory.CreateDirectory("test-results");
        using var output = new StreamWriter("test-results/self-test.txt", false) { AutoFlush = true };
        try
        {
            if (formatChangeOnly) { TestFormatChange(Path.GetFullPath("tools/ffmpeg.exe"), output).GetAwaiter().GetResult(); return 0; }
            var s = new SlotSettings(); s.Validate();
            Check(new AppSettings().Slots.Count == 10, "Ten default slots", output);
            Check(s.Sdp().Contains("H264/90000"), "H264 SDP", output);
            Check((s with { Input = "RTP H265" }).Sdp().Contains("H265/90000"), "H265 SDP", output);
            Check((s with { Input = "RTP MPEG-TS", Payload = 33 }).Sdp().Contains("a=rtpmap:33 MP2T/90000"), "MPEG-TS SDP", output);
            Check((s with { Address = "239.10.10.1" }).Sdp().Contains("239.10.10.1/32"), "Multicast SDP", output);
            foreach (var bad in new[] { s with { Port = 65535 }, s with { Address = "hostname" }, s with { Payload = 128 }, s with { Fmtp = "bad\r\ninjection" } })
            {
                bool rejected = false; try { bad.Validate(); } catch (ArgumentException) { rejected = true; } Check(rejected, "Reject invalid slot", output);
            }
            Check(Marshal.SizeOf<Ndi.Video>() == 72 && Marshal.SizeOf<Ndi.SenderSettings>() == 24, "NDI x64 ABI sizes", output);
            var parser = new FrameInfoParser();
            parser.Parse("[Parsed_showinfo_2] config in time_base: 1/90000, frame_rate: 60000/1001");
            var rounded = parser.Parse("[Parsed_showinfo_2] n: 0 duration: 1502 fmt:uyvy422 sar:1/1 s:1920x1080 i:P");
            Check(rounded is { RateN: 60000, RateD: 1001 }, "Fractional rates survive timestamp rounding", output);
            var changed = parser.Parse("[Parsed_showinfo_2] n: 1 duration: 1500 fmt:uyvy422 sar:1/1 s:1920x1080 i:P");
            Check(changed is { RateN: 60, RateD: 1 }, "Frame duration detects rate changes despite stale nominal rate", output);
            string ffmpeg = Path.GetFullPath("tools/ffmpeg.exe");
            Ndi.Initialize();
            TestStream(ffmpeg, "127.0.0.1", "RTP H264", 25100, output, 1280, 720, 50).GetAwaiter().GetResult();
            TestStream(ffmpeg, "239.250.44.1", "RTP H264", 25102, output, 640, 360, 30000, 1001).GetAwaiter().GetResult();
            TestStream(ffmpeg, "127.0.0.1", "RTP MPEG-TS", 25104, output, 720, 576, 25, 1, 16, 15, true, 48000, 2).GetAwaiter().GetResult();
            TestStream(ffmpeg, "127.0.0.1", "RTP H265", 25106, output, 960, 540, 60000, 1001).GetAwaiter().GetResult();
            TestStream(ffmpeg, "127.0.0.1", "UDP MPEG-TS", 25108, output, 720, 576, 25, 1, 64, 45, false, 44100, 1).GetAwaiter().GetResult();
            TestStream(ffmpeg, "127.0.0.1", "SDP file", 25120, output, audioRate: 48000, audioChannels: 2).GetAwaiter().GetResult();
            TestFormatChange(ffmpeg, output).GetAwaiter().GetResult();
            var runners = Enumerable.Range(0, 10).Select(_ => new SlotRunner()).ToArray();
            try
            {
                for (int i = 0; i < 10; i++) runners[i].Start(new SlotSettings { Name = $"UDP-NDI-test-{i}", Port = 25200 + i * 2 }, ffmpeg);
                Thread.Sleep(1500);
                Check(runners.All(r => r.Running), "All ten slots run independently without input", output);
            }
            finally { Task.WhenAll(runners.Select(r => r.StopAsync())).GetAwaiter().GetResult(); }
            Check(runners.All(r => !r.Running && r.State == "Stopped"), "All ten waiting slots stop cleanly", output);
            output.WriteLine("PASS: all tests completed. Shared NDI configuration was not changed."); return 0;
        }
        catch (Exception ex) { output.WriteLine("FAIL: " + ex); return 1; }
    }
    static void Check(bool condition, string name, TextWriter output)
    { if (!condition) throw new Exception(name); output.WriteLine("PASS: " + name); }

    static async Task TestFormatChange(string ffmpeg, TextWriter output)
    {
        var runner = new SlotRunner();
        runner.Log += message => { if (message.Contains("detected")) output.WriteLine(message); };
        Process? source = null; Task<string>? errors = null;
        try
        {
            runner.Start(new SlotSettings { Name = "UDP-NDI-format-change", Input = "UDP MPEG-TS", Port = 25110, Address = "127.0.0.1" }, ffmpeg);
            foreach (var (width, height, rate) in new[] { (320, 180, 25), (640, 360, 50), (640, 360, 30) })
            {
                var info = new ProcessStartInfo(ffmpeg) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
                foreach (var arg in new[] { "-hide_banner", "-loglevel", "error", "-re", "-f", "lavfi", "-i", $"testsrc2=size={width}x{height}:rate={rate}", "-an", "-c:v", "libx264", "-preset", "ultrafast", "-tune", "zerolatency", "-g", "25", "-threads", "1", "-f", "mpegts", "udp://127.0.0.1:25110?pkt_size=1316" }) info.ArgumentList.Add(arg);
                source = Process.Start(info)!; errors = source.StandardError.ReadToEndAsync();
                var timeout = Stopwatch.StartNew();
                while (timeout.Elapsed.TotalSeconds < 15 && !(runner.DetectedFormat is { } f && f.Width == width && f.Height == height && f.RateN == rate * f.RateD)) await Task.Delay(100);
                var current = runner.DetectedFormat;
                Check(current is not null && current.Width == width && current.Height == height && current.RateN == rate * current.RateD, $"Live format change to {width}×{height} / {rate} fps (observed {current?.Description})", output);
                var framesBefore = runner.Frames;
                await Task.Delay(500);
                Check(runner.Frames > framesBefore + 5 && runner.State == "Live", "Video continues after format detection", output);
                source.Kill(true); await source.WaitForExitAsync(); await errors; source.Dispose(); source = null;
            }
        }
        finally
        {
            if (source is not null) { if (!source.HasExited) source.Kill(true); await source.WaitForExitAsync(); if (errors is not null) await errors; source.Dispose(); }
            await runner.StopAsync();
        }
    }

    static async Task TestStream(string ffmpeg, string address, string format, int port, TextWriter output, int width = 320, int height = 180, int rateN = 25, int rateD = 1, int sarN = 1, int sarD = 1, bool interlaced = false, int audioRate = 0, int audioChannels = 2, Func<Task>? hold = null)
    {
        var runner = new SlotRunner();
        var logs = new System.Collections.Concurrent.ConcurrentQueue<string>(); runner.Log += logs.Enqueue;
        var s = new SlotSettings { Name = $"UDP-NDI-verification-{port}-{Guid.NewGuid():N}", Address = address, Port = port, Input = format, Payload = format == "RTP MPEG-TS" ? 33 : 96 };
        string? testSdp = null;
        if (format == "SDP file")
        {
            testSdp = Path.Combine(Path.GetTempPath(), $"udp-ndi-av-{Guid.NewGuid():N}.sdp");
            File.WriteAllText(testSdp, $"v=0\r\no=- 0 0 IN IP4 127.0.0.1\r\ns=AV test\r\nc=IN IP4 127.0.0.1\r\nt=0 0\r\nm=video {port} RTP/AVP 96\r\na=rtpmap:96 H264/90000\r\nm=audio {port + 2} RTP/AVP 97\r\na=rtpmap:97 L16/{audioRate}/{audioChannels}\r\n");
            s.SdpPath = testSdp;
        }
        Process? source = null; IntPtr receiver = IntPtr.Zero; IntPtr finder = IntPtr.Zero; IntPtr directUrl = IntPtr.Zero; Task<string>? sourceErrors = null;
        try
        {
            runner.Start(s, ffmpeg);
            await Task.Delay(700);
            var info = new ProcessStartInfo(ffmpeg) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
            foreach (var arg in new[] { "-hide_banner", "-loglevel", "error", "-re", "-f", "lavfi", "-i", $"testsrc2=size={width}x{height}:rate={rateN * (interlaced ? 2 : 1)}/{rateD}" }) info.ArgumentList.Add(arg);
            if (audioRate > 0) foreach (var arg in new[] { "-f", "lavfi", "-i", $"sine=frequency=997:sample_rate={audioRate}", "-c:a", "aac", "-ac", audioChannels.ToString() }) info.ArgumentList.Add(arg);
            else info.ArgumentList.Add("-an");
            foreach (var arg in new[] { "-vf", (interlaced ? "tinterlace=interleave_top," : "") + $"setsar={sarN}/{sarD}", "-c:v", format == "RTP H265" ? "libx265" : "libx264", "-preset", "ultrafast", "-tune", "zerolatency", "-g", "25", "-threads", "1" }) info.ArgumentList.Add(arg);
            if (interlaced) { info.ArgumentList.Add("-flags"); info.ArgumentList.Add("+ilme+ildct"); info.ArgumentList.Add("-x264-params"); info.ArgumentList.Add("tff=1"); }
            if (format == "RTP H265") { info.ArgumentList.Add("-x265-params"); info.ArgumentList.Add("pools=1:repeat-headers=1:log-level=error"); }
            if (format == "SDP file") { info.ArgumentList.Add("-map"); info.ArgumentList.Add("0:v:0"); info.ArgumentList.Add("-an"); }
            info.ArgumentList.Add("-f"); info.ArgumentList.Add(format == "UDP MPEG-TS" ? "mpegts" : format == "RTP MPEG-TS" ? "rtp_mpegts" : "rtp");
            if (format != "UDP MPEG-TS") { info.ArgumentList.Add("-payload_type"); info.ArgumentList.Add(s.Payload.ToString()); }
            info.ArgumentList.Add($"{(format == "UDP MPEG-TS" ? "udp" : "rtp")}://{address}:{port}?pkt_size=1200&ttl=1");
            if (format == "SDP file") foreach (var arg in new[] { "-map", "1:a:0", "-vn", "-c:a", "pcm_s16be", "-ac", audioChannels.ToString(), "-f", "rtp", "-payload_type", "97", $"rtp://{address}:{port + 2}?pkt_size=1200" }) info.ArgumentList.Add(arg);
            source = Process.Start(info)!; sourceErrors = source.StandardError.ReadToEndAsync();
            var timeout = Stopwatch.StartNew();
            while (runner.Frames < 10 && timeout.Elapsed.TotalSeconds < 18) await Task.Delay(100);
            if (runner.Frames < 10) throw new Exception($"No converted frames for {format} {address}: {string.Join("\n", logs.TakeLast(20))}");
            Check(true, $"{format} {address}: decoded {runner.Frames} frames and submitted to NDI", output);
            var detected = runner.DetectedFormat!;
            Check(detected.Width == width && detected.Height == height && (long)detected.RateN * rateD == (long)rateN * detected.RateD && detected.Interlaced == interlaced && Math.Abs(detected.Aspect - (double)width * sarN / height / sarD) < 0.001, $"Auto-detected {detected.Description}", output);
            var nativeSource = Marshal.PtrToStructure<Ndi.Source>(Ndi.NDIlib_send_get_source_name(runner.Sender));
            var sourceName = Marshal.PtrToStringUTF8(nativeSource.Name);
            finder = Ndi.NDIlib_find_create_v2(IntPtr.Zero);
            timeout.Restart();
            while (timeout.Elapsed.TotalSeconds < 10)
            {
                var sources = Ndi.NDIlib_find_get_current_sources(finder, out var count);
                bool found = false;
                for (int i = 0; i < count; i++)
                {
                    var candidate = Marshal.PtrToStructure<Ndi.Source>(sources + i * Marshal.SizeOf<Ndi.Source>());
                    if (Marshal.PtrToStringUTF8(candidate.Name) == sourceName) { nativeSource = candidate; found = true; break; }
                }
                if (found) break;
                await Task.Delay(200);
            }
            output.WriteLine($"NDI source: {Marshal.PtrToStringUTF8(nativeSource.Name)}; endpoint: {Marshal.PtrToStringUTF8(nativeSource.Url)}");
            var advertised = Marshal.PtrToStringUTF8(nativeSource.Url);
            if (Uri.TryCreate("ndi://" + advertised, UriKind.Absolute, out var endpoint) && endpoint.Port > 0)
            {
                var localIps = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces().SelectMany(n => n.GetIPProperties().UnicastAddresses).Select(a => a.Address.ToString());
                if (!localIps.Contains(endpoint.Host))
                {
                    output.WriteLine($"NOTE: Discovery advertises non-local address {endpoint.Host}; verifying video with a direct loopback connection to sender port {endpoint.Port}. Windows NDI settings are unchanged.");
                    directUrl = Marshal.StringToCoTaskMemUTF8($"127.0.0.1:{endpoint.Port}");
                    nativeSource = new Ndi.Source { Url = directUrl };
                }
            }
            var recvSettings = new Ndi.ReceiverSettings { Source = nativeSource, Color = 0, Bandwidth = 100, Fields = true };
            receiver = Ndi.NDIlib_recv_create_v3(ref recvSettings);
            Check(receiver != IntPtr.Zero, "Create NDI receiver", output);
            timeout.Restart(); bool received = false; long videoTimecode = 0;
            while (timeout.Elapsed.TotalSeconds < 12)
            {
                var frame = new Ndi.Video();
                if (Ndi.NDIlib_recv_capture_v2(receiver, ref frame, IntPtr.Zero, IntPtr.Zero, 500) != 1) continue;
                try
                {
                    output.WriteLine($"Received NDI: {frame.Width}×{frame.Height} {frame.RateN}/{frame.RateD} fps; aspect {frame.Aspect}; scan enum {frame.Format}");
                    videoTimecode = frame.Timecode;
                    received = frame.Width == width && frame.Height == height && frame.Data != IntPtr.Zero && (long)frame.RateN * rateD == (long)rateN * frame.RateD && Math.Abs(frame.Aspect - detected.Aspect) < 0.001 && frame.Format == (interlaced ? 0 : 1);
                    if (received)
                    {
                        var pixels = new byte[Math.Min(Math.Abs(frame.Stride), width * 2)];
                        Marshal.Copy(frame.Data, pixels, 0, pixels.Length);
                        Check(pixels.Max() - pixels.Min() > 40, $"{port}: NDI picture contains non-uniform test-pattern pixels", output);
                    }
                }
                finally { Ndi.NDIlib_recv_free_video_v2(receiver, ref frame); }
                if (received) break;
            }
            output.WriteLine($"Sender frames: {runner.Frames}; connections: {runner.Receivers}; state: {runner.State}");
            Check(received, $"{format} {address}: NDI receiver mirrors resolution, exact rate, aspect and scan type", output);
            if (audioRate > 0)
            {
                bool heard = false; timeout.Restart();
                while (timeout.Elapsed.TotalSeconds < 8)
                {
                    var audio = new Ndi.Audio();
                    if (Ndi.CaptureAudio(receiver, IntPtr.Zero, ref audio, IntPtr.Zero, 500) != 2) continue;
                    try
                    {
                        var samples = new float[audio.Samples]; Marshal.Copy(audio.Data, samples, 0, samples.Length);
                        var peak = samples.Max(x => Math.Abs(x));
                        heard = audio.SampleRate == audioRate && audio.Channels == audioChannels && peak > 0.03 && peak < 2;
                        output.WriteLine($"NDI audio: {audio.SampleRate} Hz, {audio.Channels} channels, peak {peak:0.000}, timecode {audio.Timecode}");
                        Check(Math.Abs((double)audio.Timecode - videoTimecode) < 5_000_000, "Audio/video timestamps share a common timeline (within 500 ms during startup)", output);
                    }
                    finally { Ndi.NDIlib_recv_free_audio_v2(receiver, ref audio); }
                    if (heard) break;
                }
                Check(heard && runner.AudioSamples > 0, "NDI receiver gets decoded audio with source rate, channels and audible samples", output);
            }
            if (hold is not null)
            {
                long previousFrames = runner.Frames, previousAudio = runner.AudioSamples;
                await hold();
                Check(runner.State == "Live" && runner.Frames > previousFrames + 100 && runner.AudioSamples > previousAudio + 100000,
                    $"{port}: video and audio continue while all ten slots are active", output);
                var latest = new Ndi.Video();
                bool fresh = false; timeout.Restart();
                while (timeout.Elapsed.TotalSeconds < 5)
                {
                    if (Ndi.NDIlib_recv_capture_v2(receiver, ref latest, IntPtr.Zero, IntPtr.Zero, 500) != 1) continue;
                    try { fresh = latest.Timecode > videoTimecode + 50_000_000 && latest.Data != IntPtr.Zero; }
                    finally { Ndi.NDIlib_recv_free_video_v2(receiver, ref latest); }
                    if (fresh) break;
                }
                Check(fresh, $"{port}: NDI receiver still receives advancing video after concurrent run", output);
            }
        }
        finally
        {
            if (receiver != IntPtr.Zero) Ndi.NDIlib_recv_destroy(receiver);
            if (finder != IntPtr.Zero) Ndi.NDIlib_find_destroy(finder);
            if (directUrl != IntPtr.Zero) Marshal.FreeCoTaskMem(directUrl);
            if (source is not null) { if (!source.HasExited) source.Kill(true); await source.WaitForExitAsync(); if (sourceErrors is not null) { var errors = await sourceErrors; if (errors.Length > 0) output.WriteLine(errors); } source.Dispose(); }
            await runner.StopAsync();
            if (testSdp is not null) File.Delete(testSdp);
        }
    }
}
