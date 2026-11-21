using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using AmazingBot.Core;

namespace AmazingBot.Input
{
    /// <summary>
    /// Шаг маршрута. Координаты — относительно клиентской области окна игры.
    /// Типы шагов: move, click, rclick, key, wait.
    /// </summary>
    public sealed class RouteStep
    {
        [JsonPropertyName("delayMs")]
        public int DelayMs { get; set; }

        [JsonPropertyName("type")]
        public string Type { get; set; } = "wait";

        [JsonPropertyName("x")]
        public int X { get; set; }

        [JsonPropertyName("y")]
        public int Y { get; set; }

        [JsonPropertyName("key")]
        public string? Key { get; set; }

        [JsonPropertyName("durationMs")]
        public int DurationMs { get; set; }
    }

    public sealed class RouteFile
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("clientOriginAtRecord")]
        public int[] ClientOriginAtRecord { get; set; } = new[] { 0, 0 };

        [JsonPropertyName("steps")]
        public List<RouteStep> Steps { get; set; } = new List<RouteStep>();
    }

    /// <summary>Чтение/запись/проигрывание записанных маршрутов.</summary>
    public static class RoutePlayer
    {
        public static string RoutePath(string routesDir, string name)
        {
            string file = name.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? name : name + ".json";
            return Path.Combine(routesDir, file);
        }

        public static RouteFile? Load(string routesDir, string name, Logger log)
        {
            string path = RoutePath(routesDir, name);
            if (!File.Exists(path))
            {
                log.Warn("Маршрут не найден: " + path);
                return null;
            }
            try
            {
                string json = File.ReadAllText(path);
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                return JsonSerializer.Deserialize<RouteFile>(json, options);
            }
            catch (Exception ex)
            {
                log.Exception("Не удалось прочитать маршрут " + path, ex);
                return null;
            }
        }

        public static void Save(string routesDir, RouteFile route, Logger log)
        {
            Directory.CreateDirectory(routesDir);
            string path = RoutePath(routesDir, route.Name);
            string json = JsonSerializer.Serialize(route, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
            log.Info("Маршрут «" + route.Name + "» сохранён: " + path + " (" + route.Steps.Count + " шагов)");
        }

        /// <summary>
        /// Проиграть маршрут. originX/originY — текущий угол клиентской области окна игры.
        /// shouldStop — проверка паник-клавиши (F12) между шагами.
        /// </summary>
        public static bool Play(RouteFile route, int originX, int originY, int interStepDelayMs, Logger log, Func<bool>? shouldStop, Action? backgroundCheck = null)
        {
            log.Info("Играю маршрут «" + route.Name + "» (" + route.Steps.Count + " шагов)");
            foreach (RouteStep step in route.Steps)
            {
                backgroundCheck?.Invoke();
                if (shouldStop != null && shouldStop())
                {
                    log.Warn("Маршрут «" + route.Name + "» прерван по паник-клавише");
                    return false;
                }
                SleepInterruptible(step.DelayMs, shouldStop, backgroundCheck);
                if (shouldStop != null && shouldStop()) return false;

                int absX = originX + step.X;
                int absY = originY + step.Y;
                switch ((step.Type ?? "wait").ToLowerInvariant())
                {
                    case "move":
                        Mouse.MoveTo(absX, absY);
                        break;
                    case "click":
                        Mouse.Click(absX, absY);
                        break;
                    case "rclick":
                        Mouse.RightClick(absX, absY);
                        break;
                    case "key":
                        if (!string.IsNullOrEmpty(step.Key))
                        {
                            Keyboard.Down(step.Key);
                            SleepInterruptible(Math.Max(20, step.DurationMs), shouldStop, backgroundCheck);
                            Keyboard.Up(step.Key);
                        }
                        break;
                    case "wait":
                        break;
                    default:
                        log.Warn("Неизвестный тип шага маршрута: " + step.Type);
                        break;
                }
                SleepInterruptible(interStepDelayMs, shouldStop, backgroundCheck);
            }
            return true;
        }

        private static void SleepInterruptible(int ms, Func<bool>? shouldStop, Action? backgroundCheck)
        {
            int remaining = ms;
            while (remaining > 0)
            {
                if (shouldStop != null && shouldStop())
                    return;
                backgroundCheck?.Invoke();
                int slice = Math.Min(remaining, 40);
                Thread.Sleep(slice);
                remaining -= slice;
            }
        }
    }

    /// <summary>
    /// Запись маршрута низкоуровневыми хуками мыши и клавиатуры.
    /// Завершение записи — клавиша F10. Координаты пишутся относительно
    /// клиентской области окна игры (не перемещайте окно во время записи!).
    /// </summary>
    public sealed class RouteRecorder
    {
        private const uint LLKHF_INJECTED = 0x10;
        private readonly Logger _log;
        private Native.NativeMethods.LowLevelProc? _mouseHookProc;
        private Native.NativeMethods.LowLevelProc? _keyboardHookProc;
        private IntPtr _mouseHook = IntPtr.Zero;
        private IntPtr _keyboardHook = IntPtr.Zero;

        private volatile bool _stopRequested;
        private readonly StopwatchCompat _clock = new StopwatchCompat();
        private long _lastEventTicks;
        private readonly List<RouteStep> _steps = new List<RouteStep>();
        private readonly Dictionary<uint, long> _keyDownTicks = new Dictionary<uint, long>();
        private int _originX;
        private int _originY;
        private int _lastMoveX = int.MinValue;
        private int _lastMoveY = int.MinValue;
        private long _lastMoveTicks;

        public RouteRecorder(Logger log)
        {
            _log = log;
        }

        /// <summary>Запись до нажатия F10. originX/originY — текущий угол клиента окна.</summary>
        public RouteFile Record(string name, int originX, int originY)
        {
            _originX = originX;
            _originY = originY;
            _steps.Clear();
            _keyDownTicks.Clear();
            _stopRequested = false;
            _lastMoveX = int.MinValue;
            _lastMoveTicks = 0;
            _clock.Restart();
            _lastEventTicks = 0;

            _mouseHookProc = MouseHookCallback;
            _keyboardHookProc = KeyboardHookCallback;
            IntPtr module = Native.NativeMethods.GetModuleHandle(null!);
            _mouseHook = Native.NativeMethods.SetWindowsHookEx(Native.NativeMethods.WH_MOUSE_LL, _mouseHookProc, module, 0);
            _keyboardHook = Native.NativeMethods.SetWindowsHookEx(Native.NativeMethods.WH_KEYBOARD_LL, _keyboardHookProc, module, 0);

            if (_mouseHook == IntPtr.Zero || _keyboardHook == IntPtr.Zero)
            {
                if (_mouseHook != IntPtr.Zero) { Native.NativeMethods.UnhookWindowsHookEx(_mouseHook); _mouseHook = IntPtr.Zero; }
                if (_keyboardHook != IntPtr.Zero) { Native.NativeMethods.UnhookWindowsHookEx(_keyboardHook); _keyboardHook = IntPtr.Zero; }
                throw new InvalidOperationException("Не удалось установить низкоуровневые хуки (код ошибки Win32: " + Marshal.GetLastWin32Error() + ")");
            }

            _log.Info("Запись маршрута «" + name + "» началась. F10 — закончить запись.");
            _log.Info("Делайте всё как обычно (идите к бочке/машине и т.п.). Клики и клавиши запишутся автоматически.");

            try
            {
                while (!_stopRequested)
                {
                    Native.NativeMethods.MSG msg;
                    while (Native.NativeMethods.PeekMessage(out msg, IntPtr.Zero, 0, 0, Native.NativeMethods.PM_REMOVE))
                    {
                        // сообщения не нужны, достаточно качать очередь, чтобы хуки срабатывали
                    }
                    Thread.Sleep(2);
                }
            }
            finally
            {
                if (_mouseHook != IntPtr.Zero) { Native.NativeMethods.UnhookWindowsHookEx(_mouseHook); _mouseHook = IntPtr.Zero; }
                if (_keyboardHook != IntPtr.Zero) { Native.NativeMethods.UnhookWindowsHookEx(_keyboardHook); _keyboardHook = IntPtr.Zero; }
            }

            var route = new RouteFile
            {
                Name = name,
                ClientOriginAtRecord = new[] { originX, originY },
                Steps = _steps
            };
            _log.Info("Запись завершена: " + _steps.Count + " шагов.");
            return route;
        }

        private int DelaySinceLastEvent()
        {
            long now = _clock.ElapsedMilliseconds;
            int delay = (int)(now - _lastEventTicks);
            _lastEventTicks = now;
            return Math.Max(0, delay);
        }

        private void AddStep(RouteStep step)
        {
            step.DelayMs = DelaySinceLastEvent();
            _steps.Add(step);
            string extra = step.Type == "key" ? (" key=" + step.Key + " hold=" + step.DurationMs + "ms")
                                              : (step.Type == "wait" || step.Type == "move" || step.Type == "click" || step.Type == "rclick")
                                                  ? (" x=" + step.X + " y=" + step.Y)
                                                  : string.Empty;
            _log.Info("  + " + step.Type + extra);
        }

        private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                uint msg = unchecked((uint)wParam.ToInt64());
                var data = (Native.MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(Native.MSLLHOOKSTRUCT))!;
                int relX = data.pt.X - _originX;
                int relY = data.pt.Y - _originY;

                if (msg == Native.NativeMethods.WM_LBUTTONDOWN)
                {
                    AddStep(new RouteStep { Type = "click", X = relX, Y = relY });
                }
                else if (msg == Native.NativeMethods.WM_RBUTTONDOWN)
                {
                    AddStep(new RouteStep { Type = "rclick", X = relX, Y = relY });
                }
                else if (msg == 0x0200 /* WM_MOUSEMOVE */)
                {
                    long now = _clock.ElapsedMilliseconds;
                    bool farEnough = _lastMoveX == int.MinValue
                        || Math.Abs(relX - _lastMoveX) >= 12
                        || Math.Abs(relY - _lastMoveY) >= 12
                        || (now - _lastMoveTicks) >= 300;
                    if (farEnough)
                    {
                        _lastMoveX = relX;
                        _lastMoveY = relY;
                        _lastMoveTicks = now;
                        AddStep(new RouteStep { Type = "move", X = relX, Y = relY });
                    }
                }
            }
            return Native.NativeMethods.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
        }

        private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                uint msg = unchecked((uint)wParam.ToInt64());
                var data = (Native.KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(Native.KBDLLHOOKSTRUCT))!;
                bool injected = (data.flags & LLKHF_INJECTED) != 0;
                if (!injected)
                {
                    if (msg == Native.NativeMethods.WM_KEYDOWN)
                    {
                        if (data.vkCode == Native.NativeMethods.VK_F10)
                        {
                            _stopRequested = true;
                            return Native.NativeMethods.CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
                        }
                        if (!_keyDownTicks.ContainsKey(data.vkCode))
                            _keyDownTicks[data.vkCode] = _clock.ElapsedMilliseconds;
                    }
                    else if (msg == Native.NativeMethods.WM_KEYUP)
                    {
                        long downAt;
                        if (_keyDownTicks.TryGetValue(data.vkCode, out downAt))
                        {
                            _keyDownTicks.Remove(data.vkCode);
                            long now = _clock.ElapsedMilliseconds;
                            string? keyName = VkToName(data.vkCode);
                            if (keyName != null)
                            {
                                var step = new RouteStep
                                {
                                    Type = "key",
                                    Key = keyName,
                                    DurationMs = (int)Math.Max(20, now - downAt)
                                };
                                AddStep(step);
                            }
                        }
                    }
                }
            }
            return Native.NativeMethods.CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
        }

        private static string? VkToName(uint vk)
        {
            if (vk >= 0x41 && vk <= 0x5A) return ((char)vk).ToString(); // A-Z
            if (vk >= 0x30 && vk <= 0x39) return ((char)vk).ToString(); // 0-9
            if (vk >= 0x70 && vk <= 0x7B) return "F" + (vk - 0x70 + 1); // F1-F12
            switch (vk)
            {
                case 0x20: return "Space";
                case 0xA0: return "Shift";
                case 0x10: return "Shift";
                case 0xA4: return "Alt";
                case 0x12: return "Alt";
                case 0x0D: return "Enter";
                case 0x1B: return "Escape";
                case 0x09: return "Tab";
                case 0x08: return "Backspace";
                case 0x25: return "Left";
                case 0x26: return "Up";
                case 0x27: return "Right";
                case 0x28: return "Down";
                case 0x21: return "PageUp";
                case 0x22: return "PageDown";
                case 0x24: return "Home";
                case 0x23: return "End";
                default: return null;
            }
        }

        private sealed class StopwatchCompat
        {
            private long _start;
            public StopwatchCompat()
            {
                Restart();
            }

            public void Restart()
            {
                _start = DateTime.UtcNow.Ticks;
            }

            public long ElapsedMilliseconds
            {
                get { return (DateTime.UtcNow.Ticks - _start) / TimeSpan.TicksPerMillisecond; }
            }
        }
    }
}
