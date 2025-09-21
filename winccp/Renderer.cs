using System.Text;

namespace winccp
{
    internal class Renderer
    {
        private readonly Config _config;
        private readonly StringBuilder _buffer = new();
        private readonly Dictionary<string, string> _widgetCache = new();
        private (int width, int height) _lastConsoleSize;
        private string? _lastFrame;
        private readonly object _renderLock = new();

        // ANSI escape codes
        private const string ESC = "\x1b";
        private const string CLEAR_SCREEN = "\x1b[2J";
        private const string HIDE_CURSOR = "\x1b[?25l";
        private const string SHOW_CURSOR = "\x1b[?25h";
        private const string RESET_COLOR = "\x1b[0m";
        private const string HOME = "\x1b[H";

        public Renderer(Config config)
        {
            _config = config;
            Console.OutputEncoding = Encoding.UTF8;
            Console.Write(HIDE_CURSOR);
        }

        public void Render(MediaState mediaState, bool forceRedraw = false)
        {
            lock (_renderLock)
            {
                var currentSize = (Console.WindowWidth, Console.WindowHeight);
                bool sizeChanged = currentSize != _lastConsoleSize;

                if (sizeChanged)
                {
                    Console.Write(CLEAR_SCREEN);
                    _widgetCache.Clear();
                    _lastConsoleSize = currentSize;
                }

                // Build the frame
                var frame = BuildFrame(mediaState, currentSize, forceRedraw || sizeChanged);

                // Only output if something changed
                if (frame != _lastFrame || sizeChanged)
                {
                    Console.Write(frame);
                    _lastFrame = frame;
                }
            }
        }

        private string BuildFrame(MediaState mediaState, (int width, int height) consoleSize, bool forceRedraw)
        {
            _buffer.Clear();
            _buffer.Append(HOME); // Move cursor to home position

            var layout = CalculateLayout(consoleSize);

            // Render each widget to buffer
            foreach (var widget in _config.WidgetsOrder)
            {
                if (!layout.TryGetValue(widget.ToLowerInvariant(), out var position))
                    continue;

                var content = GenerateWidgetContent(widget, mediaState, position, consoleSize);

                // Check cache to avoid unnecessary redraws
                var cacheKey = $"{widget}_{position.Y}";
                if (!forceRedraw && _widgetCache.TryGetValue(cacheKey, out var cached) && cached == content)
                    continue;

                _widgetCache[cacheKey] = content;
                RenderWidgetToBuffer(content, position);
            }

            return _buffer.ToString();
        }

        private Dictionary<string, WidgetPosition> CalculateLayout((int width, int height) consoleSize)
        {
            var layout = new Dictionary<string, WidgetPosition>();
            var widgets = new List<(string name, int height)>();

            // Calculate widget heights
            foreach (var widget in _config.WidgetsOrder)
            {
                var widgetLower = widget.ToLowerInvariant();
                var height = widgetLower switch
                {
                    "albumcover" => GetAlbumCoverHeight(consoleSize),
                    "infos" => 2,
                    "progressbar" => (_config.ProgressBar?.Time ?? true) ? 2 : 1,
                    "source" => 1,
                    _ => 0
                };

                if (height > 0 && IsWidgetEnabled(widgetLower))
                    widgets.Add((widgetLower, height));
            }

            // Calculate total height including spacing
            int totalHeight = widgets.Sum(w => w.height) + (widgets.Count - 1); // 1 line spacing between widgets

            // Center vertically
            int startY = Math.Max(1, (consoleSize.height - totalHeight) / 2);
            int currentY = startY;

            foreach (var (name, height) in widgets)
            {
                layout[name] = new WidgetPosition
                {
                    Y = currentY,
                    Height = height,
                    Width = consoleSize.width
                };
                currentY += height + 1;
            }

            return layout;
        }

        private int GetAlbumCoverHeight((int width, int height) consoleSize)
        {
            // Dynamic height based on console size
            return Math.Clamp(consoleSize.height / 3, 8, 20);
        }

        private bool IsWidgetEnabled(string widget)
        {
            return widget switch
            {
                "albumcover" => _config.AlbumCover?.Show ?? true,
                "infos" => _config.Infos?.Show ?? true,
                "progressbar" => _config.ProgressBar?.Show ?? true,
                "source" => _config.Source?.Show ?? false,
                _ => false
            };
        }

        private string GenerateWidgetContent(string widget, MediaState mediaState, WidgetPosition position, (int width, int height) consoleSize)
        {
            return widget.ToLowerInvariant() switch
            {
                "albumcover" => GenerateAlbumCover(mediaState, position, consoleSize),
                "infos" => GenerateInfos(mediaState, position),
                "progressbar" => GenerateProgressBar(mediaState, position),
                "source" => GenerateSource(mediaState, position),
                _ => string.Empty
            };
        }

        private string GenerateAlbumCover(MediaState mediaState, WidgetPosition position, (int width, int height) consoleSize)
        {
            if (mediaState.AlbumCoverBytes == null || mediaState.AlbumCoverBytes.Length == 0)
                return string.Empty;

            try
            {
                // Calculate proper size for sixel image
                var maxPixelWidth = Math.Min(position.Width * 8, 400);
                var maxPixelHeight = Math.Min(position.Height * 16, 320);

                using var ms = new MemoryStream(mediaState.AlbumCoverBytes);
                using var image = SixLabors.ImageSharp.Image.Load<SixLabors.ImageSharp.PixelFormats.Rgba32>(ms);

                // Maintain aspect ratio
                var aspectRatio = (double)image.Width / image.Height;
                int targetWidth, targetHeight;

                if (aspectRatio > 1) // Wider than tall
                {
                    targetWidth = maxPixelWidth;
                    targetHeight = (int)(targetWidth / aspectRatio);
                    if (targetHeight > maxPixelHeight)
                    {
                        targetHeight = maxPixelHeight;
                        targetWidth = (int)(targetHeight * aspectRatio);
                    }
                }
                else // Taller than wide
                {
                    targetHeight = maxPixelHeight;
                    targetWidth = (int)(targetHeight * aspectRatio);
                    if (targetWidth > maxPixelWidth)
                    {
                        targetWidth = maxPixelWidth;
                        targetHeight = (int)(targetWidth / aspectRatio);
                    }
                }

                // Generate sixel with proper centering
                var sixel = SixelEncoder.ImageToSixel(image, new SixelEncoder.SizeLimit(targetWidth, targetHeight));

                // Calculate horizontal centering for sixel
                // Sixel images need special handling for centering
                int charWidth = targetWidth / 10; // Approximate character width
                int leftPadding = Math.Max(0, (position.Width - charWidth) / 2);

                // Apply centering by moving cursor before sixel output
                if (leftPadding > 0)
                {
                    // This will center the sixel image
                    return $"{ESC}[{leftPadding}C{sixel}";
                }

                return sixel;
            }
            catch
            {
                return string.Empty;
            }
        }

        private string GenerateInfos(MediaState mediaState, WidgetPosition position)
        {
            var lines = new List<string>();

            // Title
            string title = string.IsNullOrWhiteSpace(mediaState.Title) ? "Unknown Title" : mediaState.Title;
            if (title.Length > position.Width - 4)
                title = title.Substring(0, position.Width - 7) + "...";

            // Artist/Album
            string artist = mediaState.Artist ?? "Unknown Artist";
            string album = mediaState.Album ?? "";
            string artistAlbum = string.IsNullOrEmpty(album) ? artist : $"{artist} - {album}";

            if (artistAlbum.Length > position.Width - 4)
                artistAlbum = artistAlbum.Substring(0, position.Width - 7) + "...";

            // Apply color and center
            var color = GetAnsiColor(_config.Infos?.Color ?? "White");
            lines.Add($"{color}{CenterText(title, position.Width)}{RESET_COLOR}");
            lines.Add($"{color}{CenterText(artistAlbum, position.Width)}{RESET_COLOR}");

            return string.Join("\n", lines);
        }

        private string GenerateProgressBar(MediaState mediaState, WidgetPosition position)
        {
            var lines = new List<string>();

            // Calculate progress
            int barWidth = Math.Min(50, Math.Max(10, position.Width - 20));
            double progress = 0;

            if (mediaState.Duration.TotalSeconds > 0)
            {
                progress = Math.Clamp(mediaState.Position.TotalSeconds / mediaState.Duration.TotalSeconds, 0, 1);
            }

            int filledCount = (int)(progress * barWidth);
            int emptyCount = barWidth - filledCount;

            // Build the bar with colors
            var filledColor = GetAnsiColor(_config.ProgressBar?.FilledColor ?? "Green");
            var emptyColor = GetAnsiColor(_config.ProgressBar?.EmptyColor ?? "DarkGray");
            var filledChar = (_config.ProgressBar?.FilledSymbol ?? "━")[0];
            var emptyChar = (_config.ProgressBar?.EmptySymbol ?? "━")[0];

            var bar = new StringBuilder();
            if (filledCount > 0)
            {
                bar.Append(filledColor);
                bar.Append(new string(filledChar, filledCount));
            }
            if (emptyCount > 0)
            {
                bar.Append(emptyColor);
                bar.Append(new string(emptyChar, emptyCount));
            }
            bar.Append(RESET_COLOR);

            lines.Add(CenterText(bar.ToString(), position.Width));

            // Time display
            if (_config.ProgressBar?.Time ?? true)
            {
                string posStr = FormatTime(mediaState.Position);
                string durStr = mediaState.Duration == TimeSpan.Zero ? "--:--" : FormatTime(mediaState.Duration);
                string timeStr = $"{posStr} / {durStr}";

                var timeColor = GetAnsiColor(_config.ProgressBar?.TimeColor ?? "Yellow");
                lines.Add($"{timeColor}{CenterText(timeStr, position.Width)}{RESET_COLOR}");
            }

            return string.Join("\n", lines);
        }

        private string GenerateSource(MediaState mediaState, WidgetPosition position)
        {
            string source = mediaState.SourceApp ?? "Unknown Source";
            var color = GetAnsiColor(_config.Source?.Color ?? "DarkGray");
            return $"{color}{CenterText($"♫ {source} ♫", position.Width)}{RESET_COLOR}";
        }

        private void RenderWidgetToBuffer(string content, WidgetPosition position)
        {
            if (string.IsNullOrEmpty(content)) return;

            var lines = content.Split('\n');
            for (int i = 0; i < lines.Length && i < position.Height; i++)
            {
                // Position cursor and write line
                _buffer.Append($"{ESC}[{position.Y + i};1H"); // Move to line start
                _buffer.Append(lines[i]);

                // Clear to end of line if needed
                _buffer.Append($"{ESC}[K");
            }
        }

        private string CenterText(string text, int width)
        {
            // Account for ANSI codes when calculating visible length
            var visibleLength = GetVisibleLength(text);
            if (visibleLength >= width) return text;

            int padding = (width - visibleLength) / 2;
            return new string(' ', padding) + text;
        }

        private int GetVisibleLength(string text)
        {
            // Remove ANSI codes to calculate actual visible length
            var cleaned = System.Text.RegularExpressions.Regex.Replace(text, @"\x1b\[[0-9;]*m", "");
            return cleaned.Length;
        }

        private string FormatTime(TimeSpan time)
        {
            if (time.TotalHours >= 1)
                return time.ToString(@"h\:mm\:ss");
            return time.ToString(@"m\:ss");
        }

        private string GetAnsiColor(string colorName)
        {
            return colorName?.ToLower() switch
            {
                "black" => $"{ESC}[30m",
                "darkred" => $"{ESC}[31m",
                "darkgreen" => $"{ESC}[32m",
                "darkyellow" => $"{ESC}[33m",
                "darkblue" => $"{ESC}[34m",
                "darkmagenta" => $"{ESC}[35m",
                "darkcyan" => $"{ESC}[36m",
                "gray" or "grey" => $"{ESC}[37m",
                "darkgray" or "darkgrey" => $"{ESC}[90m",
                "red" => $"{ESC}[91m",
                "green" => $"{ESC}[92m",
                "yellow" => $"{ESC}[93m",
                "blue" => $"{ESC}[94m",
                "magenta" => $"{ESC}[95m",
                "cyan" => $"{ESC}[96m",
                "white" => $"{ESC}[97m",
                _ => $"{ESC}[37m" // Default to gray
            };
        }

        public void Cleanup()
        {
            Console.Write(SHOW_CURSOR);
            Console.Write(RESET_COLOR);
            Console.Clear();
        }
    }

    internal record WidgetPosition
    {
        public int Y { get; init; }
        public int Height { get; init; }
        public int Width { get; init; }
    }
}