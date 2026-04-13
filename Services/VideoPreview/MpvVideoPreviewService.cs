using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Vidvix.Core.Interfaces;
using Vidvix.Core.Models;

namespace Vidvix.Services.VideoPreview;

public sealed class MpvVideoPreviewService : IVideoPreviewService
{
    private const ulong PausePropertyObserverId = 1;
    private static readonly TimeSpan EndFrameSafetyBackoff = TimeSpan.FromMilliseconds(1);

    private readonly ApplicationConfiguration _configuration;
    private readonly IWindowContext _windowContext;
    private readonly ILogger _logger;
    private readonly object _syncRoot = new();
    private MpvNativeLibrary? _nativeLibrary;
    private IntPtr _mpvHandle;
    private IntPtr _hostWindowHandle;
    private CancellationTokenSource? _eventLoopCancellationSource;
    private Task? _eventLoopTask;
    private bool _isDisposed;
    private bool _isInitialized;
    private bool _isLoading;
    private bool _isUnloading;
    private bool _ignoreNextStopEndEvent;
    private bool _hasLoadedMedia;
    private bool _isPlaying;
    private double _volume = 0.8d;
    private string? _requestedSourcePath;
    private string? _activeSourcePath;
    private TimeSpan _duration;
    private TimeSpan _currentPosition;

    public MpvVideoPreviewService(
        ApplicationConfiguration configuration,
        IWindowContext windowContext,
        ILogger logger)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _windowContext = windowContext ?? throw new ArgumentNullException(nameof(windowContext));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public event EventHandler<VideoPreviewMediaOpenedEventArgs>? MediaOpened;

    public event EventHandler<VideoPreviewFailedEventArgs>? MediaFailed;

    public event EventHandler<VideoPreviewPlaybackStateChangedEventArgs>? PlaybackStateChanged;

    public event EventHandler? MediaEnded;

    public bool HasLoadedMedia
    {
        get
        {
            lock (_syncRoot)
            {
                return _hasLoadedMedia;
            }
        }
    }

    public bool IsPlaying
    {
        get
        {
            lock (_syncRoot)
            {
                return _isPlaying;
            }
        }
    }

    public TimeSpan Duration
    {
        get
        {
            lock (_syncRoot)
            {
                return _duration;
            }
        }
    }

    public TimeSpan CurrentPosition
    {
        get
        {
            lock (_syncRoot)
            {
                return _currentPosition;
            }
        }
    }

    public void UpdateHostPlacement(VideoPreviewHostPlacement placement)
    {
        lock (_syncRoot)
        {
            ThrowIfDisposed();
            EnsureHostWindow();
            ApplyHostPlacement(placement);
            EnsureInitialized();
        }
    }

    public Task LoadAsync(string inputPath, double volume, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        cancellationToken.ThrowIfCancellationRequested();

        var fullPath = Path.GetFullPath(inputPath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"未找到用于预览的视频文件：{fullPath}", fullPath);
        }

        lock (_syncRoot)
        {
            ThrowIfDisposed();
            EnsureHostWindow();
            EnsureInitialized();
            _requestedSourcePath = fullPath;
            _activeSourcePath = null;
            _duration = TimeSpan.Zero;
            _currentPosition = TimeSpan.Zero;
            _hasLoadedMedia = false;
            _isPlaying = false;
            _isLoading = true;
            _isUnloading = false;
            _ignoreNextStopEndEvent = true;
        }

        SetVolume(volume);
        ExecuteCommand("set", "pause", "yes");
        ExecuteCommand("loadfile", fullPath, "replace");
        RaisePlaybackStateChanged(isPlaying: false);
        return Task.CompletedTask;
    }

    public void Unload()
    {
        lock (_syncRoot)
        {
            if (!_isInitialized || _mpvHandle == IntPtr.Zero)
            {
                ResetMediaState();
                return;
            }

            _requestedSourcePath = null;
            _activeSourcePath = null;
            _isLoading = false;
            _isUnloading = true;
            _ignoreNextStopEndEvent = true;
            ResetMediaState();
        }

        try
        {
            ExecuteCommand("stop");
        }
        catch (Exception exception)
        {
            _logger.Log(LogLevel.Warning, "MPV 停止当前预览时发生异常。", exception);
        }

        RaisePlaybackStateChanged(isPlaying: false);
    }

    public void Play()
    {
        if (!HasLoadedMedia)
        {
            return;
        }

        ExecuteCommand("set", "pause", "no");
    }

    public void Pause()
    {
        if (!_isInitialized)
        {
            return;
        }

        ExecuteCommand("set", "pause", "yes");
        UpdateCurrentPositionFromPlayer();
    }

    public void Seek(TimeSpan position)
    {
        if (!HasLoadedMedia)
        {
            return;
        }

        var normalized = NormalizePreviewPosition(position);
        ExecuteCommand("seek", normalized.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture), "absolute+exact");
        lock (_syncRoot)
        {
            _currentPosition = normalized;
        }
    }

    public void SetPlaybackPosition(TimeSpan position)
    {
        if (!HasLoadedMedia)
        {
            return;
        }

        var normalized = NormalizePreviewPosition(position);
        SetPropertyDouble("time-pos", normalized.TotalSeconds);
        lock (_syncRoot)
        {
            _currentPosition = normalized;
        }
    }

    public TimeSpan GetCurrentPosition()
    {
        return UpdateCurrentPositionFromPlayer();
    }

    public void SetVolume(double volume)
    {
        var normalized = Math.Clamp(volume, 0d, 1d);
        lock (_syncRoot)
        {
            _volume = normalized;
            if (!_isInitialized || _mpvHandle == IntPtr.Zero)
            {
                return;
            }
        }

        TrySetPropertyDouble("volume", normalized * 100d);
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;

        var eventLoopCancellationSource = _eventLoopCancellationSource;
        var eventLoopTask = _eventLoopTask;
        var nativeLibrary = _nativeLibrary;
        var mpvHandle = _mpvHandle;

        try
        {
            if (nativeLibrary is not null && mpvHandle != IntPtr.Zero)
            {
                try
                {
                    ExecuteCommand("quit");
                }
                catch (Exception exception)
                {
                    _logger.Log(LogLevel.Warning, "请求 MPV 退出时发生异常，正在继续释放资源。", exception);
                }

                eventLoopCancellationSource?.Cancel();
                nativeLibrary.Wakeup(mpvHandle);
            }

            if (eventLoopTask is not null)
            {
                try
                {
                    eventLoopTask.Wait(TimeSpan.FromSeconds(5));
                }
                catch (AggregateException exception)
                {
                    _logger.Log(LogLevel.Warning, "等待 MPV 事件线程退出时发生异常。", exception.Flatten());
                }
            }
        }
        finally
        {
            if (nativeLibrary is not null && mpvHandle != IntPtr.Zero)
            {
                try
                {
                    nativeLibrary.Destroy(mpvHandle);
                }
                catch (Exception exception)
                {
                    _logger.Log(LogLevel.Warning, "释放 MPV 句柄时发生异常。", exception);
                }
            }

            if (_hostWindowHandle != IntPtr.Zero)
            {
                NativeMethods.DestroyWindow(_hostWindowHandle);
                _hostWindowHandle = IntPtr.Zero;
            }

            eventLoopCancellationSource?.Dispose();
            _nativeLibrary?.Dispose();
            _nativeLibrary = null;
            _mpvHandle = IntPtr.Zero;
            _eventLoopCancellationSource = null;
            _eventLoopTask = null;
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
    }

    private void EnsureHostWindow()
    {
        if (_hostWindowHandle != IntPtr.Zero)
        {
            return;
        }

        _hostWindowHandle = NativeMethods.CreateWindowExW(
            0,
            "Static",
            string.Empty,
            NativeMethods.WS_CHILD | NativeMethods.WS_CLIPSIBLINGS | NativeMethods.WS_CLIPCHILDREN,
            0,
            0,
            1,
            1,
            _windowContext.Handle,
            IntPtr.Zero,
            IntPtr.Zero,
            IntPtr.Zero);
        if (_hostWindowHandle == IntPtr.Zero)
        {
            throw new InvalidOperationException("无法为 MPV 预览创建宿主窗口。");
        }
    }

    private void ApplyHostPlacement(VideoPreviewHostPlacement placement)
    {
        if (_hostWindowHandle == IntPtr.Zero)
        {
            return;
        }

        var width = Math.Max(1, placement.Width);
        var height = Math.Max(1, placement.Height);
        NativeMethods.MoveWindow(_hostWindowHandle, placement.X, placement.Y, width, height, true);
        NativeMethods.ShowWindow(_hostWindowHandle, placement.IsVisible && placement.Width > 0 && placement.Height > 0
            ? NativeMethods.SW_SHOW
            : NativeMethods.SW_HIDE);
    }

    private void EnsureInitialized()
    {
        if (_isInitialized)
        {
            return;
        }

        var runtimeDirectory = ResolveRuntimeDirectory();
        _nativeLibrary = new MpvNativeLibrary(
            runtimeDirectory,
            _configuration.MpvLibraryFileName,
            _configuration.MpvSupportDllFileNames);

        var profiles = new[]
        {
            new MpvInitializationProfile(
                "gpu-next-d3d11",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["vo"] = "gpu-next",
                    ["gpu-api"] = "d3d11",
                    ["gpu-context"] = "d3d11",
                    ["hwdec"] = "auto",
                    ["tone-mapping"] = "auto",
                    ["target-colorspace-hint"] = "auto",
                    ["hdr-compute-peak"] = "auto"
                }),
            new MpvInitializationProfile(
                "gpu-d3d11",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["vo"] = "gpu",
                    ["gpu-api"] = "d3d11",
                    ["gpu-context"] = "d3d11",
                    ["hwdec"] = "auto",
                    ["tone-mapping"] = "auto"
                }),
            new MpvInitializationProfile(
                "gpu-auto",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["vo"] = "gpu",
                    ["hwdec"] = "auto",
                    ["tone-mapping"] = "auto"
                })
        };

        List<string> initializationFailures = new();
        foreach (var profile in profiles)
        {
            CleanupFailedInitializationHandle();
            _mpvHandle = _nativeLibrary.Create();
            if (_mpvHandle == IntPtr.Zero)
            {
                initializationFailures.Add($"配置文件 {profile.Name}：无法创建 MPV 上下文。");
                continue;
            }

            try
            {
                ApplyCommonInitializationOptions();
                foreach (var option in profile.Options)
                {
                    SetOptionString(option.Key, option.Value);
                }

                SetOptionString("wid", unchecked((ulong)_hostWindowHandle.ToInt64()).ToString(CultureInfo.InvariantCulture));
                var initializeResult = _nativeLibrary.Initialize(_mpvHandle);
                if (initializeResult < 0)
                {
                    initializationFailures.Add($"配置文件 {profile.Name}：{GetErrorMessage(initializeResult)}");
                    continue;
                }

                SetVolume(_volume);
                RequestEvent(MpvEventId.FileLoaded);
                RequestEvent(MpvEventId.EndFile);
                RequestEvent(MpvEventId.PropertyChange);
                ObserveProperty(PausePropertyObserverId, "pause", MpvFormat.Flag);
                _nativeLibrary.RequestLogMessages(_mpvHandle, MpvUtf8.GetBytes("warn"));
                _eventLoopCancellationSource = new CancellationTokenSource();
                _eventLoopTask = Task.Factory.StartNew(
                    () => RunEventLoop(_eventLoopCancellationSource.Token),
                    _eventLoopCancellationSource.Token,
                    TaskCreationOptions.LongRunning,
                    TaskScheduler.Default);
                _isInitialized = true;
                _logger.Log(LogLevel.Info, $"MPV 预览内核已启用：{profile.Name}，客户端 API 版本 0x{_nativeLibrary.ClientApiVersion():X}。");
                return;
            }
            catch (Exception exception)
            {
                initializationFailures.Add($"配置文件 {profile.Name}：{exception.Message}");
                _logger.Log(LogLevel.Warning, $"MPV 初始化配置 {profile.Name} 失败，正在尝试兼容回退。", exception);
            }
        }

        CleanupFailedInitializationHandle();
        throw new InvalidOperationException("无法初始化 MPV 预览内核：" + string.Join("；", initializationFailures));
    }

    private void CleanupFailedInitializationHandle()
    {
        if (_nativeLibrary is null || _mpvHandle == IntPtr.Zero)
        {
            return;
        }

        try
        {
            _nativeLibrary.Destroy(_mpvHandle);
        }
        catch
        {
        }

        _mpvHandle = IntPtr.Zero;
        _isInitialized = false;
    }

    private string ResolveRuntimeDirectory()
    {
        var runtimeDirectory = Path.Combine(
            AppContext.BaseDirectory,
            _configuration.RuntimeDirectoryName,
            _configuration.MpvBundledRuntimeDirectoryName);
        if (!Directory.Exists(runtimeDirectory))
        {
            throw new DirectoryNotFoundException($"未找到 MPV 运行时目录：{runtimeDirectory}");
        }

        return runtimeDirectory;
    }

    private void ApplyCommonInitializationOptions()
    {
        SetOptionString("config", "no");
        SetOptionString("load-scripts", "no");
        SetOptionString("input-default-bindings", "no");
        SetOptionString("input-vo-keyboard", "no");
        SetOptionString("osc", "no");
        SetOptionString("keep-open", "yes");
        SetOptionString("keepaspect", "yes");
        SetOptionString("terminal", "no");
        SetOptionString("pause", "yes");
        SetOptionString("osd-level", "0");
        SetOptionString("profile", "fast");
        SetOptionString("background-color", "#000000");
    }

    private void ObserveProperty(ulong replyUserData, string name, MpvFormat format)
    {
        var result = _nativeLibrary!.ObserveProperty(_mpvHandle, replyUserData, MpvUtf8.GetBytes(name), format);
        EnsureSuccess(result, $"观察 MPV 属性 {name} 失败。");
    }

    private void RequestEvent(MpvEventId eventId)
    {
        var result = _nativeLibrary!.RequestEvent(_mpvHandle, eventId, enable: 1);
        EnsureSuccess(result, $"启用 MPV 事件 {eventId} 失败。");
    }

    private void SetOptionString(string name, string value)
    {
        var result = _nativeLibrary!.SetOptionString(_mpvHandle, MpvUtf8.GetBytes(name), MpvUtf8.GetBytes(value));
        EnsureSuccess(result, $"设置 MPV 选项 {name} 失败。");
    }

    private void ExecuteCommand(params string[] arguments)
    {
        if (_nativeLibrary is null || _mpvHandle == IntPtr.Zero)
        {
            return;
        }

        using var commandArguments = new MpvCommandArguments(arguments);
        var result = _nativeLibrary.Command(_mpvHandle, commandArguments.Pointer);
        EnsureSuccess(result, $"执行 MPV 命令 {arguments[0]} 失败。");
    }

    private void SetPropertyDouble(string name, double value)
    {
        using var valueBuffer = new UnmanagedValue<double>(value);
        var result = _nativeLibrary!.SetProperty(_mpvHandle, MpvUtf8.GetBytes(name), MpvFormat.Double, valueBuffer.Pointer);
        EnsureSuccess(result, $"设置 MPV 属性 {name} 失败。");
    }

    private void TrySetPropertyDouble(string name, double value)
    {
        try
        {
            SetPropertyDouble(name, value);
        }
        catch (Exception exception)
        {
            _logger.Log(LogLevel.Warning, $"设置 MPV 属性 {name} 时发生异常。", exception);
        }
    }

    private bool TryGetPropertyDouble(string name, out double value)
    {
        value = 0d;
        if (_nativeLibrary is null || _mpvHandle == IntPtr.Zero)
        {
            return false;
        }

        using var buffer = new UnmanagedValue<double>(0d);
        var result = _nativeLibrary.GetProperty(_mpvHandle, MpvUtf8.GetBytes(name), MpvFormat.Double, buffer.Pointer);
        if (result < 0)
        {
            return false;
        }

        value = buffer.Read();
        return true;
    }

    private TimeSpan UpdateCurrentPositionFromPlayer()
    {
        if (TryGetPropertyDouble("time-pos/full", out var preciseSeconds) ||
            TryGetPropertyDouble("time-pos", out preciseSeconds))
        {
            lock (_syncRoot)
            {
                _currentPosition = TimeSpan.FromSeconds(Math.Max(0d, preciseSeconds));
                return _currentPosition;
            }
        }

        lock (_syncRoot)
        {
            return _currentPosition;
        }
    }

    private TimeSpan NormalizePreviewPosition(TimeSpan requestedPosition)
    {
        lock (_syncRoot)
        {
            if (_duration > TimeSpan.Zero && requestedPosition >= _duration)
            {
                return _duration > EndFrameSafetyBackoff
                    ? _duration - EndFrameSafetyBackoff
                    : TimeSpan.Zero;
            }

            return requestedPosition < TimeSpan.Zero ? TimeSpan.Zero : requestedPosition;
        }
    }

    private void RunEventLoop(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var nativeLibrary = _nativeLibrary;
                var handle = _mpvHandle;
                if (nativeLibrary is null || handle == IntPtr.Zero)
                {
                    return;
                }

                var eventPointer = nativeLibrary.WaitEvent(handle, -1d);
                if (eventPointer == IntPtr.Zero)
                {
                    continue;
                }

                var mpvEvent = Marshal.PtrToStructure<MpvEvent>(eventPointer);
                HandleEvent(mpvEvent);
                if (mpvEvent.EventId == MpvEventId.Shutdown)
                {
                    return;
                }
            }
        }
        catch (Exception exception)
        {
            _logger.Log(LogLevel.Error, "MPV 事件循环发生异常。", exception);
        }
    }

    private void HandleEvent(MpvEvent mpvEvent)
    {
        switch (mpvEvent.EventId)
        {
            case MpvEventId.FileLoaded:
                HandleFileLoaded();
                break;
            case MpvEventId.EndFile:
                HandleEndFile(mpvEvent.Data);
                break;
            case MpvEventId.PropertyChange:
                HandlePropertyChange(mpvEvent.Data);
                break;
            case MpvEventId.LogMessage:
                HandleLogMessage(mpvEvent.Data);
                break;
            case MpvEventId.QueueOverflow:
                _logger.Log(LogLevel.Warning, "MPV 事件队列已溢出，正在继续读取最新事件。");
                break;
        }
    }

    private void HandleFileLoaded()
    {
        var sourcePath = string.Empty;
        var duration = TimeSpan.Zero;
        lock (_syncRoot)
        {
            _isLoading = false;
            _isUnloading = false;
            _ignoreNextStopEndEvent = false;
            _hasLoadedMedia = true;
            _isPlaying = false;
            _activeSourcePath = _requestedSourcePath ?? string.Empty;
            sourcePath = _activeSourcePath;
            if (TryGetPropertyDouble("duration/full", out var durationSeconds) ||
                TryGetPropertyDouble("duration", out durationSeconds))
            {
                _duration = durationSeconds > 0d ? TimeSpan.FromSeconds(durationSeconds) : TimeSpan.Zero;
            }

            duration = _duration;
        }

        SetVolume(_volume);
        RaisePlaybackStateChanged(isPlaying: false);
        MediaOpened?.Invoke(this, new VideoPreviewMediaOpenedEventArgs(sourcePath, duration));
    }

    private void HandleEndFile(IntPtr eventData)
    {
        if (eventData == IntPtr.Zero)
        {
            return;
        }

        var endFileEvent = Marshal.PtrToStructure<MpvEventEndFile>(eventData);
        string sourcePath;
        lock (_syncRoot)
        {
            sourcePath = _activeSourcePath ?? _requestedSourcePath ?? string.Empty;
            if (endFileEvent.Reason == MpvEndFileReason.Stop &&
                (_ignoreNextStopEndEvent || _isUnloading || _isLoading))
            {
                _ignoreNextStopEndEvent = false;
                return;
            }

            if (endFileEvent.Reason == MpvEndFileReason.Error)
            {
                _isLoading = false;
                _isUnloading = false;
                _hasLoadedMedia = false;
                _isPlaying = false;
                _activeSourcePath = null;
            }
            else
            {
                _isPlaying = false;
            }
        }

        RaisePlaybackStateChanged(isPlaying: false);
        if (endFileEvent.Reason == MpvEndFileReason.Error)
        {
            MediaFailed?.Invoke(
                this,
                new VideoPreviewFailedEventArgs(
                    sourcePath,
                    BuildFailureMessage(endFileEvent.Error),
                    endFileEvent.Error));
            return;
        }

        if (endFileEvent.Reason == MpvEndFileReason.Eof)
        {
            MediaEnded?.Invoke(this, EventArgs.Empty);
        }
    }

    private void HandlePropertyChange(IntPtr eventData)
    {
        if (eventData == IntPtr.Zero)
        {
            return;
        }

        var property = Marshal.PtrToStructure<MpvEventProperty>(eventData);
        var name = MpvNativeLibrary.PtrToUtf8String(property.Name);
        if (!string.Equals(name, "pause", StringComparison.Ordinal) ||
            property.Format != MpvFormat.Flag ||
            property.Data == IntPtr.Zero)
        {
            return;
        }

        var isPaused = Marshal.ReadInt32(property.Data) != 0;
        bool isPlaying;
        lock (_syncRoot)
        {
            _isPlaying = _hasLoadedMedia && !isPaused;
            isPlaying = _isPlaying;
        }

        RaisePlaybackStateChanged(isPlaying);
    }

    private void HandleLogMessage(IntPtr eventData)
    {
        if (eventData == IntPtr.Zero)
        {
            return;
        }

        var logMessage = Marshal.PtrToStructure<MpvEventLogMessage>(eventData);
        var level = MpvNativeLibrary.PtrToUtf8String(logMessage.Level);
        var message = MpvNativeLibrary.PtrToUtf8String(logMessage.Text).Trim();
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        var logLevel = string.Equals(level, "error", StringComparison.OrdinalIgnoreCase)
            ? LogLevel.Error
            : LogLevel.Warning;
        _logger.Log(logLevel, $"MPV: {message}");
    }

    private void RaisePlaybackStateChanged(bool isPlaying) =>
        PlaybackStateChanged?.Invoke(this, new VideoPreviewPlaybackStateChangedEventArgs(isPlaying));

    private string BuildFailureMessage(int errorCode)
    {
        var errorText = GetErrorMessage(errorCode);
        return string.IsNullOrWhiteSpace(errorText)
            ? "当前视频无法预览，但仍可尝试直接导出。"
            : $"当前视频无法预览：{errorText}";
    }

    private string GetErrorMessage(int errorCode)
    {
        if (_nativeLibrary is null)
        {
            return $"错误代码 {errorCode}";
        }

        var pointer = _nativeLibrary.ErrorString(errorCode);
        var message = MpvNativeLibrary.PtrToUtf8String(pointer);
        return string.IsNullOrWhiteSpace(message)
            ? $"错误代码 {errorCode}"
            : message;
    }

    private void EnsureSuccess(int result, string message)
    {
        if (result >= 0)
        {
            return;
        }

        throw new InvalidOperationException($"{message} {GetErrorMessage(result)}");
    }

    private void ResetMediaState()
    {
        _duration = TimeSpan.Zero;
        _currentPosition = TimeSpan.Zero;
        _hasLoadedMedia = false;
        _isPlaying = false;
    }

    private sealed class MpvInitializationProfile
    {
        public MpvInitializationProfile(string name, IReadOnlyDictionary<string, string> options)
        {
            Name = name;
            Options = options;
        }

        public string Name { get; }

        public IReadOnlyDictionary<string, string> Options { get; }
    }

    private static class NativeMethods
    {
        public const int SW_HIDE = 0;
        public const int SW_SHOW = 5;
        public const uint WS_CHILD = 0x40000000;
        public const uint WS_CLIPSIBLINGS = 0x04000000;
        public const uint WS_CLIPCHILDREN = 0x02000000;

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr CreateWindowExW(
            uint exStyle,
            string className,
            string windowName,
            uint style,
            int x,
            int y,
            int width,
            int height,
            IntPtr parentHandle,
            IntPtr menuHandle,
            IntPtr instanceHandle,
            IntPtr parameter);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool MoveWindow(IntPtr handle, int x, int y, int width, int height, bool repaint);

        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr handle, int command);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool DestroyWindow(IntPtr handle);
    }

    private sealed class UnmanagedValue<T> : IDisposable
        where T : struct
    {
        public UnmanagedValue(T value)
        {
            Pointer = Marshal.AllocHGlobal(Marshal.SizeOf<T>());
            Marshal.StructureToPtr(value, Pointer, false);
        }

        public IntPtr Pointer { get; }

        public T Read() => Marshal.PtrToStructure<T>(Pointer);

        public void Dispose()
        {
            if (Pointer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(Pointer);
            }
        }
    }
}
