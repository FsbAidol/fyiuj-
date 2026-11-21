using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AmazingBot.Core;
using AmazingBot.Input;
using AmazingBot.Native;
using AmazingBot.Vision;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Security.Cryptography;

namespace AmazingBot.Tasks
{
    /// <summary>
    /// Watches the lower HUD strip for a single visible QTE key label and presses it.
    /// Frames stay in memory; no screenshot files are written. Ambiguous OCR results are ignored.
    /// </summary>
    public sealed class KeyPromptTask
    {
        private static readonly Regex KeyToken = new Regex(
            @"(?<![A-Z0-9])(SPACE|ALT|SHIFT|ENTER|RETURN|UP|DOWN|LEFT|RIGHT|F|C|H|Y|N)(?![A-Z0-9])",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private readonly VisionService _vision;
        private readonly Logger _log;
        private readonly OcrEngine? _ocr;
        private readonly Func<bool> _shouldStop;
        private long _nextScanAt;
        private int _scanInProgress;
        private string? _lastPressedKey;
        private int _emptyFrames;

        public KeyPromptTask(string windowTitle, string templatesDir, Logger log, Func<bool> shouldStop, bool enabled)
        {
            _vision = new VisionService(windowTitle, templatesDir, log);
            _log = log;
            _shouldStop = shouldStop;
            if (!enabled) return;
            try
            {
                _ocr = OcrEngine.TryCreateFromUserProfileLanguages();
                if (_ocr == null)
                    _log.Warn("Автонажатие мини-игры отключено: в Windows не найден установленный OCR-язык.");
            }
            catch (Exception ex)
            {
                _log.Warn("Не удалось запустить Windows OCR для мини-игры: " + ex.Message);
            }
        }

        /// <summary>Called by the profession runner; OCR itself runs asynchronously and is rate limited.</summary>
        public void CheckIfDue()
        {
            if (_ocr == null || _shouldStop()) return;
            long now = Environment.TickCount64;
            if (now < Interlocked.Read(ref _nextScanAt)) return;
            Interlocked.Exchange(ref _nextScanAt, now + 250);
            if (Interlocked.CompareExchange(ref _scanInProgress, 1, 0) != 0) return;
            _ = Task.Run(ScanAndPressAsync);
        }

        private async Task ScanAndPressAsync()
        {
            try
            {
                if (_shouldStop() || !_vision.Refresh())
                {
                    MarkNoPrompt();
                    return;
                }

                CapturedFrame? frame = _vision.Current;
                if (frame == null || frame.Height < 80) return;

                // QTE prompts are reported in the lower HUD strip. Restrict OCR to that area
                // and require a yellow signal there, so ordinary HUD labels do not trigger input.
                int top = frame.Height * 68 / 100;
                BitmapImage strip = frame.Image.Crop(0, top, frame.Width, frame.Height - top);
                if (!ContainsYellowSignal(strip))
                {
                    MarkNoPrompt();
                    return;
                }

                string text = await RecognizeAsync(strip);
                if (_shouldStop()) return;
                string? key = ParseSingleKey(text);
                if (key == null)
                {
                    MarkNoPrompt();
                    return;
                }

                _emptyFrames = 0;
                if (string.Equals(key, _lastPressedKey, StringComparison.OrdinalIgnoreCase))
                    return; // keep one press per prompt; wait until the prompt changes or clears

                Keyboard.Press(key);
                _lastPressedKey = key;
                _log.Info("Автонажатие подсказки мини-игры: " + key);
            }
            catch (Exception ex)
            {
                _log.Exception("Ошибка распознавания подсказки мини-игры", ex);
            }
            finally
            {
                Interlocked.Exchange(ref _scanInProgress, 0);
            }
        }

        private async Task<string> RecognizeAsync(BitmapImage image)
        {
            OcrEngine engine = _ocr!;
            byte[] pixels = (byte[])image.Pixels.Clone();
            using (SoftwareBitmap bitmap = SoftwareBitmap.CreateCopyFromBuffer(
                CryptographicBuffer.CreateFromByteArray(pixels),
                BitmapPixelFormat.Bgra8,
                image.Width,
                image.Height,
                BitmapAlphaMode.Ignore))
            {
                OcrResult result = await engine.RecognizeAsync(bitmap);
                return result.Text ?? string.Empty;
            }
        }

        private static bool ContainsYellowSignal(BitmapImage image)
        {
            int count = 0;
            int total = image.Width * image.Height;
            byte[] pixels = image.Pixels;
            // Sample every second pixel. Yellow/gold HUD backgrounds normally have high R/G and low B.
            for (int y = 0; y < image.Height; y += 2)
            {
                int row = y * image.Width * 4;
                for (int x = 0; x < image.Width; x += 2)
                {
                    int i = row + x * 4;
                    int b = pixels[i];
                    int g = pixels[i + 1];
                    int r = pixels[i + 2];
                    if (r >= 155 && g >= 105 && b <= 150 && r > b + 35 && g > b + 15)
                        count++;
                }
            }
            return count >= Math.Max(24, total / 5000);
        }

        private static string? ParseSingleKey(string recognizedText)
        {
            string text = (recognizedText ?? string.Empty).ToUpperInvariant()
                .Replace("LEFT ALT", " ALT ")
                .Replace("LEFT SHIFT", " SHIFT ")
                .Replace("←", " LEFT ")
                .Replace("↑", " UP ")
                .Replace("→", " RIGHT ")
                .Replace("↓", " DOWN ");

            MatchCollection matches = KeyToken.Matches(text);
            if (matches.Count != 1) return null;
            string token = matches[0].Value;
            switch (token)
            {
                case "SPACE": return "Space";
                case "ALT": return "Alt";
                case "SHIFT": return "Shift";
                case "ENTER":
                case "RETURN": return "Enter";
                case "LEFT": return "Left";
                case "RIGHT": return "Right";
                case "UP": return "Up";
                case "DOWN": return "Down";
                default: return token; // F, C, H, Y, N
            }
        }

        private void MarkNoPrompt()
        {
            _emptyFrames++;
            if (_emptyFrames >= 2)
                _lastPressedKey = null;
        }
    }
}
