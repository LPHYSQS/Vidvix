using System;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Vidvix.Utils;
using Vidvix.Views.Converters;
using Windows.Media.Core;
using Windows.Media.Playback;

namespace Vidvix.Views.Controls;

public sealed partial class SplitAudioStemPreviewCard : UserControl, INotifyPropertyChanged, IDisposable, ISplitAudioPlaybackParticipant
{
    private static readonly TimeSpan PositionRefreshInterval = TimeSpan.FromMilliseconds(80);

    private readonly DispatcherQueue _dispatcherQueue;
    private readonly DispatcherQueueTimer _positionTimer;
    private Task<bool>? _loadTask;
    private TaskCompletionSource<bool>? _loadCompletionSource;
    private MediaPlayer? _mediaPlayer;
    private TimeSpan _duration;
    private TimeSpan _currentPosition;
    private bool _isDisposed;
    private bool _isPlaying;
    private bool _isSeeking;
    private bool _isTimelinePointerPressed;
    private bool _resumePlaybackAfterSeek;
    private bool _hasLoadFailed;
    private string _loadedFilePath = string.Empty;

    public SplitAudioStemPreviewCard()
    {
        InitializeComponent();
        if (Resources.TryGetValue("TimelineMillisecondsToTimeConverter", out var converterResource) &&
            converterResource is TimelineMillisecondsToTimeConverter timelineConverter)
        {
            timelineConverter.Formatter = FormatTimelineThumbToolTip;
        }

        _dispatcherQueue = DispatcherQueue.GetForCurrentThread()
            ?? throw new InvalidOperationException("Current thread does not have a dispatcher queue.");

        _positionTimer = _dispatcherQueue.CreateTimer();
        _positionTimer.Interval = PositionRefreshInterval;
        _positionTimer.IsRepeating = true;
        _positionTimer.Tick += OnPositionTimerTick;

        TimelineSlider.AddHandler(PointerPressedEvent, new PointerEventHandler(OnTimelineSliderPointerPressed), true);
        TimelineSlider.AddHandler(PointerReleasedEvent, new PointerEventHandler(OnTimelineSliderPointerReleased), true);
        TimelineSlider.AddHandler(PointerCaptureLostEvent, new PointerEventHandler(OnTimelineSliderPointerCaptureLost), true);
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;

        ResetForSource();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string StemTitle
    {
        get => (string)GetValue(StemTitleProperty);
        set => SetValue(StemTitleProperty, value);
    }

    public static readonly DependencyProperty StemTitleProperty = DependencyProperty.Register(
        nameof(StemTitle),
        typeof(string),
        typeof(SplitAudioStemPreviewCard),
        new PropertyMetadata(string.Empty));

    public string FilePath
    {
        get => (string)GetValue(FilePathProperty);
        set => SetValue(FilePathProperty, value);
    }

    public static readonly DependencyProperty FilePathProperty = DependencyProperty.Register(
        nameof(FilePath),
        typeof(string),
        typeof(SplitAudioStemPreviewCard),
        new PropertyMetadata(string.Empty, OnPreviewSourceChanged));

    public double DurationMilliseconds
    {
        get => (double)GetValue(DurationMillisecondsProperty);
        set => SetValue(DurationMillisecondsProperty, value);
    }

    public static readonly DependencyProperty DurationMillisecondsProperty = DependencyProperty.Register(
        nameof(DurationMilliseconds),
        typeof(double),
        typeof(SplitAudioStemPreviewCard),
        new PropertyMetadata(0d, OnPreviewSourceChanged));

    public ICommand? RevealCommand
    {
        get => (ICommand?)GetValue(RevealCommandProperty);
        set => SetValue(RevealCommandProperty, value);
    }

    public static readonly DependencyProperty RevealCommandProperty = DependencyProperty.Register(
        nameof(RevealCommand),
        typeof(ICommand),
        typeof(SplitAudioStemPreviewCard),
        new PropertyMetadata(null));

    public string RevealButtonText
    {
        get => (string)GetValue(RevealButtonTextProperty);
        set => SetValue(RevealButtonTextProperty, value);
    }

    public static readonly DependencyProperty RevealButtonTextProperty = DependencyProperty.Register(
        nameof(RevealButtonText),
        typeof(string),
        typeof(SplitAudioStemPreviewCard),
        new PropertyMetadata("Reveal file", OnLocalizedTextChanged));

    public string PlayPreviewButtonText
    {
        get => (string)GetValue(PlayPreviewButtonTextProperty);
        set => SetValue(PlayPreviewButtonTextProperty, value);
    }

    public static readonly DependencyProperty PlayPreviewButtonTextProperty = DependencyProperty.Register(
        nameof(PlayPreviewButtonText),
        typeof(string),
        typeof(SplitAudioStemPreviewCard),
        new PropertyMetadata("Play preview", OnLocalizedTextChanged));

    public string PausePreviewButtonText
    {
        get => (string)GetValue(PausePreviewButtonTextProperty);
        set => SetValue(PausePreviewButtonTextProperty, value);
    }

    public static readonly DependencyProperty PausePreviewButtonTextProperty = DependencyProperty.Register(
        nameof(PausePreviewButtonText),
        typeof(string),
        typeof(SplitAudioStemPreviewCard),
        new PropertyMetadata("Pause preview", OnLocalizedTextChanged));

    public string DisplayFileName => string.IsNullOrWhiteSpace(FilePath) ? string.Empty : Path.GetFileName(FilePath);

    public bool CanPlayPreview => !_hasLoadFailed && !string.IsNullOrWhiteSpace(FilePath) && _duration > TimeSpan.Zero;

    public string PlayPauseButtonText => IsPlaying ? PausePreviewButtonText : PlayPreviewButtonText;

    public Symbol PlayPauseButtonSymbol => IsPlaying ? Symbol.Pause : Symbol.Play;

    public double TimelineMinimum => 0d;

    public double TimelineMaximum => Math.Max(1d, _duration.TotalMilliseconds);

    public double CurrentPositionMilliseconds
    {
        get => _currentPosition.TotalMilliseconds;
        set => SyncCurrentPosition(TimeSpan.FromMilliseconds(value));
    }

    public string TimelinePositionText => $"{FormatCompactTime(_currentPosition)} / {FormatCompactTime(_duration)}";

    private bool IsPlaying
    {
        get => _isPlaying;
        set
        {
            if (_isPlaying == value)
            {
                return;
            }

            _isPlaying = value;
            OnPropertyChanged(nameof(PlayPauseButtonText));
            OnPropertyChanged(nameof(PlayPauseButtonSymbol));
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        StopPositionTimer();
        DisposeMediaPlayer();
    }

    private static void OnPreviewSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((SplitAudioStemPreviewCard)d).ResetForSource();

    private static void OnLocalizedTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var card = (SplitAudioStemPreviewCard)d;
        card.OnPropertyChanged(nameof(card.RevealButtonText));
        card.OnPropertyChanged(nameof(card.PlayPauseButtonText));
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        SplitAudioPlaybackCoordinator.Register(this);
        ResetForSource();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        SplitAudioPlaybackCoordinator.Unregister(this);
        StopPositionTimer();
        DisposeMediaPlayer();
    }

    private async void OnPlayPauseClick(object sender, RoutedEventArgs e)
    {
        if (!CanPlayPreview)
        {
            return;
        }

        if (!await EnsureMediaReadyAsync())
        {
            return;
        }

        if (IsPlaying)
        {
            PausePlayback();
            return;
        }

        await SplitAudioPlaybackCoordinator.RequestPlaybackAsync(this);
        var target = ClampToDuration(_currentPosition);
        if (_duration > TimeSpan.Zero && target >= _duration)
        {
            target = TimeSpan.Zero;
        }

        await SeekToAsync(target, resumePlayback: true);
    }

    private void OnTimelineSliderPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!CanPlayPreview)
        {
            return;
        }

        _isTimelinePointerPressed = true;
        _resumePlaybackAfterSeek = IsPlaying;
        if (IsPlaying)
        {
            PausePlayback();
        }
    }

    private void OnTimelineSliderPointerReleased(object sender, PointerRoutedEventArgs e) =>
        _ = CommitTimelineSeekAsync();

    private void OnTimelineSliderPointerCaptureLost(object sender, PointerRoutedEventArgs e) =>
        _ = CommitTimelineSeekAsync();

    private void OnTimelineSliderValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (!_isTimelinePointerPressed)
        {
            return;
        }

        SyncCurrentPosition(TimeSpan.FromMilliseconds(e.NewValue));
    }

    private void OnPositionTimerTick(DispatcherQueueTimer sender, object args)
    {
        if (_mediaPlayer is null || _isTimelinePointerPressed)
        {
            return;
        }

        SyncCurrentPosition(ClampToDuration(_mediaPlayer.PlaybackSession.Position));
    }

    private async Task CommitTimelineSeekAsync()
    {
        if (!_isTimelinePointerPressed)
        {
            return;
        }

        _isTimelinePointerPressed = false;
        var shouldResumePlayback = _resumePlaybackAfterSeek;
        _resumePlaybackAfterSeek = false;
        await SeekToAsync(TimeSpan.FromMilliseconds(TimelineSlider.Value), shouldResumePlayback);
    }

    private async Task<bool> EnsureMediaReadyAsync()
    {
        if (_mediaPlayer is not null &&
            string.Equals(_loadedFilePath, FilePath, StringComparison.OrdinalIgnoreCase) &&
            !_hasLoadFailed)
        {
            return true;
        }

        if (_loadTask is not null)
        {
            return await _loadTask;
        }

        if (string.IsNullOrWhiteSpace(FilePath) || !File.Exists(FilePath))
        {
            _hasLoadFailed = true;
            OnPropertyChanged(nameof(CanPlayPreview));
            return false;
        }

        var loadTask = LoadMediaAsync();
        _loadTask = loadTask;

        try
        {
            return await loadTask;
        }
        finally
        {
            if (ReferenceEquals(_loadTask, loadTask))
            {
                _loadTask = null;
            }
        }
    }

    private async Task<bool> LoadMediaAsync()
    {
        DisposeMediaPlayer();
        _hasLoadFailed = false;
        _loadedFilePath = FilePath;

        var player = new MediaPlayer
        {
            AutoPlay = false
        };

        player.MediaOpened += OnMediaOpened;
        player.MediaEnded += OnMediaEnded;
        player.MediaFailed += OnMediaFailed;
        player.PlaybackSession.PlaybackStateChanged += OnPlaybackStateChanged;

        _mediaPlayer = player;
        _loadCompletionSource = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        player.Source = MediaSource.CreateFromUri(new Uri(FilePath));

        return await _loadCompletionSource.Task;
    }

    private async Task SeekToAsync(TimeSpan position, bool resumePlayback)
    {
        if (!await EnsureMediaReadyAsync())
        {
            return;
        }

        if (_mediaPlayer is null)
        {
            return;
        }

        SetSeeking(true);
        try
        {
            var target = ClampToDuration(position);
            _mediaPlayer.PlaybackSession.Position = target;
            SyncCurrentPosition(target);

            if (resumePlayback)
            {
                await SplitAudioPlaybackCoordinator.RequestPlaybackAsync(this);
                _mediaPlayer.Play();
            }
            else
            {
                _mediaPlayer.Pause();
            }
        }
        finally
        {
            SetSeeking(false);
        }
    }

    private void PausePlayback()
    {
        if (_mediaPlayer is null)
        {
            SplitAudioPlaybackCoordinator.NotifyPaused(this);
            return;
        }

        _mediaPlayer.Pause();
        SyncCurrentPosition(ClampToDuration(_mediaPlayer.PlaybackSession.Position));
        SetPlaying(false);
        SplitAudioPlaybackCoordinator.NotifyPaused(this);
    }

    private void OnMediaOpened(MediaPlayer sender, object args)
    {
        RunOnUiThread(() =>
        {
            if (_mediaPlayer != sender)
            {
                return;
            }

            var naturalDuration = sender.PlaybackSession.NaturalDuration;
            _duration = naturalDuration > TimeSpan.Zero
                ? naturalDuration
                : TimeSpan.FromMilliseconds(Math.Max(0d, DurationMilliseconds));
            _currentPosition = TimeSpan.Zero;
            _hasLoadFailed = false;
            RaiseTimelineChanged();
            _loadCompletionSource?.TrySetResult(true);
            _loadCompletionSource = null;
        });
    }

    private void OnMediaEnded(MediaPlayer sender, object args)
    {
        RunOnUiThread(() =>
        {
            if (_mediaPlayer != sender)
            {
                return;
            }

            StopPositionTimer();
            SetPlaying(false);
            SyncCurrentPosition(_duration);
            SplitAudioPlaybackCoordinator.NotifyPaused(this);
        });
    }

    private void OnMediaFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args)
    {
        RunOnUiThread(() =>
        {
            if (_mediaPlayer != sender)
            {
                return;
            }

            StopPositionTimer();
            SetPlaying(false);
            _hasLoadFailed = true;
            OnPropertyChanged(nameof(CanPlayPreview));
            SplitAudioPlaybackCoordinator.NotifyPaused(this);
            _loadCompletionSource?.TrySetResult(false);
            _loadCompletionSource = null;
        });
    }

    private void OnPlaybackStateChanged(MediaPlaybackSession sender, object args)
    {
        RunOnUiThread(() =>
        {
            if (_mediaPlayer?.PlaybackSession != sender)
            {
                return;
            }

            var isPlaying = sender.PlaybackState == MediaPlaybackState.Playing;
            SetPlaying(isPlaying);
            if (isPlaying)
            {
                StartPositionTimer();
                return;
            }

            StopPositionTimer();
            if (!_isTimelinePointerPressed)
            {
                SyncCurrentPosition(ClampToDuration(sender.Position));
            }
        });
    }

    private void ResetForSource()
    {
        StopPositionTimer();
        _resumePlaybackAfterSeek = false;
        _isTimelinePointerPressed = false;
        _hasLoadFailed = false;
        _loadTask = null;
        _loadedFilePath = string.Empty;
        SetSeeking(false);
        SetPlaying(false);
        SplitAudioPlaybackCoordinator.NotifyPaused(this);
        DisposeMediaPlayer();
        _duration = TimeSpan.FromMilliseconds(Math.Max(0d, DurationMilliseconds));
        _currentPosition = TimeSpan.Zero;
        OnPropertyChanged(nameof(DisplayFileName));
        OnPropertyChanged(nameof(CanPlayPreview));
        RaiseTimelineChanged();
    }

    private void DisposeMediaPlayer()
    {
        if (_mediaPlayer is null)
        {
            _loadCompletionSource?.TrySetResult(false);
            _loadCompletionSource = null;
            return;
        }

        var player = _mediaPlayer;
        _mediaPlayer = null;
        SplitAudioPlaybackCoordinator.NotifyPaused(this);

        _loadCompletionSource?.TrySetResult(false);
        _loadCompletionSource = null;

        player.MediaOpened -= OnMediaOpened;
        player.MediaEnded -= OnMediaEnded;
        player.MediaFailed -= OnMediaFailed;
        player.PlaybackSession.PlaybackStateChanged -= OnPlaybackStateChanged;
        player.Source = null;
        player.Dispose();
    }

    private void StartPositionTimer()
    {
        if (_positionTimer.IsRunning)
        {
            return;
        }

        _positionTimer.Start();
    }

    private void StopPositionTimer()
    {
        if (_positionTimer.IsRunning)
        {
            _positionTimer.Stop();
        }
    }

    private void SetPlaying(bool isPlaying) => IsPlaying = isPlaying;

    private void SetSeeking(bool isSeeking)
    {
        if (_isSeeking == isSeeking)
        {
            return;
        }

        _isSeeking = isSeeking;
    }

    private void SyncCurrentPosition(TimeSpan position)
    {
        var normalized = ClampToDuration(position);
        if (_currentPosition == normalized)
        {
            return;
        }

        _currentPosition = normalized;
        RaiseTimelineChanged();
    }

    private void RaiseTimelineChanged()
    {
        OnPropertyChanged(nameof(TimelineMinimum));
        OnPropertyChanged(nameof(TimelineMaximum));
        OnPropertyChanged(nameof(CurrentPositionMilliseconds));
        OnPropertyChanged(nameof(TimelinePositionText));
    }

    private TimeSpan ClampToDuration(TimeSpan position)
    {
        if (position < TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        if (_duration > TimeSpan.Zero && position > _duration)
        {
            return _duration;
        }

        return position;
    }

    private string FormatTimelineThumbToolTip(TimeSpan position) => FormatFullTime(position);

    private static string FormatCompactTime(TimeSpan value)
    {
        if (value < TimeSpan.Zero)
        {
            value = TimeSpan.Zero;
        }

        return value.TotalHours >= 1d
            ? value.ToString(@"hh\:mm\:ss")
            : value.ToString(@"mm\:ss");
    }

    private static string FormatFullTime(TimeSpan value)
    {
        if (value < TimeSpan.Zero)
        {
            value = TimeSpan.Zero;
        }

        return value.TotalHours >= 1d
            ? value.ToString(@"hh\:mm\:ss\.fff")
            : value.ToString(@"mm\:ss\.fff");
    }

    private void RunOnUiThread(Action action)
    {
        if (_dispatcherQueue.HasThreadAccess)
        {
            action();
            return;
        }

        _dispatcherQueue.TryEnqueue(() => action());
    }

    private void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    public Task PauseForPlaybackCoordinationAsync()
    {
        if (_dispatcherQueue.HasThreadAccess)
        {
            PausePlayback();
            return Task.CompletedTask;
        }

        var completionSource = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _dispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                PausePlayback();
                completionSource.TrySetResult(true);
            }
            catch (Exception exception)
            {
                completionSource.TrySetException(exception);
            }
        });

        return completionSource.Task;
    }
}
