using System.Text;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Processing.Processors.Quantization;

namespace winccp
{
    internal static class SixelEncoder
    {
        private const char SixelEmpty = '?';
        private const char SixelColorStart = '#';
        private const char SixelRepeat = '!';
        private const char SixelDECGCR = '$';
        private const char SixelDECGNL = '-';
        private const string SixelStart = $"\eP0;1q";
        private const string SixelEnd = $"\e\\";
        private const string SixelTransparentColor = "#0;2;0;0;0";
        private const string SixelRasterAttributes = "\"1;1;";

        private static (int width, int height) _cellSize = GetCellSize();
        public struct SizeLimit
        {
            public int? MaxWidth { get; set; }
            public int? MaxHeight { get; set; }

            public SizeLimit(int? maxWidth = null, int? maxHeight = null)
            {
                MaxWidth = maxWidth;
                MaxHeight = maxHeight;
            }
        }

        private static string GetControlSequenceResponse(string controlSequence)
        {
            char? c;
            var response = string.Empty;

            Console.Write($"\e{controlSequence}");
            do
            {
                c = Console.ReadKey(true).KeyChar;
                response += c;
            } while (c != 'c' && Console.KeyAvailable);

            return response;
        }

        private static (int width, int hegiht) GetCellSize()
        {
            var response = GetControlSequenceResponse("[16t");

            try
            {
                var parts = response.Split(';', 't');
                return (width: int.Parse(parts[2]), hegiht: int.Parse(parts[1]));
            }
            catch
            {
                // Return the default Windows Terminal size
                // if we can't get the size from the terminal.
                return (width: 10, hegiht: 20);
            }
        }

        public static string ImageToSixel(Image<Rgba32> image)
        {
            return ImageToSixel(image, null);
        }

        public static string ImageToSixel(Image<Rgba32> image, SizeLimit? sizeLimit)
        {
            int cellWidth = Console.WindowWidth;
            image.Mutate(ctx =>
            {
                // Calculate target size based on console width or size limit
                var targetSize = CalculateTargetSize(image.Width, image.Height, cellWidth, sizeLimit);

                if (targetSize.HasValue)
                {
                    // Resize the image to the target size
                    ctx.Resize(new ResizeOptions()
                    {
                        Sampler = KnownResamplers.Bicubic,
                        Size = targetSize.Value,
                        PremultiplyAlpha = false,
                    });
                }

                // Sixel supports 256 colors max
                ctx.Quantize(new OctreeQuantizer(new()
                {
                    MaxColors = 256,
                }));
            });
            var targetFrame = image.Frames[0];
            return FrameToSixelString(targetFrame);
        }

        private static SixLabors.ImageSharp.Size? CalculateTargetSize(int originalWidth, int originalHeight, int cellWidth, SizeLimit? sizeLimit)
        {
            // Start with original dimensions
            int targetWidth = originalWidth;
            int targetHeight = originalHeight;
            bool needsResize = false;

            // Apply console width constraint if cellWidth is valid
            if (cellWidth > 0)
            {
                var pixelWidth = cellWidth * _cellSize.width;
                var pixelHeight = (int)Math.Round((double)originalHeight / originalWidth * pixelWidth);
                targetWidth = pixelWidth;
                targetHeight = pixelHeight;
                needsResize = true;
            }

            // Apply size limits if specified
            if (sizeLimit.HasValue)
            {
                if (sizeLimit.Value.MaxWidth.HasValue && targetWidth > sizeLimit.Value.MaxWidth.Value)
                {
                    // Scale down proportionally based on width limit
                    var ratio = (double)sizeLimit.Value.MaxWidth.Value / targetWidth;
                    targetWidth = sizeLimit.Value.MaxWidth.Value;
                    targetHeight = (int)Math.Round(targetHeight * ratio);
                    needsResize = true;
                }

                if (sizeLimit.Value.MaxHeight.HasValue && targetHeight > sizeLimit.Value.MaxHeight.Value)
                {
                    // Scale down proportionally based on height limit
                    var ratio = (double)sizeLimit.Value.MaxHeight.Value / targetHeight;
                    targetHeight = sizeLimit.Value.MaxHeight.Value;
                    targetWidth = (int)Math.Round(targetWidth * ratio);
                    needsResize = true;
                }
            }

            return needsResize ? new SixLabors.ImageSharp.Size(targetWidth, targetHeight) : null;
        }

        private static string FrameToSixelString(ImageFrame<Rgba32> frame)
        {
            var sixelBuilder = new StringBuilder();
            var palette = new Dictionary<Rgba32, int>();
            var colorCounter = 1;
            sixelBuilder.StartSixel(frame.Width, frame.Height);
            frame.ProcessPixelRows(accessor =>
            {
                for (var y = 0; y < accessor.Height; y++)
                {
                    var pixelRow = accessor.GetRowSpan(y);
                    var c = (char)(SixelEmpty + (1 << (y % 6)));
                    var lastColor = -1;
                    var repeatCounter = 0;
                    foreach (ref var pixel in pixelRow)
                    {
                        if (!palette.TryGetValue(pixel, out var colorIndex))
                        {
                            colorIndex = colorCounter++;
                            palette[pixel] = colorIndex;
                            sixelBuilder.AddColorToPalette(pixel, colorIndex);
                        }
                        var colorId = pixel.A == 0 ? 0 : colorIndex;
                        if (colorId == lastColor || repeatCounter == 0)
                        {
                            lastColor = colorId;
                            repeatCounter++;
                            continue;
                        }
                        if (repeatCounter > 1)
                        {
                            sixelBuilder.AppendRepeatEntry(lastColor, repeatCounter, c);
                        }
                        else
                        {
                            sixelBuilder.AppendSixelEntry(lastColor, c);
                        }
                        lastColor = colorId;
                        repeatCounter = 1;
                    }
                    if (repeatCounter > 1)
                    {
                        sixelBuilder.AppendRepeatEntry(lastColor, repeatCounter, c);
                    }
                    else
                    {
                        sixelBuilder.AppendSixelEntry(lastColor, c);
                    }
                    sixelBuilder.Append(SixelDECGCR);
                    if (y % 6 == 5)
                    {
                        sixelBuilder.Append(SixelDECGNL);
                    }
                }
            });
            sixelBuilder.Append(SixelEnd);
            return sixelBuilder.ToString();
        }

        private static void AddColorToPalette(this StringBuilder sixelBuilder,
                                              Rgba32 pixel,
                                              int colorIndex)
        {
            var r = (int)Math.Round(pixel.R / 255.0 * 100);
            var g = (int)Math.Round(pixel.G / 255.0 * 100);
            var b = (int)Math.Round(pixel.B / 255.0 * 100);

            sixelBuilder.Append(SixelColorStart)
                        .Append(colorIndex)
                        .Append(";2;")
                        .Append(r)
                        .Append(';')
                        .Append(g)
                        .Append(';')
                        .Append(b);
        }

        private static void AppendRepeatEntry(this StringBuilder sixelBuilder,
                                              int color,
                                              int repeatCounter,
                                              char e)
        {
            sixelBuilder.Append(SixelColorStart)
                        .Append(color)
                        .Append(SixelRepeat)
                        .Append(repeatCounter)
                        .Append(color != 0 ? e : SixelEmpty);
        }

        private static void AppendSixelEntry(this StringBuilder sixelBuilder, int color, char e)
        {
            sixelBuilder.Append(SixelColorStart)
                        .Append(color)
                        .Append(color != 0 ? e : SixelEmpty);
        }

        private static void StartSixel(this StringBuilder sixelBuilder, int width, int height)
        {
            sixelBuilder.Append(SixelStart)
                        .Append(SixelRasterAttributes)
                        .Append(width)
                        .Append(';')
                        .Append(height)
                        .Append(SixelTransparentColor);
        }
    }
}
