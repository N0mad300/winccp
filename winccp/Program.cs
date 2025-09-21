namespace winccp
{
    static class Program
    {
        private static MediaController? s_mediaController;
        private static Renderer? s_renderer;
        private static Config s_config = new Config();
        private static CancellationTokenSource? s_cts;
        private static DateTime s_lastRender = DateTime.MinValue;

        static async Task<int> Main(string[] args)
        {
            // Set up console
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Console.TreatControlCAsInput = true;

            // Load config
            try
            {
                var configPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".config", "winccp", "config.ini");

                if (File.Exists(configPath))
                {
                    s_config = ConfigLoader.Load(configPath);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: failed to load config.ini — using defaults. {ex.Message}");
            }

            if (s_config.WidgetsOrder == null || s_config.WidgetsOrder.Count == 0)
            {
                s_config.WidgetsOrder = new List<string>
                {
                    "AlbumCover","Infos","ProgressBar","Source"
                };
            }

            // Initialize components
            s_mediaController = new MediaController();
            s_renderer = new Renderer(s_config);

            if (!await s_mediaController.InitializeAsync())
            {
                Console.WriteLine("No media sessions found. Waiting for media to start...");

                // Wait for a media session to appear
                int retryCount = 0;
                while (!await s_mediaController.InitializeAsync() && retryCount < 30)
                {
                    await Task.Delay(1000);
                    retryCount++;
                }

                if (retryCount >= 30)
                {
                    Console.WriteLine("No media sessions found after 30 seconds. Exiting.");
                    return 1;
                }
            }

            // Set up event handlers
            s_mediaController.MediaChanged += OnMediaChanged;
            s_mediaController.ProgressChanged += OnProgressChanged;

            // Set up cancellation
            s_cts = new CancellationTokenSource();
            Console.CancelKeyPress += (sender, e) =>
            {
                e.Cancel = true;
                s_cts.Cancel();
            };

            // Initial render
            s_renderer.Render(s_mediaController.GetCurrentState(), forceRedraw: true);

            // Start main loops
            var tasks = new[]
            {
                PositionUpdateLoop(s_cts.Token),
                InputLoop(s_cts.Token),
                RenderLoop(s_cts.Token)
            };

            try
            {
                await Task.WhenAny(tasks);
            }
            catch (OperationCanceledException)
            {
                // Normal cancellation
            }
            finally
            {
                // Cleanup
                s_renderer?.Cleanup();
                s_cts?.Cancel();
                Console.TreatControlCAsInput = false;
            }

            return 0;
        }

        private static void OnMediaChanged(object? sender, MediaState state)
        {
            s_renderer?.Render(state, forceRedraw: true);
            s_lastRender = DateTime.UtcNow;
        }

        private static void OnProgressChanged(object? sender, MediaState state)
        {
            // Throttle progress updates to avoid excessive rendering
            if ((DateTime.UtcNow - s_lastRender).TotalMilliseconds >= 100)
            {
                s_renderer?.Render(state, forceRedraw: false);
                s_lastRender = DateTime.UtcNow;
            }
        }

        private static async Task PositionUpdateLoop(CancellationToken ct)
        {
            // This loop fetches the actual position from the media session
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    if (s_mediaController != null)
                    {
                        await s_mediaController.UpdatePositionAsync();
                    }

                    // Update position every 100ms for smooth progress bar
                    await Task.Delay(100, ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    // Ignore other exceptions
                }
            }
        }

        private static async Task RenderLoop(CancellationToken ct)
        {
            // Ensures regular UI updates even if no events fire
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    // Only render if we haven't rendered recently
                    if ((DateTime.UtcNow - s_lastRender).TotalMilliseconds >= 200)
                    {
                        if (s_mediaController != null && s_renderer != null)
                        {
                            var state = s_mediaController.GetCurrentState();
                            s_renderer.Render(state, forceRedraw: false);
                            s_lastRender = DateTime.UtcNow;
                        }
                    }

                    // Check less frequently to reduce CPU usage
                    await Task.Delay(200, ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    // Ignore other exceptions
                }
            }
        }

        private static async Task InputLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    // Use async key reading to avoid blocking
                    if (Console.KeyAvailable)
                    {
                        var key = Console.ReadKey(true);

                        if (s_cts != null && (key.Key == ConsoleKey.Q || key.Key == ConsoleKey.Escape))
                        {
                            s_cts.Cancel();
                            break;
                        }

                        if (s_mediaController != null)
                        {
                            switch (key.Key)
                            {
                                case ConsoleKey.Spacebar:
                                case ConsoleKey.P:
                                    await s_mediaController.TogglePlayPauseAsync();
                                    break;

                                case ConsoleKey.LeftArrow:
                                case ConsoleKey.B:
                                    await s_mediaController.SkipPreviousAsync();
                                    break;

                                case ConsoleKey.RightArrow:
                                case ConsoleKey.N:
                                    await s_mediaController.SkipNextAsync();
                                    break;

                                case ConsoleKey.R:
                                    // Force refresh
                                    s_renderer?.Render(s_mediaController.GetCurrentState(), forceRedraw: true);
                                    break;
                            }
                        }
                    }

                    // Small delay to prevent CPU spinning
                    await Task.Delay(50, ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Input error: {ex.Message}");
                }
            }
        }
    }
}