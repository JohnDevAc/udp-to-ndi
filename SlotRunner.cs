using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using System.Net;
using System.Net.Sockets;

namespace UdpNdi;

public sealed class SlotRunner
{
    CancellationTokenSource? stop;
    Task? task;
    long frames, lastFrame;
    long audioSamples, lastAudio;
    int retries;
    int receivers;
    string state = "Stopped";
    string detail = "";
    public VideoFormat? DetectedFormat { get; private set; }
    public string SourceDescription { get; private set; } = "";
    public AudioFormat? DetectedAudio { get; private set; }
    public long AudioSamples => Interlocked.Read(ref audioSamples);
    public int Retries => Volatile.Read(ref retries);
    string audioState = "Automatic";
    public string AudioStatus => DetectedAudio is { } audio ? (Running && Environment.TickCount64 - Interlocked.Read(ref lastAudio) > 4000 ? "Audio lost" : audio.Summary) : audioState;
    public bool Running => task is { IsCompleted: false };
    public long Frames => Interlocked.Read(ref frames);
    public string State => Running && state == "Live" && Environment.TickCount64 - Interlocked.Read(ref lastFrame) > 4000 ? "Signal lost · retrying" : state;
    public string Detail => detail;
    public int Receivers => Volatile.Read(ref receivers);
    internal IntPtr Sender;
    public event Action<string>? Log;

    public void Start(SlotSettings settings, string ffmpeg)
    {
        if (Running) return;
        settings.Validate();
        if (!File.Exists(ffmpeg)) throw new FileNotFoundException("FFmpeg is not configured. Download the Windows essentials ZIP from https://www.gyan.dev/ffmpeg/builds/ and extract it. Then choose More → Choose FFmpeg and select bin\\ffmpeg.exe. Use More → Download FFmpeg to open the download page.");
        Ndi.Initialize();
        stop?.Dispose(); stop = new();
        frames = 0; detail = ""; state = "Starting"; DetectedFormat = null; SourceDescription = "";
        audioSamples = 0; retries = 0; DetectedAudio = null; audioState = "Detecting…";
        task = Task.Run(() => Run(settings with { }, ffmpeg, stop.Token));
    }
    public async Task StopAsync()
    {
        stop?.Cancel();
        if (task is not null) await task;
        state = "Stopped";
    }
    async Task Run(SlotSettings s, string ffmpeg, CancellationToken token)
    {
        try
        {
            bool canHaveAudio = s.Input is "RTP MPEG-TS" or "UDP MPEG-TS" or "SDP file";
            bool includeAudio = canHaveAudio;
            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (Sender == IntPtr.Zero)
                    {
                        var create = new Ndi.SenderSettings { Name = s.Name, ClockVideo = false, ClockAudio = false };
                        Sender = Ndi.NDIlib_send_create(ref create);
                        if (Sender == IntPtr.Zero) throw new InvalidOperationException("Cannot create NDI sender.");
                    }
                    await Decode(s, ffmpeg, includeAudio, token);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
                catch (NoAudioException) { includeAudio = false; audioState = "No audio"; continue; }
                catch (Exception ex) { detail = ex.Message; Log?.Invoke($"{s.Name}: {ex.Message}"); }
                includeAudio = canHaveAudio;
                Interlocked.Increment(ref retries);
                state = "Retrying · unlimited";
                await Task.Delay(3000, token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex) { state = "Error"; detail = ex.Message; Log?.Invoke($"{s.Name}: {ex.Message}"); }
        finally
        {
            if (Sender != IntPtr.Zero) { Ndi.NDIlib_send_destroy(Sender); Sender = IntPtr.Zero; }
            receivers = 0;
            if (token.IsCancellationRequested) state = "Stopped";
        }
    }
    sealed class NoAudioException : Exception { }
    async Task Decode(SlotSettings s, string ffmpeg, bool includeAudio, CancellationToken token)
    {
        var temp = Path.Combine(Path.GetTempPath(), $"udp-ndi-{Guid.NewGuid():N}.sdp");
        Process? process = null;
        Task? errorTask = null;
        Task? audioTask = null;
        TcpListener? audioListener = null;
        using var decoderStop = CancellationTokenSource.CreateLinkedTokenSource(token);
        var frameInfo = Channel.CreateBounded<(VideoFormat format, double pts)>(new BoundedChannelOptions(16) { SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait });
        var audioInfo = Channel.CreateBounded<AudioPacket>(new BoundedChannelOptions(256) { SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait });
        var clock = new MediaClock();
        DetectedAudio = null; audioState = includeAudio ? "Detecting…" : "No audio";
        state = "Detecting source";
        try
        {
            var start = new ProcessStartInfo(ffmpeg) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            void Add(params string[] values) { foreach (var v in values) start.ArgumentList.Add(v); }
            Add("-hide_banner", "-loglevel", "info", "-nostats", "-nostdin", "-threads", "2", "-filter_threads", "1", "-analyzeduration", "2000000", "-probesize", "2000000");
            if (s.Input == "UDP MPEG-TS")
            {
                var nic = s.Interface.Length > 0 ? $"&localaddr={s.Interface}" : "";
                Add("-i", $"udp://{s.Address}:{s.Port}?fifo_size=65536&overrun_nonfatal=1&buffer_size=4194304{nic}");
            }
            else
            {
                var input = s.SdpPath;
                if (s.Input != "SDP file") { File.WriteAllText(temp, s.Sdp()); input = temp; }
                Add("-protocol_whitelist", "file,udp,rtp", "-reorder_queue_size", "512");
                if (s.Interface.Length > 0) Add("-localaddr", s.Interface);
                Add("-i", input);
            }
            Add("-map", "0:v:0", "-an", "-sn", "-dn", "-vf", "format=uyvy422,fieldorder=tff,showinfo=checksum=0", "-noautoscale", "-fps_mode", "passthrough", "-c:v", "rawvideo", "-pix_fmt", "uyvy422", "-threads", "1", "-f", "rawvideo", "pipe:1");
            if (includeAudio)
            {
                audioListener = new TcpListener(IPAddress.Loopback, 0); audioListener.Start();
                int port = ((IPEndPoint)audioListener.LocalEndpoint).Port;
                Add("-map", "0:a:0?", "-vn", "-sn", "-dn", "-af", "aformat=sample_fmts=flt,asettb=1/sr,ashowinfo", "-c:a", "pcm_f32le", "-flush_packets", "1", "-f", "f32le", $"tcp://127.0.0.1:{port}");
            }
            process = Process.Start(start) ?? throw new IOException("FFmpeg did not start.");
            using var cancellation = token.Register(() => Kill(process));
            if (audioListener is not null)
            {
                audioTask = ReceiveAudio(audioListener, audioInfo.Reader, clock, decoderStop.Token);
                _ = audioTask.ContinueWith(failed => { if (failed.IsFaulted) Kill(process); }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
            errorTask = Task.Run(async () =>
            {
                var parser = new FrameInfoParser();
                bool inputSection = true;
                try
                {
                    while (await process.StandardError.ReadLineAsync(decoderStop.Token) is { } line)
                    {
                        if (line.StartsWith("Stream mapping:")) inputSection = false;
                        if (inputSection && line.Contains("Stream #") && line.Contains("Video:")) SourceDescription = line.Trim();
                        if (includeAudio && line.Contains("does not contain any stream")) throw new NoAudioException();
                        if (AudioInfoParser.Parse(line) is { } audio) await audioInfo.Writer.WriteAsync(audio, decoderStop.Token);
                        if (parser.Parse(line) is { } format) await frameInfo.Writer.WriteAsync((format, parser.Timestamp), decoderStop.Token);
                        if (line.Contains("showinfo_")) continue;
                        detail = line;
                        Log?.Invoke($"{s.Name}: {line}");
                    }
                }
                catch (OperationCanceledException) when (decoderStop.IsCancellationRequested) { }
                catch (Exception ex) { frameInfo.Writer.TryComplete(ex); audioInfo.Writer.TryComplete(ex); return; }
                finally { frameInfo.Writer.TryComplete(); audioInfo.Writer.TryComplete(); }
            });
            byte[] buffer = [];
            GCHandle pin = default;
            try
            {
                while (!token.IsCancellationRequested)
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                    timeout.CancelAfter(TimeSpan.FromSeconds(20));
                    VideoFormat format;
                    double pts;
                    try
                    {
                        (format, pts) = await frameInfo.Reader.ReadAsync(timeout.Token);
                        if (buffer.Length != format.BufferSize)
                        {
                            if (pin.IsAllocated) pin.Free();
                            buffer = GC.AllocateUninitializedArray<byte>(format.BufferSize);
                            pin = GCHandle.Alloc(buffer, GCHandleType.Pinned);
                        }
                        await process.StandardOutput.BaseStream.ReadExactlyAsync(buffer, timeout.Token);
                    }
                    catch (OperationCanceledException) when (!token.IsCancellationRequested) { throw new TimeoutException("No complete video frame for 20 seconds. Check RTP/SDP, port, interface and firewall."); }
                    catch (ChannelClosedException ex) when (ex.InnerException is NoAudioException) { throw new NoAudioException(); }
                    catch (ChannelClosedException ex) { throw new IOException(ex.InnerException?.Message ?? $"Decoder ended before the next frame. {detail}", ex); }
                    if (DetectedFormat != format) { DetectedFormat = format; Log?.Invoke($"{s.Name}: detected {format.Description}; mirroring to NDI."); }
                    var timecode = await clock.WaitAsync(pts, token);
                    var video = new Ndi.Video { Width = format.Width, Height = format.Height, FourCC = 0x59565955, RateN = format.RateN, RateD = format.RateD, Aspect = format.Aspect, Format = format.Interlaced ? 0 : 1, Timecode = timecode, Data = pin.AddrOfPinnedObject(), Stride = format.Width * 2 };
                    Ndi.NDIlib_send_send_video_v2(Sender, ref video);
                    Interlocked.Increment(ref frames);
                    Interlocked.Exchange(ref lastFrame, Environment.TickCount64);
                    state = "Live";
                    receivers = Ndi.NDIlib_send_get_no_connections(Sender, 0);
                }
            }
            finally { if (pin.IsAllocated) pin.Free(); }
        }
        finally
        {
            decoderStop.Cancel();
            audioListener?.Stop();
            if (process is not null)
            {
                Kill(process);
                await process.WaitForExitAsync();
                if (errorTask is not null) await errorTask;
                process.Dispose();
            }
            if (audioTask is not null)
            {
                try { await audioTask; }
                catch (OperationCanceledException) when (decoderStop.IsCancellationRequested) { }
                catch (ChannelClosedException ex) when (ex.InnerException is NoAudioException) { }
                catch (Exception ex) { Log?.Invoke($"{s.Name}: audio decoder ended: {ex.Message}"); }
            }
            if (File.Exists(temp)) File.Delete(temp);
        }
    }
    async Task ReceiveAudio(TcpListener listener, ChannelReader<AudioPacket> packets, MediaClock clock, CancellationToken token)
    {
        using var client = await listener.AcceptTcpClientAsync(token);
        client.NoDelay = true;
        using var stream = client.GetStream();
        byte[] buffer = []; GCHandle pin = default;
        try
        {
            while (!token.IsCancellationRequested)
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeout.CancelAfter(TimeSpan.FromSeconds(20));
                var packet = await packets.ReadAsync(timeout.Token);
                if (DetectedAudio is { } previous && previous != packet.Format) throw new IOException("Audio format changed; reopening the decoder to match it.");
                if (buffer.Length != packet.BufferSize)
                {
                    if (pin.IsAllocated) pin.Free();
                    buffer = GC.AllocateUninitializedArray<byte>(packet.BufferSize); pin = GCHandle.Alloc(buffer, GCHandleType.Pinned);
                }
                await stream.ReadExactlyAsync(buffer, timeout.Token);
                ScaleAudio(buffer);
                var timecode = await clock.WaitAsync(packet.Timestamp, token);
                var audio = new Ndi.AudioInterleaved { SampleRate = packet.Format.SampleRate, Channels = packet.Format.Channels, Samples = packet.Samples, Timecode = timecode, Data = pin.AddrOfPinnedObject() };
                Ndi.NDIlib_util_send_send_audio_interleaved_32f(Sender, ref audio);
                DetectedAudio = packet.Format;
                Interlocked.Add(ref audioSamples, packet.Samples);
                Interlocked.Exchange(ref lastAudio, Environment.TickCount64);
            }
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { throw new TimeoutException("No audio frame for 20 seconds; reconnecting the source."); }
        finally { if (pin.IsAllocated) pin.Free(); }
    }
    static void ScaleAudio(byte[] buffer)
    {
        // Normalized PCM 0 dBFS maps to 10.0 in NDI's SMPTE reference scale.
        var samples = MemoryMarshal.Cast<byte, float>(buffer);
        for (int i = 0; i < samples.Length; i++) samples[i] *= 10.0f;
    }
    static void Kill(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
    }
}
