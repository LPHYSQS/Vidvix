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
    private const ulong TimePositionPropertyObserverId = 2;
    private static readonly TimeSpan EndFrameSafetyBackoff = TimeSpan.FromMilliseconds(1);
    private static readonly TimeSpan SeekTimeout = TimeSpan.FromMilliseconds(240);
    private static readonly TimeSpan CacheWarmupTarget = TimeSpan.FromSeconds(1);
    private static readonly object HostWindowClassSyncRoot = new();
    private static readonly NativeMethods.WndProc HostWindowProcedure = HostWindowWindowProc;
    private static ushort _hostWindowClassAtom;

    private readonly ApplicationConfiguration _configuration;
    private readonly IWindowContext _windowContext;
    private readonly ILogger _logger;
    private readonly object _syncRoot = new();
    private readonly SemaphoreSlim _commandSemaphore = new(1, 1);
    private MpvNativeLibrary? _nativeLibrary;
    private IntPtr _mpvHandle;
    private IntPtr _hostWindowHandle;
    private IntPtr _hostParentWindowHandle;
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
    private string _observedTimePositionPropertyName = "time-pos/full";
    private TimeSpan _duration;
    private TimeSpan _currentPosition;
    private int _loadSessionId;
    private TaskCompletionSource<bool>? _loadCompletionSource;
    private TaskCompletionSource<TimeSpan>? _seekCompletionSource;
    private TimeSpan _pendingSeekTarget;
    private VideoPreviewHostPlacement _hostPlacement;

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

    public event EventHandler<VideoPreviewPositionChangedEventArgs>? PositionChanged;

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
        var shouldRefresh = false;

        lock (_syncRoot)
        {
            ThrowIfDisposed();
            shouldRefresh = _isInitialized &&
                            placement.IsVisible &&
                            placement.Width > 0 &&
                            placement.Height > 0 &&
                            _hostPlacement != placement;
            _hostPlacement = placement;
            EnsureHostWindow();
            ApplyHostPlacement(placement);
        }

        if (shouldRefresh)
        {
            _ = RefreshAsync();
        }
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            lock (_syncRoot)
            {
                if (_isDisposed || _hostWindowHandle == IntPtr.Zero)
                {
                    return;
                }

                ApplyHostPlacement(_hostPlacement);
                RefreshHostWindow();
            }

            await RunCommandAsync(() =>
            {
                lock (_syncRoot)
                {
                    if (_isDisposed || !_isInitialized || _mpvHandle == IntPtr.Zero)
                    {
                        return;
                    }

                    ApplyHostPlacement(_hostPlacement);
                    TryRequestRedraw();
                    RefreshHostWindow();
                }
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.Log(LogLevel.Warning, "刷新 MPV 预览画面时发生异常。", exception);
        }
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await RunCommandAsync(() =>
        {
            lock (_syncRoot)
            {
                ThrowIfDisposed();
                EnsureHostWindow();
                EnsureInitialized();
                ApplyHostPlacement(_hostPlacement);
                TryRequestRedraw();
                RefreshHostWindow();
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task LoadAsync(
        string inputPath,
        double volume,
        bool enableExternalSubtitleAutoLoad = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        cancellationToken.ThrowIfCancellationRequested();

        var fullPath = Path.GetFullPath(inputPath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"未找到用于预览的视频文件：{fullPath}", fullPath);
        }

        TaskCompletionSource<bool>? previousLoadCompletionSource;
        TaskCompletionSource<bool>? currentLoadCompletionSource;
        var normalizedVolume = Math.Clamp(volume, 0d, 1d);

        lock (_syncRoot)
        {
            ThrowIfDisposed();
            previousLoadCompletionSource = _loadCompletionSource;
            _loadCompletionSource = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            currentLoadCompletionSource = _loadCompletionSource;
            _loadSessionId++;
            _requestedSourcePath = fullPath;
            _activeSourcePath = null;
            _duration = TimeSpan.Zero;
            _currentPosition = TimeSpan.Zero;
            _hasLoadedMedia = false;
            _isPlaying = false;
            _isLoading = true;
            _isUnloading = false;
            _ignoreNextStopEndEvent = true;
            _volume = normalizedVolume;
        }

        previousLoadCompletionSource?.TrySetResult(false);

        await RunCommandAsync(() =>
        {
            lock (_syncRoot)
            {
                ThrowIfDisposed();
                EnsureHostWindow();
                EnsureInitialized();
            }

            TrySetPropertyDouble("volume", normalizedVolume * 100d);
            ExecuteCommand("set", "pause", "yes");
            ExecuteCommand("set", "sub-auto", enableExternalSubtitleAutoLoad ? "exact" : "no");
            ExecuteCommand("loadfile", fullPath, "replace");
            TryRequestRedraw();
            RefreshHostWindow();
        }, cancellationToken).ConfigureAwait(false);

        RaisePlaybackStateChanged(isPlaying: false);

        if (currentLoadCompletionSource is not null)
        {
            var isReady = await currentLoadCompletionSource.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            if (!isReady)
            {
                return;
            }
        }
    }

    public async Task UnloadAsync(CancellationToken cancellationToken = default)
    {
        TaskCompletionSource<bool>? loadCompletionSource;
        TaskCompletionSource<TimeSpan>? seekCompletionSource;
        var shouldStop = false;

        lock (_syncRoot)
        {
            loadCompletionSource = _loadCompletionSource;
            _loadCompletionSource = null;
            seekCompletionSource = _seekCompletionSource;
            _seekCompletionSource = null;
            _loadSessionId++;
            _requestedSourcePath = null;
            _activeSourcePath = null;
            _isLoading = false;
            _isUnloading = true;
            _ignoreNextStopEndEvent = true;

            if (_isInitialized && _mpvHandle != IntPtr.Zero)
            {
                shouldStop = true;
            }

            ResetMediaState();
        }

        loadCompletionSource?.TrySetResult(false);
        seekCompletionSource?.TrySetResult(TimeSpan.Zero);

        if (!shouldStop)
        {
            RaisePlaybackStateChanged(isPlaying: false);
            return;
        }

        try
        {
            await RunCommandAsync(() => ExecuteCommand("stop"), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.Log(LogLevel.Warning, "MPV 停止当前预览时发生异常。", exception);
        }

        RaisePlaybackStateChanged(isPlaying: false);
    }

    public async Task PlayAsync(CancellationToken cancellationToken = default)
    {
        if (!HasLoadedMedia)
        {
            return;
        }

        await RunCommandAsync(() =>
        {
            ExecuteCommand("set", "pause", "no");
            TryRequestRedraw();
            RefreshHostWindow();
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<TimeSpan> PauseAsync(CancellationToken cancellationToken = default)
    {
        if (!_isInitialized)
        {
            return CurrentPosition;
        }

        var position = await RunCommandAsync(() =>
        {
            ExecuteCommand("set", "pause", "yes");
            return UpdateCurrentPositionFromPlayer();
        }, cancellationToken).ConfigureAwait(false);

        RaisePositionChanged(position);
        return position;
    }

    public async Task<TimeSpan> SeekAsync(TimeSpan position, CancellationToken cancellationToken = default)
    {
        if (!HasLoadedMedia)
        {
            return CurrentPosition;
        }

        var normalized = NormalizePreviewPosition(position);
        TaskCompletionSource<TimeSpan>? seekCompletionSource = null;

        await RunCommandAsync(() =>
        {
            seekCompletionSource = BeginPendingSeek(normalized);
            ExecuteCommand(
                "seek",
                normalized.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture),
                "absolute+keyframes");
        }, cancellationToken).ConfigureAwait(false);

        if (seekCompletionSource is null)
        {
            return CurrentPosition;
        }

        var completedTask = await Task.WhenAny(
            seekCompletionSource.Task,
            Task.Delay(SeekTimeout, CancellationToken.None)).ConfigureAwait(false);

        if (completedTask == seekCompletionSource.Task)
        {
            return await seekCompletionSource.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        _logger.Log(
            LogLevel.Warning,
            $"MPV fast seek timed out, keeping keyframe seek pending at {normalized.TotalSeconds:0.###}s.");

        CompletePendingSeek(normalized);
        return normalized;
    }

#if false

        var refreshedPosition = await RunCommandAsync(() =>
        {
            SetPropertyDouble("time-pos", normalized.TotalSeconds);
            return UpdateCurrentPositionFromPlayer();
        }, CancellationToken.None).ConfigureAwait(false);

        _logger.Log(
            LogLevel.Warning,
            $"MPV Seek 超时，已回退为一次 time-pos 强制刷新，目标时间：{normalized.TotalSeconds:0.###} 秒。");

        CompletePendingSeek(refreshedPosition);
        RaisePositionChanged(refreshedPosition);
        return await seekCompletionSource.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

#endif

    public async Task<TimeSpan> SetPlaybackPositionAsync(TimeSpan position, CancellationToken cancellationToken = default)
    {
        if (!HasLoadedMedia)
        {
            return CurrentPosition;
        }

        var normalized = NormalizePreviewPosition(position);
        await RunCommandAsync(() =>
        {
            SetPropertyDouble("time-pos", normalized.TotalSeconds);
            UpdateCurrentPosition(normalized);
            TryRequestRedraw();
        }, cancellationToken).ConfigureAwait(false);

        RaisePositionChanged(normalized);
        return normalized;
    }

    public async Task SetVolumeAsync(double volume, CancellationToken cancellationToken = default)
    {
        var normalized = Math.Clamp(volume, 0d, 1d);
        var canApply = false;

        lock (_syncRoot)
        {
            _volume = normalized;
            canApply = _isInitialized && _mpvHandle != IntPtr.Zero;
        }

        if (!canApply)
        {
            return;
        }

        await RunCommandAsync(
            () => TrySetPropertyDouble("volume", normalized * 100d),
            cancellationToken).ConfigureAwait(false);
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
                _hostParentWindowHandle = IntPtr.Zero;
            }

            eventLoopCancellationSource?.Dispose();
            _nativeLibrary?.Dispose();
            _nativeLibrary = null;
            _mpvHandle = IntPtr.Zero;
            _eventLoopCancellationSource = null;
            _eventLoopTask = null;
            _commandSemaphore.Dispose();
        }
    }

    private async Task RunCommandAsync(Action action, CancellationToken cancellationToken)
    {
        await RunCommandAsync(
            () =>
            {
                action();
                return 0;
            },
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<T> RunCommandAsync<T>(Func<T> action, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await _commandSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            return await Task.Run(
                () =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    ThrowIfDisposed();
                    return action();
                },
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _commandSemaphore.Release();
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
    }

    private void EnsureHostWindow()
    {
        var parentHandle = _windowContext.Handle;
        if (_hostWindowHandle != IntPtr.Zero)
        {
            return;
        }

        EnsureHostWindowClassRegistered();
        _hostWindowHandle = NativeMethods.CreateWindowExW(
            0,
            NativeMethods.HostWindowClassName,
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
        _hostParentWindowHandle = parentHandle;
    }

    private void EnsureHostWindowParent(IntPtr parentHandle)
    {
        if (_hostWindowHandle == IntPtr.Zero ||
            parentHandle == IntPtr.Zero ||
            _hostParentWindowHandle == parentHandle)
        {
            return;
        }

        Marshal.GetLastWin32Error();
        var previousParentHandle = NativeMethods.SetParent(_hostWindowHandle, parentHandle);
        if (previousParentHandle == IntPtr.Zero && Marshal.GetLastWin32Error() != 0)
        {
            throw new InvalidOperationException("无法将 MPV 预览宿主切换到 WinUI 内容层。");
        }

        _hostParentWindowHandle = parentHandle;
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
        UpdateHostWindowZOrder(placement);
        RefreshHostWindow();
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

                TrySetPropertyDouble("volume", _volume * 100d);
                RequestEvent(MpvEventId.FileLoaded);
                RequestEvent(MpvEventId.EndFile);
                RequestEvent(MpvEventId.PropertyChange);
                RequestEvent(MpvEventId.PlaybackRestart);
                ObserveProperty(PausePropertyObserverId, "pause", MpvFormat.Flag);
                ObserveTimePositionProperty();
                _nativeLibrary.RequestLogMessages(_mpvHandle, MpvUtf8.GetBytes("warn"));
                _eventLoopCancellationSource = new CancellationTokenSource();
                _eventLoopTask = Task.Factory.StartNew(
                    () => RunEventLoop(_eventLoopCancellationSource.Token),
                    _eventLoopCancellationSource.Token,
                    TaskCreationOptions.LongRunning,
                    TaskScheduler.Default);
                _isInitialized = true;
                ApplyHostPlacement(_hostPlacement);
                RefreshHostWindow();
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
        SetOptionString("force-window", "yes");
        SetOptionString("background-color", "#000000");
        SetOptionString("sub-codepage", "gb18030");
        SetOptionString("metadata-codepage", "gb18030");
        SetOptionString("cache", "yes");
        SetOptionString("cache-secs", "45");
        SetOptionString("demuxer-max-bytes", "536870912");
        SetOptionString("demuxer-max-back-bytes", "268435456");
        SetOptionString("demuxer-readahead-secs", "15");
    }

    private void ObserveTimePositionProperty()
    {
        try
        {
            ObserveProperty(TimePositionPropertyObserverId, "time-pos/full", MpvFormat.Double);
            _observedTimePositionPropertyName = "time-pos/full";
        }
        catch
        {
            ObserveProperty(TimePositionPropertyObserverId, "time-pos", MpvFormat.Double);
            _observedTimePositionPropertyName = "time-pos";
        }
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
        if (TryGetPropertyDouble(_observedTimePositionPropertyName, out var preciseSeconds) ||
            TryGetPropertyDouble("time-pos/full", out preciseSeconds) ||
            TryGetPropertyDouble("time-pos", out preciseSeconds))
        {
            var normalized = TimeSpan.FromSeconds(Math.Max(0d, preciseSeconds));
            UpdateCurrentPosition(normalized);
            return normalized;
        }

        lock (_syncRoot)
        {
            return _currentPosition;
        }
    }

    private bool UpdateCurrentPosition(TimeSpan position)
    {
        lock (_syncRoot)
        {
            var normalized = position < TimeSpan.Zero ? TimeSpan.Zero : position;
            var hasChanged = !AreClose(_currentPosition, normalized);
            _currentPosition = normalized;
            return hasChanged;
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

    private static TimeSpan[] BuildInitialWarmupTargets(TimeSpan duration)
    {
        var nearEnd = duration > EndFrameSafetyBackoff
            ? duration - EndFrameSafetyBackoff
            : TimeSpan.Zero;
        var warmHead = duration > CacheWarmupTarget ? CacheWarmupTarget : nearEnd;
        var warmTail = duration > CacheWarmupTarget + EndFrameSafetyBackoff
            ? duration - CacheWarmupTarget
            : nearEnd;

        return
        [
            TimeSpan.Zero,
            nearEnd,
            warmHead,
            warmTail,
            TimeSpan.Zero
        ];
    }

    private TaskCompletionSource<TimeSpan> BeginPendingSeek(TimeSpan target)
    {
        TaskCompletionSource<TimeSpan>? previousCompletionSource;
        TaskCompletionSource<TimeSpan> currentCompletionSource;

        lock (_syncRoot)
        {
            previousCompletionSource = _seekCompletionSource;
            currentCompletionSource = new TaskCompletionSource<TimeSpan>(TaskCreationOptions.RunContinuationsAsynchronously);
            _seekCompletionSource = currentCompletionSource;
            _pendingSeekTarget = target;
        }

        previousCompletionSource?.TrySetResult(CurrentPosition);
        return currentCompletionSource;
    }

    private void CompletePendingSeek(TimeSpan position)
    {
        TaskCompletionSource<TimeSpan>? seekCompletionSource = null;

        lock (_syncRoot)
        {
            if (_seekCompletionSource is null)
            {
                return;
            }

            seekCompletionSource = _seekCompletionSource;
            _seekCompletionSource = null;
            _pendingSeekTarget = TimeSpan.Zero;
        }

        seekCompletionSource.TrySetResult(position);
    }

    private void FinishPendingLoad(int sessionId, bool isReady)
    {
        TaskCompletionSource<bool>? loadCompletionSource = null;

        lock (_syncRoot)
        {
            if (sessionId != _loadSessionId)
            {
                return;
            }

            loadCompletionSource = _loadCompletionSource;
            _loadCompletionSource = null;
        }

        loadCompletionSource?.TrySetResult(isReady);
    }

    private void FailPendingLoad(int errorCode)
    {
        TaskCompletionSource<bool>? loadCompletionSource = null;

        lock (_syncRoot)
        {
            loadCompletionSource = _loadCompletionSource;
            _loadCompletionSource = null;
        }

        loadCompletionSource?.TrySetException(new InvalidOperationException(BuildFailureMessage(errorCode)));
    }

    private bool IsCurrentLoadSession(int sessionId, string sourcePath)
    {
        lock (_syncRoot)
        {
            return sessionId == _loadSessionId &&
                   string.Equals(_activeSourcePath, sourcePath, StringComparison.OrdinalIgnoreCase);
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
            case MpvEventId.PlaybackRestart:
                HandlePlaybackRestart();
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
        var sessionId = 0;

        lock (_syncRoot)
        {
            _isLoading = false;
            _isUnloading = false;
            _ignoreNextStopEndEvent = false;
            _hasLoadedMedia = true;
            _isPlaying = false;
            _activeSourcePath = _requestedSourcePath ?? string.Empty;
            sourcePath = _activeSourcePath;
            sessionId = _loadSessionId;
            if (TryGetPropertyDouble("duration/full", out var durationSeconds) ||
                TryGetPropertyDouble("duration", out durationSeconds))
            {
                _duration = durationSeconds > 0d ? TimeSpan.FromSeconds(durationSeconds) : TimeSpan.Zero;
            }

            _currentPosition = TimeSpan.Zero;
            duration = _duration;
        }

        _ = PrepareLoadedMediaAsync(sessionId, sourcePath, duration);
    }

    private async Task PrepareLoadedMediaAsync(int sessionId, string sourcePath, TimeSpan duration)
    {
        try
        {
            foreach (var target in BuildInitialWarmupTargets(duration))
            {
                await SetPlaybackPositionAsync(target).ConfigureAwait(false);
            }

            await RefreshAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.Log(LogLevel.Warning, "MPV 预览首帧预热失败，正在继续显示当前预览。", exception);
        }

        if (!IsCurrentLoadSession(sessionId, sourcePath))
        {
            return;
        }

        RaisePlaybackStateChanged(isPlaying: false);
        RaisePositionChanged(CurrentPosition);
        MediaOpened?.Invoke(this, new VideoPreviewMediaOpenedEventArgs(sourcePath, duration));
        FinishPendingLoad(sessionId, isReady: true);
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

        CompletePendingSeek(CurrentPosition);
        RaisePlaybackStateChanged(isPlaying: false);
        if (endFileEvent.Reason == MpvEndFileReason.Error)
        {
            FailPendingLoad(endFileEvent.Error);
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

    private void HandlePlaybackRestart()
    {
        var position = UpdateCurrentPositionFromPlayer();
        CompletePendingSeek(position);
        RaisePositionChanged(position);
    }

    private void HandlePropertyChange(IntPtr eventData)
    {
        if (eventData == IntPtr.Zero)
        {
            return;
        }

        var property = Marshal.PtrToStructure<MpvEventProperty>(eventData);
        var name = MpvNativeLibrary.PtrToUtf8String(property.Name);

        if (string.Equals(name, "pause", StringComparison.Ordinal) &&
            property.Format == MpvFormat.Flag &&
            property.Data != IntPtr.Zero)
        {
            var isPaused = Marshal.ReadInt32(property.Data) != 0;
            bool isPlaying;
            lock (_syncRoot)
            {
                _isPlaying = _hasLoadedMedia && !isPaused;
                isPlaying = _isPlaying;
            }

            RaisePlaybackStateChanged(isPlaying);
            return;
        }

        if (!string.Equals(name, _observedTimePositionPropertyName, StringComparison.Ordinal) ||
            property.Format != MpvFormat.Double ||
            property.Data == IntPtr.Zero)
        {
            return;
        }

        var seconds = Marshal.PtrToStructure<double>(property.Data);
        if (double.IsNaN(seconds) || double.IsInfinity(seconds))
        {
            return;
        }

        var position = TimeSpan.FromSeconds(Math.Max(0d, seconds));
        var hasChanged = UpdateCurrentPosition(position);
        CompletePendingSeek(position);

        if (hasChanged)
        {
            RaisePositionChanged(position);
        }
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

    private void RaisePositionChanged(TimeSpan position) =>
        PositionChanged?.Invoke(this, new VideoPreviewPositionChangedEventArgs(position));

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
        _pendingSeekTarget = TimeSpan.Zero;
    }

    private void RefreshHostWindow()
    {
        if (_hostWindowHandle == IntPtr.Zero)
        {
            return;
        }

        NativeMethods.RedrawWindow(
            _hostWindowHandle,
            IntPtr.Zero,
            IntPtr.Zero,
            NativeMethods.RDW_INVALIDATE | NativeMethods.RDW_UPDATENOW | NativeMethods.RDW_ALLCHILDREN);
        NativeMethods.UpdateWindow(_hostWindowHandle);
    }

    private void UpdateHostWindowZOrder(VideoPreviewHostPlacement placement)
    {
        if (_hostWindowHandle == IntPtr.Zero)
        {
            return;
        }

        NativeMethods.SetWindowPos(
            _hostWindowHandle,
            placement.IsVisible && placement.Width > 0 && placement.Height > 0
                ? NativeMethods.HWND_TOP
                : NativeMethods.HWND_BOTTOM,
            0,
            0,
            0,
            0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOOWNERZORDER);
    }

    private void TryRequestRedraw()
    {
        if (_nativeLibrary is null || _mpvHandle == IntPtr.Zero)
        {
            return;
        }

        try
        {
            using var commandArguments = new MpvCommandArguments("redraw");
            _nativeLibrary.Command(_mpvHandle, commandArguments.Pointer);
        }
        catch
        {
        }

        _nativeLibrary.Wakeup(_mpvHandle);
    }

    private static void EnsureHostWindowClassRegistered()
    {
        if (_hostWindowClassAtom != 0)
        {
            return;
        }

        lock (HostWindowClassSyncRoot)
        {
            if (_hostWindowClassAtom != 0)
            {
                return;
            }

            var windowClass = new NativeMethods.WindowClass
            {
                Size = (uint)Marshal.SizeOf<NativeMethods.WindowClass>(),
                Style = NativeMethods.CS_HREDRAW | NativeMethods.CS_VREDRAW,
                WindowProcedure = HostWindowProcedure,
                InstanceHandle = NativeMethods.GetModuleHandleW(IntPtr.Zero),
                CursorHandle = NativeMethods.LoadCursorW(IntPtr.Zero, (IntPtr)NativeMethods.IDC_ARROW),
                ClassName = NativeMethods.HostWindowClassName
            };

            _hostWindowClassAtom = NativeMethods.RegisterClassExW(ref windowClass);
            if (_hostWindowClassAtom == 0)
            {
                throw new InvalidOperationException("无法注册 MPV 预览宿主窗口类。");
            }
        }
    }

    private static IntPtr HostWindowWindowProc(IntPtr windowHandle, uint message, IntPtr wParam, IntPtr lParam)
    {
        if (message == NativeMethods.WM_SETCURSOR)
        {
            var cursor = NativeMethods.LoadCursorW(IntPtr.Zero, (IntPtr)NativeMethods.IDC_ARROW);
            if (cursor != IntPtr.Zero)
            {
                NativeMethods.SetCursor(cursor);
                return (IntPtr)1;
            }
        }

        return NativeMethods.DefWindowProcW(windowHandle, message, wParam, lParam);
    }

    private static bool AreClose(TimeSpan left, TimeSpan right) =>
        Math.Abs((left - right).TotalMilliseconds) < 1d;

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
        public const string HostWindowClassName = "VidvixMpvPreviewHostWindow";
        public const int CS_HREDRAW = 0x0002;
        public const int CS_VREDRAW = 0x0001;
        public const int IDC_ARROW = 32512;
        public const int RDW_INVALIDATE = 0x0001;
        public const int RDW_UPDATENOW = 0x0100;
        public const int RDW_ALLCHILDREN = 0x0080;
        public const int SW_HIDE = 0;
        public const int SW_SHOW = 5;
        public const uint SWP_NOSIZE = 0x0001;
        public const uint SWP_NOMOVE = 0x0002;
        public const uint SWP_NOACTIVATE = 0x0010;
        public const uint SWP_NOOWNERZORDER = 0x0200;
        public const uint WM_SETCURSOR = 0x0020;
        public const uint WS_CHILD = 0x40000000;
        public const uint WS_CLIPSIBLINGS = 0x04000000;
        public const uint WS_CLIPCHILDREN = 0x02000000;
        public static readonly IntPtr HWND_TOP = IntPtr.Zero;
        public static readonly IntPtr HWND_BOTTOM = new(1);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct WindowClass
        {
            public uint Size;
            public uint Style;
            public WndProc WindowProcedure;
            public int ClassExtraBytes;
            public int WindowExtraBytes;
            public IntPtr InstanceHandle;
            public IntPtr IconHandle;
            public IntPtr CursorHandle;
            public IntPtr BackgroundBrushHandle;
            public string? MenuName;
            public string ClassName;
            public IntPtr SmallIconHandle;
        }

        public delegate IntPtr WndProc(IntPtr windowHandle, uint message, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern ushort RegisterClassExW(ref WindowClass windowClass);

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

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetParent(IntPtr childHandle, IntPtr newParentHandle);

        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr handle, int command);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetWindowPos(
            IntPtr handle,
            IntPtr insertAfterHandle,
            int x,
            int y,
            int width,
            int height,
            uint flags);

        [DllImport("user32.dll")]
        public static extern IntPtr DefWindowProcW(IntPtr windowHandle, uint message, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr LoadCursorW(IntPtr instanceHandle, IntPtr cursorName);

        [DllImport("user32.dll")]
        public static extern IntPtr SetCursor(IntPtr cursorHandle);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UpdateWindow(IntPtr handle);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool RedrawWindow(IntPtr handle, IntPtr updateRect, IntPtr updateRegion, int flags);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool DestroyWindow(IntPtr handle);

        [DllImport("kernel32.dll")]
        public static extern IntPtr GetModuleHandleW(IntPtr moduleName);
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
