using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;

namespace Vidvix.Services.VideoPreview;

internal sealed class MpvNativeLibrary : IDisposable
{
    private IntPtr _supportLibraryHandle;
    private IntPtr _libraryHandle;
    private bool _isDisposed;

    public MpvNativeLibrary(string runtimeDirectory, string libraryFileName, IReadOnlyList<string> supportDllFileNames)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(libraryFileName);

        var libraryPath = Path.Combine(runtimeDirectory, libraryFileName);
        if (!File.Exists(libraryPath))
        {
            throw new FileNotFoundException($"未找到 MPV 运行时：{libraryPath}", libraryPath);
        }

        if (supportDllFileNames is not null)
        {
            foreach (var supportDllFileName in supportDllFileNames)
            {
                if (string.IsNullOrWhiteSpace(supportDllFileName))
                {
                    continue;
                }

                var supportPath = Path.Combine(runtimeDirectory, supportDllFileName);
                if (!File.Exists(supportPath))
                {
                    continue;
                }

                _supportLibraryHandle = NativeLibrary.Load(supportPath);
                break;
            }
        }

        _libraryHandle = NativeLibrary.Load(libraryPath);
        ClientApiVersion = LoadFunction<MpvClientApiVersionDelegate>("mpv_client_api_version");
        ErrorString = LoadFunction<MpvErrorStringDelegate>("mpv_error_string");
        Create = LoadFunction<MpvCreateDelegate>("mpv_create");
        Initialize = LoadFunction<MpvInitializeDelegate>("mpv_initialize");
        Destroy = LoadFunction<MpvDestroyDelegate>("mpv_destroy");
        SetOptionString = LoadFunction<MpvSetOptionStringDelegate>("mpv_set_option_string");
        Command = LoadFunction<MpvCommandDelegate>("mpv_command");
        SetProperty = LoadFunction<MpvSetPropertyDelegate>("mpv_set_property");
        GetProperty = LoadFunction<MpvGetPropertyDelegate>("mpv_get_property");
        ObserveProperty = LoadFunction<MpvObservePropertyDelegate>("mpv_observe_property");
        RequestEvent = LoadFunction<MpvRequestEventDelegate>("mpv_request_event");
        RequestLogMessages = LoadFunction<MpvRequestLogMessagesDelegate>("mpv_request_log_messages");
        WaitEvent = LoadFunction<MpvWaitEventDelegate>("mpv_wait_event");
        Wakeup = LoadFunction<MpvWakeupDelegate>("mpv_wakeup");
    }

    public MpvClientApiVersionDelegate ClientApiVersion { get; }

    public MpvErrorStringDelegate ErrorString { get; }

    public MpvCreateDelegate Create { get; }

    public MpvInitializeDelegate Initialize { get; }

    public MpvDestroyDelegate Destroy { get; }

    public MpvSetOptionStringDelegate SetOptionString { get; }

    public MpvCommandDelegate Command { get; }

    public MpvSetPropertyDelegate SetProperty { get; }

    public MpvGetPropertyDelegate GetProperty { get; }

    public MpvObservePropertyDelegate ObserveProperty { get; }

    public MpvRequestEventDelegate RequestEvent { get; }

    public MpvRequestLogMessagesDelegate RequestLogMessages { get; }

    public MpvWaitEventDelegate WaitEvent { get; }

    public MpvWakeupDelegate Wakeup { get; }

    public static string PtrToUtf8String(IntPtr pointer) =>
        pointer == IntPtr.Zero ? string.Empty : Marshal.PtrToStringUTF8(pointer) ?? string.Empty;

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        if (_libraryHandle != IntPtr.Zero)
        {
            NativeLibrary.Free(_libraryHandle);
            _libraryHandle = IntPtr.Zero;
        }

        if (_supportLibraryHandle != IntPtr.Zero)
        {
            NativeLibrary.Free(_supportLibraryHandle);
            _supportLibraryHandle = IntPtr.Zero;
        }
    }

    private T LoadFunction<T>(string exportName)
        where T : Delegate
    {
        var export = NativeLibrary.GetExport(_libraryHandle, exportName);
        return Marshal.GetDelegateForFunctionPointer<T>(export);
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate ulong MpvClientApiVersionDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate IntPtr MpvErrorStringDelegate(int error);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate IntPtr MpvCreateDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate int MpvInitializeDelegate(IntPtr context);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void MpvDestroyDelegate(IntPtr context);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate int MpvSetOptionStringDelegate(IntPtr context, byte[] name, byte[] value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate int MpvCommandDelegate(IntPtr context, IntPtr args);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate int MpvSetPropertyDelegate(IntPtr context, byte[] name, MpvFormat format, IntPtr data);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate int MpvGetPropertyDelegate(IntPtr context, byte[] name, MpvFormat format, IntPtr data);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate int MpvObservePropertyDelegate(IntPtr context, ulong replyUserData, byte[] name, MpvFormat format);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate int MpvRequestEventDelegate(IntPtr context, MpvEventId eventId, int enable);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate int MpvRequestLogMessagesDelegate(IntPtr context, byte[] minLevel);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate IntPtr MpvWaitEventDelegate(IntPtr context, double timeout);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void MpvWakeupDelegate(IntPtr context);
}

internal enum MpvFormat
{
    None = 0,
    String = 1,
    OsdString = 2,
    Flag = 3,
    Int64 = 4,
    Double = 5
}

internal enum MpvEventId
{
    None = 0,
    Shutdown = 1,
    LogMessage = 2,
    GetPropertyReply = 3,
    SetPropertyReply = 4,
    CommandReply = 5,
    StartFile = 6,
    EndFile = 7,
    FileLoaded = 8,
    ClientMessage = 16,
    VideoReconfig = 17,
    AudioReconfig = 18,
    Seek = 20,
    PlaybackRestart = 21,
    PropertyChange = 22,
    QueueOverflow = 24,
    Hook = 25
}

internal enum MpvEndFileReason
{
    Eof = 0,
    Stop = 2,
    Quit = 3,
    Error = 4,
    Redirect = 5
}

internal static class MpvUtf8
{
    public static byte[] GetBytes(string value) =>
        System.Text.Encoding.UTF8.GetBytes((value ?? string.Empty) + "\0");

    public static string FormatDouble(double value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture);
}

[StructLayout(LayoutKind.Sequential)]
internal struct MpvEvent
{
    public MpvEventId EventId;
    public int Error;
    public ulong ReplyUserData;
    public IntPtr Data;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MpvEventProperty
{
    public IntPtr Name;
    public MpvFormat Format;
    public IntPtr Data;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MpvEventLogMessage
{
    public IntPtr Prefix;
    public IntPtr Level;
    public IntPtr Text;
    public int LogLevel;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MpvEventEndFile
{
    public MpvEndFileReason Reason;
    public int Error;
    public long PlaylistEntryId;
    public long PlaylistInsertId;
    public int PlaylistInsertNumEntries;
}

internal sealed class MpvCommandArguments : IDisposable
{
    private readonly IntPtr[] _stringPointers;
    private readonly IntPtr _argumentsPointer;

    public MpvCommandArguments(params string[] arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        _stringPointers = new IntPtr[arguments.Length];
        for (var index = 0; index < arguments.Length; index++)
        {
            _stringPointers[index] = Marshal.StringToCoTaskMemUTF8(arguments[index]);
        }

        _argumentsPointer = Marshal.AllocHGlobal((arguments.Length + 1) * IntPtr.Size);
        for (var index = 0; index < arguments.Length; index++)
        {
            Marshal.WriteIntPtr(_argumentsPointer, index * IntPtr.Size, _stringPointers[index]);
        }

        Marshal.WriteIntPtr(_argumentsPointer, arguments.Length * IntPtr.Size, IntPtr.Zero);
    }

    public IntPtr Pointer => _argumentsPointer;

    public void Dispose()
    {
        foreach (var pointer in _stringPointers)
        {
            if (pointer != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(pointer);
            }
        }

        if (_argumentsPointer != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_argumentsPointer);
        }
    }
}
