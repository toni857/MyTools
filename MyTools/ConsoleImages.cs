using System.Diagnostics;
using System.Text;
using StbImageSharp;

namespace My;

public static class ConsoleImages
{
    public sealed record CameraVideoSource(
        string CameraPath,
        int CameraWidth,
        int CameraHeight,
        double Fps,
        double? DurationSeconds);

    public static string GreifeAufKameraZuUmFotoZuMachen(
        string cameraPath = "/dev/video0",
        int cameraWidth = 640,
        int cameraHeight = 480,
        string? outputPath = null)
    {
        ValidateCameraInput(cameraPath, cameraWidth, cameraHeight);

        if (!IsCommandAvailable("ffmpeg"))
        {
            throw new InvalidOperationException("Fuer Kamera-Fotos wird ffmpeg im Systempfad benoetigt.");
        }

        string finalOutputPath = outputPath ?? Path.Combine(Path.GetTempPath(), $"camera-photo-{Guid.NewGuid():N}.jpg");
        using Process process = StartCameraPhotoProcess(cameraPath, cameraWidth, cameraHeight, finalOutputPath);
        process.WaitForExit();

        if (process.ExitCode != 0 || !File.Exists(finalOutputPath))
        {
            throw new InvalidOperationException($"Das Kamera-Foto konnte nicht aufgenommen werden. ffmpeg Exit-Code: {process.ExitCode}.");
        }

        return finalOutputPath;
    }

    public static CameraVideoSource GreifeAufKameraZuUmVideoZuMachen(
        string cameraPath = "/dev/video0",
        int cameraWidth = 640,
        int cameraHeight = 480,
        double fps = 12,
        double? durationSeconds = null)
    {
        ValidateCameraInput(cameraPath, cameraWidth, cameraHeight);

        if (fps <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(fps), "Die FPS muessen groesser als 0 sein.");
        }

        if (durationSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(durationSeconds), "Der Timer muss groesser als 0 sein.");
        }

        return new CameraVideoSource(cameraPath, cameraWidth, cameraHeight, fps, durationSeconds);
    }

    public static void ShowPhoto(
        string filePath,
        int width = 120,
        int? height = null,
        int quality = 2)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("Der Dateipfad darf nicht leer sein.", nameof(filePath));
        }

        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Die Breite muss größer als 0 sein.");
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), "Die Höhe muss größer als 0 sein.");
        }

        RgbImage image = LoadImage(filePath);
        Console.Write(RenderImage(image, width, height, quality));
    }

    public static void ShowVideo(
        CameraVideoSource camera,
        int width = 120,
        int? height = null,
        int quality = 2,
        double? durationSeconds = null)
    {
        ArgumentNullException.ThrowIfNull(camera);
        ValidateDisplayInput(width, height, quality);

        if (durationSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(durationSeconds), "Der Timer muss groesser als 0 sein.");
        }

        if (!IsCommandAvailable("ffmpeg"))
        {
            throw new InvalidOperationException("Fuer Kamera-Video wird ffmpeg im Systempfad benoetigt.");
        }

        VideoInfo videoInfo = new(camera.CameraWidth, camera.CameraHeight);
        double effectiveDurationSeconds = durationSeconds ?? camera.DurationSeconds ?? 0;
        int? maxFrames = effectiveDurationSeconds > 0
            ? (int)global::System.Math.Ceiling(effectiveDurationSeconds * camera.Fps)
            : null;

        using Process process = StartCameraVideoProcess(camera);
        RenderVideoStream(process, videoInfo, width, height, quality, camera.Fps, maxFrames);
    }

    public static void ShowVideo(
        string filePath,
        int width = 120,
        int? height = null,
        int quality = 2,
        double fps = 12,
        int? maxFrames = null,
        bool playAudio = true)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("Der Dateipfad darf nicht leer sein.", nameof(filePath));
        }

        ValidateDisplayInput(width, height, quality);

        if (fps <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(fps), "Die FPS müssen größer als 0 sein.");
        }

        if (maxFrames <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxFrames), "Die maximale Framezahl muss größer als 0 sein.");
        }

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("Die Videodatei wurde nicht gefunden.", filePath);
        }

        if (!IsCommandAvailable("ffmpeg") || !IsCommandAvailable("ffprobe"))
        {
            throw new InvalidOperationException("Für ShowVideo werden ffmpeg und ffprobe im Systempfad benötigt.");
        }

        if (playAudio && !IsCommandAvailable("ffplay"))
        {
            throw new InvalidOperationException("Für ShowVideo mit Ton wird zusätzlich ffplay im Systempfad benötigt.");
        }

        VideoInfo videoInfo = ReadVideoInfo(filePath);
        using Process? audioProcess = playAudio ? StartAudioPlaybackProcess(filePath) : null;
        using Process process = StartVideoDecodeProcess(filePath, fps);
        RenderVideoStream(process, videoInfo, width, height, quality, fps, maxFrames);

        if (audioProcess is not null)
        {
            if (!audioProcess.HasExited)
            {
                audioProcess.Kill(entireProcessTree: true);
            }

            audioProcess.WaitForExit();
        }
    }

    private static void RenderVideoStream(
        Process process,
        VideoInfo videoInfo,
        int width,
        int? height,
        int quality,
        double fps,
        int? maxFrames)
    {
        ConsoleImageQuality qualityMode = GetQualityMode(quality);
        (int outputWidth, int outputPixelHeight) = ResolveVideoOutputDimensions(
            videoInfo.Width,
            videoInfo.Height,
            width,
            height,
            qualityMode);
        using Stream stream = process.StandardOutput.BaseStream;
        int frameSize = checked(videoInfo.Width * videoInfo.Height * 3);
        byte[] frameBuffer = new byte[frameSize];
        Stopwatch stopwatch = Stopwatch.StartNew();
        int frameIndex = 0;
        double frameDurationMs = 1000d / fps;
        ResetCursorPosition();

        try
        {
            while (!maxFrames.HasValue || frameIndex < maxFrames.Value)
            {
                if (!TryReadExact(stream, frameBuffer, frameSize))
                {
                    break;
                }

                RgbImage frame = new(videoInfo.Width, videoInfo.Height, frameBuffer);
                string renderedFrame = RenderImage(frame, outputWidth, outputPixelHeight, quality);

                ResetCursorPosition();
                Console.Write(renderedFrame);

                frameIndex++;
                double targetElapsedMs = frameIndex * frameDurationMs;
                double remainingMs = targetElapsedMs - stopwatch.Elapsed.TotalMilliseconds;
                if (remainingMs > 1)
                {
                    Thread.Sleep((int)remainingMs);
                }
            }
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }

        process.WaitForExit();

        if (process.ExitCode != 0 && process.ExitCode != 255)
        {
            throw new InvalidOperationException($"ffmpeg wurde mit Exit-Code {process.ExitCode} beendet.");
        }
    }

    private static string RenderImage(RgbImage image, int width, int? height, int quality)
    {
        int outputWidth = width;
        ConsoleImageQuality qualityMode = GetQualityMode(quality);

        int outputPixelHeight = height ?? GetAutoOutputPixelHeight(image, outputWidth, qualityMode);
        StringBuilder builder = new();

        switch (qualityMode)
        {
            case ConsoleImageQuality.HalfBlocks:
                RenderHalfBlocks(image, outputWidth, outputPixelHeight, builder);
                break;
            case ConsoleImageQuality.QuarterBlocks:
                RenderQuarterBlocks(image, outputWidth, outputPixelHeight, builder);
                break;
            case ConsoleImageQuality.Braille:
                RenderBraille(image, outputWidth, outputPixelHeight, builder);
                break;
        }

        return builder.ToString();
    }

    private static ConsoleImageQuality GetQualityMode(int quality)
    {
        return quality switch
        {
            1 => ConsoleImageQuality.HalfBlocks,
            2 => ConsoleImageQuality.QuarterBlocks,
            3 => ConsoleImageQuality.Braille,
            _ => throw new ArgumentOutOfRangeException(nameof(quality), "Erlaubte Werte sind aktuell 1, 2 oder 3.")
        };
    }

    private static int GetAutoOutputPixelHeight(RgbImage image, int outputWidth, ConsoleImageQuality quality)
    {
        return GetAutoOutputPixelHeight(image.Width, image.Height, outputWidth, quality);
    }

    private static int GetAutoOutputPixelHeight(int sourceWidth, int sourceHeight, int outputWidth, ConsoleImageQuality quality)
    {
        int logicalPixelWidth = quality switch
        {
            ConsoleImageQuality.HalfBlocks => outputWidth,
            ConsoleImageQuality.QuarterBlocks => outputWidth,
            ConsoleImageQuality.Braille => outputWidth * 2,
            _ => throw new ArgumentOutOfRangeException(nameof(quality))
        };

        return global::System.Math.Max(
            1,
            (int)global::System.Math.Round(sourceHeight * (logicalPixelWidth / (double)sourceWidth)));
    }

    private static (int OutputWidth, int OutputPixelHeight) ResolveVideoOutputDimensions(
        int sourceWidth,
        int sourceHeight,
        int requestedWidth,
        int? requestedHeight,
        ConsoleImageQuality quality)
    {
        int outputWidth = requestedWidth;
        int outputPixelHeight = requestedHeight ?? GetAutoOutputPixelHeight(sourceWidth, sourceHeight, outputWidth, quality);

        try
        {
            int maxWidth = GetSafeConsoleWidth();
            if (outputWidth > maxWidth)
            {
                outputWidth = maxWidth;
                if (!requestedHeight.HasValue)
                {
                    outputPixelHeight = GetAutoOutputPixelHeight(sourceWidth, sourceHeight, outputWidth, quality);
                }
            }

            int maxOutputRows = GetSafeConsoleRowCount();
            int maxOutputPixelHeight = GetMaxOutputPixelHeight(maxOutputRows, quality);

            if (outputPixelHeight > maxOutputPixelHeight)
            {
                if (requestedHeight.HasValue)
                {
                    outputPixelHeight = maxOutputPixelHeight;
                }
                else
                {
                    double scale = maxOutputPixelHeight / (double)outputPixelHeight;
                    outputWidth = global::System.Math.Max(1, (int)global::System.Math.Floor(outputWidth * scale));
                    outputPixelHeight = GetAutoOutputPixelHeight(sourceWidth, sourceHeight, outputWidth, quality);
                }
            }
        }
        catch (IOException)
        {
        }
        catch (InvalidOperationException)
        {
        }
        catch (ArgumentOutOfRangeException)
        {
        }

        return (outputWidth, outputPixelHeight);
    }

    private static int GetSafeConsoleWidth()
    {
        int windowWidth = global::System.Math.Max(1, Console.WindowWidth);
        int bufferWidth = global::System.Math.Max(1, Console.BufferWidth);
        int maxWidth = global::System.Math.Min(windowWidth, bufferWidth);

        return global::System.Math.Max(1, maxWidth - 1);
    }

    private static int GetSafeConsoleRowCount()
    {
        int windowHeight = global::System.Math.Max(1, Console.WindowHeight);
        int bufferHeight = global::System.Math.Max(1, Console.BufferHeight);
        int maxRows = global::System.Math.Min(windowHeight, bufferHeight);

        return global::System.Math.Max(1, maxRows - 1);
    }

    private static void RenderHalfBlocks(RgbImage image, int outputWidth, int outputPixelHeight, StringBuilder builder)
    {
        int outputRowCount = (outputPixelHeight + 1) / 2;

        for (int row = 0; row < outputRowCount; row++)
        {
            for (int x = 0; x < outputWidth; x++)
            {
                Rgb topPixel = image.SampleAverage(x, row * 2, outputWidth, outputPixelHeight);
                Rgb bottomPixel = image.SampleAverage(
                    x,
                    global::System.Math.Min((row * 2) + 1, outputPixelHeight - 1),
                    outputWidth,
                    outputPixelHeight);

                AppendColoredCharacter(builder, '▀', topPixel, bottomPixel);
            }

            AppendRowTerminator(builder, row, outputRowCount);
        }
    }

    private static void RenderQuarterBlocks(RgbImage image, int outputWidth, int outputPixelHeight, StringBuilder builder)
    {
        int logicalPixelWidth = outputWidth * 2;
        int outputRowCount = (outputPixelHeight + 1) / 2;

        for (int row = 0; row < outputRowCount; row++)
        {
            for (int x = 0; x < outputWidth; x++)
            {
                Rgb topLeft = image.SampleAverage(x * 2, row * 2, logicalPixelWidth, outputPixelHeight);
                Rgb topRight = image.SampleAverage(
                    global::System.Math.Min((x * 2) + 1, logicalPixelWidth - 1),
                    row * 2,
                    logicalPixelWidth,
                    outputPixelHeight);
                Rgb bottomLeft = image.SampleAverage(
                    x * 2,
                    global::System.Math.Min((row * 2) + 1, outputPixelHeight - 1),
                    logicalPixelWidth,
                    outputPixelHeight);
                Rgb bottomRight = image.SampleAverage(
                    global::System.Math.Min((x * 2) + 1, logicalPixelWidth - 1),
                    global::System.Math.Min((row * 2) + 1, outputPixelHeight - 1),
                    logicalPixelWidth,
                    outputPixelHeight);

                QuadrantCell cell = BuildQuadrantCell(topLeft, topRight, bottomLeft, bottomRight);
                AppendColoredCharacter(builder, cell.Character, cell.Foreground, cell.Background);
            }

            AppendRowTerminator(builder, row, outputRowCount);
        }
    }

    private static void RenderBraille(RgbImage image, int outputWidth, int outputPixelHeight, StringBuilder builder)
    {
        int logicalPixelWidth = outputWidth * 2;
        int outputRowCount = (outputPixelHeight + 3) / 4;

        for (int row = 0; row < outputRowCount; row++)
        {
            for (int x = 0; x < outputWidth; x++)
            {
                Rgb[] pixels =
                [
                    image.SampleAverage(x * 2, row * 4, logicalPixelWidth, outputPixelHeight),
                    image.SampleAverage(x * 2, global::System.Math.Min((row * 4) + 1, outputPixelHeight - 1), logicalPixelWidth, outputPixelHeight),
                    image.SampleAverage(x * 2, global::System.Math.Min((row * 4) + 2, outputPixelHeight - 1), logicalPixelWidth, outputPixelHeight),
                    image.SampleAverage(global::System.Math.Min((x * 2) + 1, logicalPixelWidth - 1), row * 4, logicalPixelWidth, outputPixelHeight),
                    image.SampleAverage(global::System.Math.Min((x * 2) + 1, logicalPixelWidth - 1), global::System.Math.Min((row * 4) + 1, outputPixelHeight - 1), logicalPixelWidth, outputPixelHeight),
                    image.SampleAverage(global::System.Math.Min((x * 2) + 1, logicalPixelWidth - 1), global::System.Math.Min((row * 4) + 2, outputPixelHeight - 1), logicalPixelWidth, outputPixelHeight),
                    image.SampleAverage(x * 2, global::System.Math.Min((row * 4) + 3, outputPixelHeight - 1), logicalPixelWidth, outputPixelHeight),
                    image.SampleAverage(global::System.Math.Min((x * 2) + 1, logicalPixelWidth - 1), global::System.Math.Min((row * 4) + 3, outputPixelHeight - 1), logicalPixelWidth, outputPixelHeight)
                ];

                BrailleCell cell = BuildBrailleCell(pixels);
                AppendColoredCharacter(builder, cell.Character, cell.Foreground, cell.Background);
            }

            AppendRowTerminator(builder, row, outputRowCount);
        }
    }

    private static void AppendRowTerminator(StringBuilder builder, int row, int outputRowCount)
    {
        builder.Append("\x1b[0m");
        if (row < outputRowCount - 1)
        {
            builder.AppendLine();
        }
    }

    private static int GetOutputRowCount(int outputPixelHeight, ConsoleImageQuality quality)
    {
        return quality switch
        {
            ConsoleImageQuality.HalfBlocks => (outputPixelHeight + 1) / 2,
            ConsoleImageQuality.QuarterBlocks => (outputPixelHeight + 1) / 2,
            ConsoleImageQuality.Braille => (outputPixelHeight + 3) / 4,
            _ => throw new ArgumentOutOfRangeException(nameof(quality))
        };
    }

    private static int GetMaxOutputPixelHeight(int outputRows, ConsoleImageQuality quality)
    {
        return quality switch
        {
            ConsoleImageQuality.HalfBlocks => outputRows * 2,
            ConsoleImageQuality.QuarterBlocks => outputRows * 2,
            ConsoleImageQuality.Braille => outputRows * 4,
            _ => throw new ArgumentOutOfRangeException(nameof(quality))
        };
    }

    private static void ResetCursorPosition()
    {
        try
        {
            if (!Console.IsOutputRedirected)
            {
                Console.SetCursorPosition(0, 0);
                return;
            }
        }
        catch (IOException)
        {
        }
        catch (ArgumentOutOfRangeException)
        {
        }

        Console.Write("\x1b[H");
    }

    private static QuadrantCell BuildQuadrantCell(Rgb topLeft, Rgb topRight, Rgb bottomLeft, Rgb bottomRight)
    {
        Span<Rgb> pixels = [topLeft, topRight, bottomLeft, bottomRight];
        int secondIndex = 1;
        int largestDistance = ColorDistanceSquared(pixels[0], pixels[1]);

        for (int i = 2; i < pixels.Length; i++)
        {
            int distance = ColorDistanceSquared(pixels[0], pixels[i]);
            if (distance > largestDistance)
            {
                largestDistance = distance;
                secondIndex = i;
            }
        }

        Rgb foregroundSeed = pixels[0];
        Rgb backgroundSeed = pixels[secondIndex];
        int mask = 0;
        int foregroundCount = 0;
        int backgroundCount = 0;
        int foregroundRed = 0;
        int foregroundGreen = 0;
        int foregroundBlue = 0;
        int backgroundRed = 0;
        int backgroundGreen = 0;
        int backgroundBlue = 0;

        for (int i = 0; i < pixels.Length; i++)
        {
            Rgb pixel = pixels[i];
            bool useForeground = ColorDistanceSquared(pixel, foregroundSeed) <= ColorDistanceSquared(pixel, backgroundSeed);

            if (useForeground)
            {
                mask |= 1 << i;
                foregroundCount++;
                foregroundRed += pixel.R;
                foregroundGreen += pixel.G;
                foregroundBlue += pixel.B;
            }
            else
            {
                backgroundCount++;
                backgroundRed += pixel.R;
                backgroundGreen += pixel.G;
                backgroundBlue += pixel.B;
            }
        }

        if (foregroundCount == 0 || backgroundCount == 0)
        {
            Rgb average = AverageColor(pixels);
            return new QuadrantCell('█', average, average);
        }

        Rgb foreground = new(
            (byte)(foregroundRed / foregroundCount),
            (byte)(foregroundGreen / foregroundCount),
            (byte)(foregroundBlue / foregroundCount));
        Rgb background = new(
            (byte)(backgroundRed / backgroundCount),
            (byte)(backgroundGreen / backgroundCount),
            (byte)(backgroundBlue / backgroundCount));

        return new QuadrantCell(GetQuadrantCharacter(mask), foreground, background);
    }

    private static BrailleCell BuildBrailleCell(Rgb[] pixels)
    {
        int darkestValue = GetLuminanceValue(pixels[0]);
        int brightestValue = darkestValue;
        int totalLuminance = darkestValue;

        for (int i = 1; i < pixels.Length; i++)
        {
            int luminance = GetLuminanceValue(pixels[i]);
            totalLuminance += luminance;

            if (luminance < darkestValue)
            {
                darkestValue = luminance;
            }

            if (luminance > brightestValue)
            {
                brightestValue = luminance;
            }
        }

        int contrast = brightestValue - darkestValue;
        if (contrast < 12_000)
        {
            Rgb average = AverageColor(pixels);
            return new BrailleCell(' ', average, average);
        }

        int threshold = totalLuminance / pixels.Length;
        int mask = 0;
        int foregroundCount = 0;
        int backgroundCount = 0;
        int foregroundRed = 0;
        int foregroundGreen = 0;
        int foregroundBlue = 0;
        int backgroundRed = 0;
        int backgroundGreen = 0;
        int backgroundBlue = 0;

        for (int i = 0; i < pixels.Length; i++)
        {
            Rgb pixel = pixels[i];
            bool useForeground = GetLuminanceValue(pixel) <= threshold;

            if (useForeground)
            {
                mask |= GetBrailleBit(i);
                foregroundCount++;
                foregroundRed += pixel.R;
                foregroundGreen += pixel.G;
                foregroundBlue += pixel.B;
            }
            else
            {
                backgroundCount++;
                backgroundRed += pixel.R;
                backgroundGreen += pixel.G;
                backgroundBlue += pixel.B;
            }
        }

        if (foregroundCount == 0)
        {
            Rgb average = AverageColor(pixels);
            return new BrailleCell(' ', average, average);
        }

        if (backgroundCount == 0)
        {
            Rgb average = AverageColor(pixels);
            return new BrailleCell((char)(0x2800 + 0xFF), average, average);
        }

        Rgb foreground = new(
            (byte)(foregroundRed / foregroundCount),
            (byte)(foregroundGreen / foregroundCount),
            (byte)(foregroundBlue / foregroundCount));
        Rgb background = new(
            (byte)(backgroundRed / backgroundCount),
            (byte)(backgroundGreen / backgroundCount),
            (byte)(backgroundBlue / backgroundCount));

        char character = (char)(0x2800 + mask);
        return new BrailleCell(character, foreground, background);
    }

    private static void AppendColoredCharacter(StringBuilder builder, char character, Rgb foreground, Rgb background)
    {
        builder
            .Append("\x1b[38;2;")
            .Append(foreground.R)
            .Append(';')
            .Append(foreground.G)
            .Append(';')
            .Append(foreground.B)
            .Append("m\x1b[48;2;")
            .Append(background.R)
            .Append(';')
            .Append(background.G)
            .Append(';')
            .Append(background.B)
            .Append('m')
            .Append(character);
    }

    private static Rgb AverageColor(ReadOnlySpan<Rgb> pixels)
    {
        int red = 0;
        int green = 0;
        int blue = 0;

        foreach (Rgb pixel in pixels)
        {
            red += pixel.R;
            green += pixel.G;
            blue += pixel.B;
        }

        return new Rgb(
            (byte)(red / pixels.Length),
            (byte)(green / pixels.Length),
            (byte)(blue / pixels.Length));
    }

    private static int ColorDistanceSquared(Rgb left, Rgb right)
    {
        int red = left.R - right.R;
        int green = left.G - right.G;
        int blue = left.B - right.B;
        return (red * red) + (green * green) + (blue * blue);
    }

    private static int GetLuminanceValue(Rgb pixel)
    {
        return (pixel.R * 299) + (pixel.G * 587) + (pixel.B * 114);
    }

    private static int GetBrailleBit(int index) => index switch
    {
        0 => 1 << 0,
        1 => 1 << 1,
        2 => 1 << 2,
        3 => 1 << 3,
        4 => 1 << 4,
        5 => 1 << 5,
        6 => 1 << 6,
        7 => 1 << 7,
        _ => throw new ArgumentOutOfRangeException(nameof(index))
    };

    private static char GetQuadrantCharacter(int mask) => mask switch
    {
        0 => ' ',
        1 => '▘',
        2 => '▝',
        3 => '▀',
        4 => '▖',
        5 => '▌',
        6 => '▞',
        7 => '▛',
        8 => '▗',
        9 => '▚',
        10 => '▐',
        11 => '▜',
        12 => '▄',
        13 => '▙',
        14 => '▟',
        15 => '█',
        _ => throw new ArgumentOutOfRangeException(nameof(mask), "Ungueltige Quadrantenmaske.")
    };

    private static RgbImage LoadImage(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("Die Bilddatei wurde nicht gefunden.", filePath);
        }

        using FileStream stream = File.OpenRead(filePath);
        ImageResult image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlue);

        if (image.Width <= 0 || image.Height <= 0)
        {
            throw new InvalidDataException("Das Bild hat eine ungültige Größe.");
        }

        return new RgbImage(image.Width, image.Height, image.Data);
    }

    private static void ValidateCameraInput(string cameraPath, int cameraWidth, int cameraHeight)
    {
        if (string.IsNullOrWhiteSpace(cameraPath))
        {
            throw new ArgumentException("Der Kamerapfad darf nicht leer sein.", nameof(cameraPath));
        }

        if (cameraWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(cameraWidth), "Die Kamerabreite muss groesser als 0 sein.");
        }

        if (cameraHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(cameraHeight), "Die Kamerahoehe muss groesser als 0 sein.");
        }
    }

    private static void ValidateDisplayInput(int width, int? height, int quality)
    {
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Die Breite muss größer als 0 sein.");
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), "Die Höhe muss größer als 0 sein.");
        }

        GetQualityMode(quality);
    }

    private static Process StartCameraPhotoProcess(string cameraPath, int cameraWidth, int cameraHeight, string outputPath)
    {
        ProcessStartInfo startInfo = CreateFfmpegStartInfo();

        startInfo.ArgumentList.Add("-f");
        startInfo.ArgumentList.Add("v4l2");
        startInfo.ArgumentList.Add("-video_size");
        startInfo.ArgumentList.Add($"{cameraWidth}x{cameraHeight}");
        startInfo.ArgumentList.Add("-i");
        startInfo.ArgumentList.Add(cameraPath);
        startInfo.ArgumentList.Add("-frames:v");
        startInfo.ArgumentList.Add("1");
        startInfo.ArgumentList.Add("-y");
        startInfo.ArgumentList.Add(outputPath);

        Process process = new() { StartInfo = startInfo };
        process.Start();
        return process;
    }

    private static Process StartCameraVideoProcess(CameraVideoSource camera)
    {
        ProcessStartInfo startInfo = CreateFfmpegStartInfo();

        startInfo.ArgumentList.Add("-f");
        startInfo.ArgumentList.Add("v4l2");
        startInfo.ArgumentList.Add("-framerate");
        startInfo.ArgumentList.Add(camera.Fps.ToString(System.Globalization.CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("-video_size");
        startInfo.ArgumentList.Add($"{camera.CameraWidth}x{camera.CameraHeight}");
        startInfo.ArgumentList.Add("-i");
        startInfo.ArgumentList.Add(camera.CameraPath);
        startInfo.ArgumentList.Add("-f");
        startInfo.ArgumentList.Add("rawvideo");
        startInfo.ArgumentList.Add("-pix_fmt");
        startInfo.ArgumentList.Add("rgb24");
        startInfo.ArgumentList.Add("-");

        Process process = new() { StartInfo = startInfo };
        process.Start();
        return process;
    }

    private static Process StartVideoDecodeProcess(string filePath, double fps)
    {
        ProcessStartInfo startInfo = CreateFfmpegStartInfo();

        startInfo.ArgumentList.Add("-loglevel");
        startInfo.ArgumentList.Add("error");
        startInfo.ArgumentList.Add("-i");
        startInfo.ArgumentList.Add(filePath);
        startInfo.ArgumentList.Add("-vf");
        startInfo.ArgumentList.Add($"fps={fps.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
        startInfo.ArgumentList.Add("-f");
        startInfo.ArgumentList.Add("rawvideo");
        startInfo.ArgumentList.Add("-pix_fmt");
        startInfo.ArgumentList.Add("rgb24");
        startInfo.ArgumentList.Add("-");

        Process process = new() { StartInfo = startInfo };
        process.Start();
        return process;
    }

    private static ProcessStartInfo CreateFfmpegStartInfo()
    {
        return new ProcessStartInfo
        {
            FileName = "ffmpeg",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
    }

    private static Process StartAudioPlaybackProcess(string filePath)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = "ffplay",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        startInfo.ArgumentList.Add("-loglevel");
        startInfo.ArgumentList.Add("error");
        startInfo.ArgumentList.Add("-nodisp");
        startInfo.ArgumentList.Add("-autoexit");
        startInfo.ArgumentList.Add("-vn");
        startInfo.ArgumentList.Add(filePath);

        Process process = new() { StartInfo = startInfo };
        process.Start();
        return process;
    }

    private static VideoInfo ReadVideoInfo(string filePath)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = "ffprobe",
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        startInfo.ArgumentList.Add("-v");
        startInfo.ArgumentList.Add("error");
        startInfo.ArgumentList.Add("-select_streams");
        startInfo.ArgumentList.Add("v:0");
        startInfo.ArgumentList.Add("-show_entries");
        startInfo.ArgumentList.Add("stream=width,height");
        startInfo.ArgumentList.Add("-of");
        startInfo.ArgumentList.Add("csv=s=x:p=0");
        startInfo.ArgumentList.Add(filePath);

        using Process process = new() { StartInfo = startInfo };
        process.Start();
        string output = process.StandardOutput.ReadToEnd().Trim();
        process.WaitForExit();

        if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
        {
            throw new InvalidDataException("Die Videogroesse konnte nicht aus ffprobe gelesen werden.");
        }

        string[] parts = output.Split('x', StringSplitOptions.TrimEntries);
        if (parts.Length != 2)
        {
            throw new InvalidDataException("Die Videogroesse konnte nicht aus ffprobe gelesen werden.");
        }

        return new VideoInfo(int.Parse(parts[0]), int.Parse(parts[1]));
    }

    private static bool TryReadExact(Stream stream, byte[] buffer, int byteCount)
    {
        int totalRead = 0;

        while (totalRead < byteCount)
        {
            int read = stream.Read(buffer, totalRead, byteCount - totalRead);
            if (read == 0)
            {
                return false;
            }

            totalRead += read;
        }

        return true;
    }

    private static bool IsCommandAvailable(string commandName)
    {
        string pathVariable = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        string[] searchPaths = pathVariable.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);

        foreach (string searchPath in searchPaths)
        {
            string fullPath = Path.Combine(searchPath, commandName);
            if (File.Exists(fullPath))
            {
                return true;
            }
        }

        return false;
    }

    public enum ConsoleImageQuality
    {
        HalfBlocks = 1,
        QuarterBlocks = 2,
        Braille = 3
    }

    private readonly record struct Rgb(byte R, byte G, byte B);

    private readonly record struct QuadrantCell(char Character, Rgb Foreground, Rgb Background);

    private readonly record struct BrailleCell(char Character, Rgb Foreground, Rgb Background);

    private readonly record struct VideoInfo(int Width, int Height);

    private sealed class RgbImage(int width, int height, byte[] pixels)
    {
        public int Width { get; } = width;

        public int Height { get; } = height;

        public Rgb GetPixel(int x, int y)
        {
            int index = ((y * Width) + x) * 3;
            return new Rgb(pixels[index], pixels[index + 1], pixels[index + 2]);
        }

        public Rgb SampleAverage(int targetX, int targetY, int targetWidth, int targetHeight)
        {
            int sourceStartX = (int)global::System.Math.Floor(targetX * Width / (double)targetWidth);
            int sourceEndX = (int)global::System.Math.Ceiling((targetX + 1) * Width / (double)targetWidth);
            int sourceStartY = (int)global::System.Math.Floor(targetY * Height / (double)targetHeight);
            int sourceEndY = (int)global::System.Math.Ceiling((targetY + 1) * Height / (double)targetHeight);

            sourceStartX = global::System.Math.Clamp(sourceStartX, 0, Width - 1);
            sourceEndX = global::System.Math.Clamp(sourceEndX, sourceStartX + 1, Width);
            sourceStartY = global::System.Math.Clamp(sourceStartY, 0, Height - 1);
            sourceEndY = global::System.Math.Clamp(sourceEndY, sourceStartY + 1, Height);

            int red = 0;
            int green = 0;
            int blue = 0;
            int count = 0;

            for (int y = sourceStartY; y < sourceEndY; y++)
            {
                for (int x = sourceStartX; x < sourceEndX; x++)
                {
                    Rgb pixel = GetPixel(x, y);
                    red += pixel.R;
                    green += pixel.G;
                    blue += pixel.B;
                    count++;
                }
            }

            return new Rgb(
                (byte)(red / count),
                (byte)(green / count),
                (byte)(blue / count));
        }
    }
}
