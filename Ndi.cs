using System.Runtime.InteropServices;

namespace UdpNdi;

internal static class Ndi
{
    const string Dll = "Processing.NDI.Lib.x64.dll";
    static bool initialized;
    static readonly object Gate = new();
    static Ndi()
    {
        NativeLibrary.SetDllImportResolver(typeof(Ndi).Assembly, (name, assembly, path) =>
        {
            if (name != Dll) return IntPtr.Zero;
            var folders = new[] { AppContext.BaseDirectory, Environment.GetEnvironmentVariable("NDI_RUNTIME_DIR_V6"), Environment.GetEnvironmentVariable("NDI_RUNTIME_DIR_V5"), @"C:\Program Files\NDI\NDI 6 Runtime\v6", @"C:\Program Files\NDI\NDI 6 SDK\Bin\x64" };
            foreach (var folder in folders)
                if (folder is not null && NativeLibrary.TryLoad(Path.Combine(folder, Dll), out var handle)) return handle;
            throw new DllNotFoundException("NDI runtime not found. Install the NDI 6 Runtime or NDI Tools and restart the application.");
        });
    }
    public static void Initialize()
    {
        lock (Gate) { if (!initialized) { if (!NDIlib_initialize()) throw new InvalidOperationException("NDI initialization failed."); initialized = true; } }
    }
    [StructLayout(LayoutKind.Sequential)]
    public struct SenderSettings
    {
        [MarshalAs(UnmanagedType.LPUTF8Str)] public string Name;
        public IntPtr Groups;
        [MarshalAs(UnmanagedType.I1)] public bool ClockVideo;
        [MarshalAs(UnmanagedType.I1)] public bool ClockAudio;
    }
    [StructLayout(LayoutKind.Sequential)]
    public struct Video
    {
        public int Width, Height, FourCC, RateN, RateD;
        public float Aspect;
        public int Format;
        public long Timecode;
        public IntPtr Data;
        public int Stride;
        public IntPtr Metadata;
        public long Timestamp;
    }
    [StructLayout(LayoutKind.Sequential)]
    public struct AudioInterleaved
    {
        public int SampleRate, Channels, Samples;
        public long Timecode;
        public IntPtr Data;
    }
    [StructLayout(LayoutKind.Sequential)]
    public struct Audio
    {
        public int SampleRate, Channels, Samples;
        public long Timecode;
        public IntPtr Data;
        public int ChannelStride;
        public IntPtr Metadata;
        public long Timestamp;
    }
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void NDIlib_util_send_send_audio_interleaved_32f(IntPtr sender, ref AudioInterleaved audio);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "NDIlib_recv_capture_v2")] public static extern int CaptureAudio(IntPtr receiver, IntPtr video, ref Audio audio, IntPtr metadata, uint timeout);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void NDIlib_recv_free_audio_v2(IntPtr receiver, ref Audio audio);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)]
    static extern bool NDIlib_initialize();
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr NDIlib_send_create(ref SenderSettings settings);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void NDIlib_send_destroy(IntPtr sender);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void NDIlib_send_send_video_v2(IntPtr sender, ref Video video);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern int NDIlib_send_get_no_connections(IntPtr sender, uint timeout);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr NDIlib_send_get_source_name(IntPtr sender);
    [StructLayout(LayoutKind.Sequential)] public struct Source { public IntPtr Name, Url; }
    [StructLayout(LayoutKind.Sequential)] public struct ReceiverSettings
    {
        public Source Source;
        public int Color, Bandwidth;
        [MarshalAs(UnmanagedType.I1)] public bool Fields;
        public IntPtr Name;
    }
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr NDIlib_recv_create_v3(ref ReceiverSettings settings);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern int NDIlib_recv_capture_v2(IntPtr receiver, ref Video video, IntPtr audio, IntPtr metadata, uint timeout);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void NDIlib_recv_free_video_v2(IntPtr receiver, ref Video video);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void NDIlib_recv_destroy(IntPtr receiver);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr NDIlib_find_create_v2(IntPtr settings);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr NDIlib_find_get_current_sources(IntPtr finder, out uint count);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void NDIlib_find_destroy(IntPtr finder);
}
