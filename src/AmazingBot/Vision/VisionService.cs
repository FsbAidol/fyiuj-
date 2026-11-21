using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using AmazingBot.Core;

namespace AmazingBot.Vision
{
    /// <summary>
    /// Высокоуровневое зрение бота: кадр окна игры, кэш шаблонов, поиск и ожидание.
    /// Каждое обращение Find/Exists делает свежий снимок окна —
    /// так бот всегда реагирует на актуальное состояние экрана.
    /// </summary>
    public sealed class VisionService
    {
        private readonly string _windowTitlePart;
        private readonly string _templatesDir;
        private readonly Logger _log;
        private readonly Dictionary<string, BitmapImage> _templateCache = new Dictionary<string, BitmapImage>(StringComparer.OrdinalIgnoreCase);
        private IntPtr _hwnd = IntPtr.Zero;
        private CapturedFrame? _current;

        public VisionService(string windowTitlePart, string templatesDir, Logger log)
        {
            _windowTitlePart = windowTitlePart;
            _templatesDir = templatesDir;
            _log = log;
        }

        public IntPtr WindowHandle => _hwnd;
        public int ClientOriginX => _current?.OriginX ?? 0;
        public int ClientOriginY => _current?.OriginY ?? 0;
        public int ClientWidth => _current?.Width ?? 0;
        public int ClientHeight => _current?.Height ?? 0;

        public bool EnsureWindow()
        {
            if (_hwnd != IntPtr.Zero && Native.NativeMethods.IsWindow(_hwnd))
                return true;
            _hwnd = ScreenCapture.FindWindowByTitle(_windowTitlePart);
            if (_hwnd == IntPtr.Zero)
            {
                _log.Warn("Окно с заголовком, содержащим «" + _windowTitlePart + "», не найдено. Игра запущена в оконном режиме?");
                return false;
            }
            _log.Info("Окно игры найдено (hwnd=" + _hwnd + ")");
            return true;
        }

        /// <summary>Сделать свежий снимок окна. false — окна нет.</summary>
        public bool Refresh()
        {
            if (!EnsureWindow())
                return false;
            try
            {
                _current = ScreenCapture.CaptureClient(_hwnd);
                return true;
            }
            catch (Exception ex)
            {
                _log.Exception("Ошибка захвата экрана", ex);
                _hwnd = IntPtr.Zero; // окно могло пересоздаться — найдём заново
                return false;
            }
        }

        public CapturedFrame? Current => _current;

        public string TemplatePath(string templateName)
        {
            string name = templateName;
            if (!name.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase))
                name += ".bmp";
            return Path.Combine(_templatesDir, name);
        }

        public bool TemplateExists(string templateName)
        {
            return File.Exists(TemplatePath(templateName));
        }

        public BitmapImage LoadTemplate(string templateName)
        {
            if (_templateCache.TryGetValue(templateName, out BitmapImage? cached))
                return cached;
            string path = TemplatePath(templateName);
            if (!File.Exists(path))
                throw new FileNotFoundException("Шаблон не найден: " + path + " (см. templates/README.md)");
            BitmapImage img = BmpCodec.Load(path);
            _templateCache[templateName] = img;
            return img;
        }

        public void ClearTemplateCache()
        {
            _templateCache.Clear();
        }

        /// <summary>Найти лучшее совпадение шаблона на свежем кадре.</summary>
        public Match? Find(string templateName, double threshold)
        {
            if (!Refresh())
                return null;
            try
            {
                BitmapImage template = LoadTemplate(templateName);
                return TemplateMatcher.FindBest(_current!.Image, template, threshold);
            }
            catch (FileNotFoundException ex)
            {
                _log.Warn(ex.Message);
                return null;
            }
        }

        public bool Exists(string templateName, double threshold)
        {
            return Find(templateName, threshold) != null;
        }

        /// <summary>Ждать появления шаблона на экране до timeoutSeconds секунд.</summary>
        public Match? WaitFor(string templateName, double timeoutSeconds, double threshold, Func<bool>? shouldStop = null, double pollMs = 150)
        {
            DateTime deadline = DateTime.Now.AddSeconds(timeoutSeconds);
            while (DateTime.Now < deadline)
            {
                if (shouldStop != null && shouldStop())
                    return null;
                Match? m = Find(templateName, threshold);
                if (m != null)
                    return m;
                Thread.Sleep((int)pollMs);
            }
            return null;
        }

        /// <summary>Ждать исчезновения шаблона (например, закрытия меню).</summary>
        public bool WaitForGone(string templateName, double timeoutSeconds, double threshold, Func<bool>? shouldStop = null, double pollMs = 150)
        {
            DateTime deadline = DateTime.Now.AddSeconds(timeoutSeconds);
            while (DateTime.Now < deadline)
            {
                if (shouldStop != null && shouldStop())
                    return false;
                if (!Exists(templateName, threshold))
                    return true;
                Thread.Sleep((int)pollMs);
            }
            return false;
        }
    }
}
