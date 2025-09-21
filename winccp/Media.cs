using Windows.Media.Control;

namespace winccp
{
    public class MediaState
    {
        public string? Title { get; set; }
        public string? Artist { get; set; }
        public string? Album { get; set; }
        public TimeSpan Position { get; set; }
        public TimeSpan Duration { get; set; }
        public byte[]? AlbumCoverBytes { get; set; }
        public string? SourceApp { get; set; }
        public bool IsPlaying { get; set; }
        public DateTime LastUpdate { get; set; }

        // Hash for change detection
        public int GetContentHash()
        {
            return HashCode.Combine(Title, Artist, Album, SourceApp);
        }

        public int GetProgressHash()
        {
            return HashCode.Combine(Position.Ticks / TimeSpan.TicksPerSecond, Duration.Ticks / TimeSpan.TicksPerSecond, IsPlaying);
        }
    }

    public class MediaController
    {
        private GlobalSystemMediaTransportControlsSessionManager? _manager;
        private GlobalSystemMediaTransportControlsSession? _currentSession;
        private readonly MediaState _mediaState = new();
        private int _lastContentHash;
        private int _lastProgressHash;

        public event EventHandler<MediaState>? MediaChanged;
        public event EventHandler<MediaState>? ProgressChanged;

        public async Task<bool> InitializeAsync()
        {
            try
            {
                _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync().AsTask();
                _manager.SessionsChanged += OnSessionsChanged;
                _manager.CurrentSessionChanged += OnCurrentSessionChanged;

                _currentSession = _manager.GetCurrentSession() ?? _manager.GetSessions().FirstOrDefault();

                if (_currentSession != null)
                {
                    AttachSessionHandlers(_currentSession);
                    await RefreshAllAsync();
                    return true;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to initialize media controller: {ex.Message}");
            }

            return false;
        }

        public MediaState GetCurrentState() => _mediaState;

        // Fetch actual position from the session
        public async Task UpdatePositionAsync()
        {
            if (_currentSession == null) return;

            try
            {
                // Get the actual timeline from the session
                var timeline = _currentSession.GetTimelineProperties();
                if (timeline != null)
                {
                    var oldPosition = _mediaState.Position;
                    _mediaState.Position = timeline.Position;
                    _mediaState.Duration = timeline.EndTime;

                    // If playing, estimate position based on time elapsed
                    if (_mediaState.IsPlaying && _mediaState.LastUpdate != default)
                    {
                        var elapsed = DateTime.UtcNow - _mediaState.LastUpdate;
                        if (elapsed.TotalSeconds < 1) // Only estimate for small time deltas
                        {
                            _mediaState.Position = oldPosition + elapsed;
                            // Clamp to duration
                            if (_mediaState.Position > _mediaState.Duration && _mediaState.Duration > TimeSpan.Zero)
                            {
                                _mediaState.Position = _mediaState.Duration;
                            }
                        }
                    }

                    _mediaState.LastUpdate = DateTime.UtcNow;

                    // Check if progress actually changed
                    var currentProgressHash = _mediaState.GetProgressHash();
                    if (currentProgressHash != _lastProgressHash)
                    {
                        _lastProgressHash = currentProgressHash;
                        ProgressChanged?.Invoke(this, _mediaState);
                    }
                }
            }
            catch { }
        }

        // Media control methods
        public async Task TogglePlayPauseAsync()
        {
            if (_currentSession != null)
            {
                await _currentSession.TryTogglePlayPauseAsync().AsTask();
                await Task.Delay(50); // Small delay to let the state update
                await RefreshPlaybackInfoAsync();
            }
        }

        public async Task SkipNextAsync()
        {
            if (_currentSession != null)
            {
                await _currentSession.TrySkipNextAsync().AsTask();
                await Task.Delay(50);
                await RefreshAllAsync();
            }
        }

        public async Task SkipPreviousAsync()
        {
            if (_currentSession != null)
            {
                await _currentSession.TrySkipPreviousAsync().AsTask();
                await Task.Delay(50);
                await RefreshAllAsync();
            }
        }

        private void AttachSessionHandlers(GlobalSystemMediaTransportControlsSession session)
        {
            // Detach previous handlers
            if (_currentSession != null && _currentSession != session)
            {
                try
                {
                    _currentSession.MediaPropertiesChanged -= OnMediaPropertiesChanged;
                    _currentSession.PlaybackInfoChanged -= OnPlaybackInfoChanged;
                    _currentSession.TimelinePropertiesChanged -= OnTimelinePropertiesChanged;
                }
                catch { }
            }

            _currentSession = session;
            _currentSession.MediaPropertiesChanged += OnMediaPropertiesChanged;
            _currentSession.PlaybackInfoChanged += OnPlaybackInfoChanged;
            _currentSession.TimelinePropertiesChanged += OnTimelinePropertiesChanged;
        }

        private async void OnMediaPropertiesChanged(object? sender, object args)
        {
            await RefreshMediaPropertiesAsync();

            var currentContentHash = _mediaState.GetContentHash();
            if (currentContentHash != _lastContentHash)
            {
                _lastContentHash = currentContentHash;
                MediaChanged?.Invoke(this, _mediaState);
            }
        }

        private async void OnPlaybackInfoChanged(object? sender, object args)
        {
            await RefreshPlaybackInfoAsync();
            ProgressChanged?.Invoke(this, _mediaState);
        }

        private async void OnTimelinePropertiesChanged(object? sender, object args)
        {
            await RefreshTimelineAsync();
            ProgressChanged?.Invoke(this, _mediaState);
        }

        private void OnSessionsChanged(object? sender, object args) => HandleSessionChange();
        private void OnCurrentSessionChanged(object? sender, object args) => HandleSessionChange();

        private async void HandleSessionChange()
        {
            var newSession = _manager?.GetCurrentSession();
            if (newSession != null && newSession != _currentSession)
            {
                AttachSessionHandlers(newSession);
                await RefreshAllAsync();
                MediaChanged?.Invoke(this, _mediaState);
            }
        }

        private async Task RefreshAllAsync()
        {
            await RefreshMediaPropertiesAsync();
            await RefreshPlaybackInfoAsync();
            await RefreshTimelineAsync();
        }

        private async Task RefreshMediaPropertiesAsync()
        {
            try
            {
                if (_currentSession == null) return;

                var mediaProps = await _currentSession.TryGetMediaPropertiesAsync().AsTask();
                if (mediaProps != null)
                {
                    _mediaState.Title = mediaProps.Title;
                    _mediaState.Artist = mediaProps.Artist;
                    _mediaState.Album = mediaProps.AlbumTitle;
                    _mediaState.SourceApp = _currentSession.SourceAppUserModelId;

                    // Get album cover
                    _mediaState.AlbumCoverBytes = await GetAlbumCoverBytesAsync(mediaProps);
                    _mediaState.LastUpdate = DateTime.UtcNow;
                }
            }
            catch { }
        }

        private async Task RefreshPlaybackInfoAsync()
        {
            try
            {
                if (_currentSession == null) return;

                var playbackInfo = _currentSession.GetPlaybackInfo();
                if (playbackInfo != null)
                {
                    var wasPlaying = _mediaState.IsPlaying;
                    _mediaState.IsPlaying = playbackInfo.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;

                    // If state changed, update timeline too
                    if (wasPlaying != _mediaState.IsPlaying)
                    {
                        await RefreshTimelineAsync();
                    }

                    _mediaState.LastUpdate = DateTime.UtcNow;
                }
            }
            catch { }
        }

        private async Task RefreshTimelineAsync()
        {
            try
            {
                if (_currentSession == null) return;

                var timeline = _currentSession.GetTimelineProperties();
                if (timeline != null)
                {
                    _mediaState.Position = timeline.Position;
                    _mediaState.Duration = timeline.EndTime;
                    _mediaState.LastUpdate = DateTime.UtcNow;
                }
            }
            catch { }
        }

        private async Task<byte[]?> GetAlbumCoverBytesAsync(GlobalSystemMediaTransportControlsSessionMediaProperties mediaProps)
        {
            try
            {
                var thumbRef = mediaProps.Thumbnail;
                if (thumbRef == null) return null;

                using var stream = await thumbRef.OpenReadAsync().AsTask();
                if (stream == null) return null;

                using var reader = new Windows.Storage.Streams.DataReader(stream.GetInputStreamAt(0));
                uint size = (uint)stream.Size;
                await reader.LoadAsync(size);
                byte[] buffer = new byte[size];
                reader.ReadBytes(buffer);
                return buffer;
            }
            catch
            {
                return null;
            }
        }
    }
}