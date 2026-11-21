using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using AmazingBot.Config;
using AmazingBot.Core;
using AmazingBot.Input;
using AmazingBot.Native;
using AmazingBot.Vision;

namespace AmazingBot.Ui
{
    /// <summary>
    /// Консольное меню AmazingBot: запуск и настройка повторяемых циклов профессий.
    /// </summary>
    public sealed class ConsoleMenu
    {
        private readonly string _configPath;
        private BotConfig _cfg;
        private Logger _log;

        public ConsoleMenu(BotConfig cfg, string configPath, Logger log)
        {
            _cfg = cfg;
            _configPath = configPath;
            _log = log;
        }

        public void Run()
        {
            while (true)
            {
                PrintHeader();
                Console.WriteLine(" 1) Запустить цикл профессии");
                Console.WriteLine(" 2) Выполнить один цикл и выйти");
                Console.WriteLine(" 3) Записать маршрут шахты/рыбалки (F10 — конец)");
                Console.WriteLine(" 4) Настройки профессии и анти-афк");
                Console.WriteLine(" 5) Сводка конфига");
                Console.WriteLine(" 6) Перезагрузить config.json с диска");
                Console.WriteLine(" 0) Выход");
                Console.Write("Выбор: ");
                string choice = (Console.ReadLine() ?? string.Empty).Trim();
                Console.WriteLine();
                try
                {
                    switch (choice)
                    {
                        case "1": StartBot(false); break;
                        case "2": StartBot(true); break;
                        case "3": RecordRoute(); break;
                        case "4": SettingsMenu(); break;
                        case "5": PrintSummary(); Pause(); break;
                        case "6": ReloadConfig(); Pause(); break;
                        case "0": return;
                        default:
                            Console.WriteLine("Нет такого пункта.");
                            Pause();
                            break;
                    }
                }
                catch (Exception ex)
                {
                    _log.Exception("Ошибка в меню", ex);
                    Pause();
                }
            }
        }

        // ---------- Пункты меню ----------

        public void StartBot(bool once)
        {
            Console.WriteLine(once
                ? "Один цикл. F12 — экстренная остановка. Enter для старта..."
                : "Запускаю бота. F12 — экстренная остановка. Enter для старта...");
            Console.ReadLine();
            var bot = new AmazingBot.Core.Bot(_cfg, _configPath, _log);
            int code = bot.Run(once);
            Console.WriteLine();
            Console.WriteLine("Бот завершился (код " + code + "). Лог: " + _log.LogFile);
            Pause();
        }

        public void RecordRoute(string? presetName = null)
        {
            string name;
            if (!string.IsNullOrWhiteSpace(presetName))
            {
                name = presetName.Trim();
                Console.WriteLine("Имя маршрута: " + name);
            }
            else
            {
                Console.Write("Имя цикла (mine_cycle / fishing_rod_cycle / fishing_net_cycle): ");
                name = (Console.ReadLine() ?? string.Empty).Trim();
            }
            if (name.Length == 0)
            {
                Console.WriteLine("Имя пустое — запись отменена.");
                return;
            }
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            name = name.Replace(' ', '_');

            var vision = CreateVision();
            if (!vision.Refresh())
            {
                Console.WriteLine("Окно игры не найдено — сначала запустите игру в оконном режиме.");
                return;
            }

            Console.WriteLine();
            Console.WriteLine("Сейчас начнётся запись. Сделайте один полный цикл шахты или рыбалки и завершите его у точки старта.");
            Console.WriteLine("Если игра движет персонажа кликами или WASD — просто делайте как обычно,");
            Console.WriteLine("действия запишутся. F10 — закончить запись.");
            Console.WriteLine("ВАЖНО: не двигайте окно игры во время записи. Enter для старта...");
            Console.ReadLine();

            var recorder = new RouteRecorder(_log);
            RouteFile route = recorder.Record(name, vision.ClientOriginX, vision.ClientOriginY);
            RoutePlayer.Save(_cfg.RoutesDir, route, _log);

            Console.WriteLine("Цикл сохранён. Выберите его в настройках профессии или укажите его имя в config.json.");
            Pause();
        }

        private void SettingsMenu()
        {
            if (_cfg.Profession == null) _cfg.Profession = new ProfessionConfig();
            while (true)
            {
                Console.WriteLine();
                Console.WriteLine("——— Настройки профессии ———");
                Console.WriteLine(" 1) Профессия: " + _cfg.Profession.Mode + " (1 шахта / 2 удочка / 3 сеть)");
                Console.WriteLine(" 2) Маршрут: " + _cfg.Profession.Route);
                Console.WriteLine(" 3) Пауза между циклами: " + _cfg.Profession.CycleDelayMs + " мс");
                Console.WriteLine(" 4) Анти-афк: " + (_cfg.AntiAfk.Enabled ? "вкл" : "выкл") + " | интервал " + _cfg.AntiAfk.IntervalSeconds + " с");
                Console.WriteLine(" 0) Назад");
                Console.Write("Выбор: ");
                string choice = (Console.ReadLine() ?? string.Empty).Trim();
                bool changed = false;
                switch (choice)
                {
                    case "1":
                        Console.Write("Выбери 1=шахта, 2=удочка, 3=сеть: ");
                        string mode = (Console.ReadLine() ?? string.Empty).Trim();
                        if (mode == "1") { _cfg.Profession.Mode = "mining"; _cfg.Profession.Route = "mine_cycle"; changed = true; }
                        else if (mode == "2") { _cfg.Profession.Mode = "fishing_rod"; _cfg.Profession.Route = "fishing_rod_cycle"; changed = true; }
                        else if (mode == "3") { _cfg.Profession.Mode = "fishing_net"; _cfg.Profession.Route = "fishing_net_cycle"; changed = true; }
                        break;
                    case "2":
                        Console.Write("Имя маршрута без .json: ");
                        string route = (Console.ReadLine() ?? string.Empty).Trim();
                        if (route.Length > 0) { _cfg.Profession.Route = route; changed = true; }
                        break;
                    case "3":
                        if (TryReadInt("Пауза в миллисекундах (0..60000): ", 0, 60000, out int delay))
                        {
                            _cfg.Profession.CycleDelayMs = delay;
                            changed = true;
                        }
                        break;
                    case "4":
                        _cfg.AntiAfk.Enabled = !_cfg.AntiAfk.Enabled;
                        if (_cfg.AntiAfk.Enabled && TryReadInt("Интервал анти-афк, сек (5..3600): ", 5, 3600, out int afk))
                            _cfg.AntiAfk.IntervalSeconds = afk;
                        changed = true;
                        break;
                    case "0":
                        return;
                }
                if (changed)
                {
                    ConfigService.Save(_configPath, _cfg, _log);
                    _log.MinimumLevel = ParseLogLevel(_cfg.LogLevel);
                }
            }
        }

        private void PrintSummary()
        {
            Console.WriteLine("Конфиг: " + Path.GetFullPath(_configPath));
            Console.WriteLine("Окно: заголовок содержит «" + _cfg.WindowTitleContains + "»");
            Console.WriteLine("Профессия: " + (_cfg.Profession?.Mode ?? "mining") + " | маршрут: " + (_cfg.Profession?.Route ?? ""));
            Console.WriteLine("Пауза цикла: " + (_cfg.Profession?.CycleDelayMs ?? 1000) + " мс");
            Console.WriteLine("Анти-афк: " + (_cfg.AntiAfk.Enabled ? "вкл, раз в " + _cfg.AntiAfk.IntervalSeconds + " с" : "выкл"));
            if (Directory.Exists(_cfg.RoutesDir))
                Console.WriteLine("Маршруты: " + string.Join(", ", Directory.GetFiles(_cfg.RoutesDir, "*.json").Select(Path.GetFileNameWithoutExtension)));
        }

        private void ReloadConfig()
        {
            List<string> problems;
            _cfg = ConfigService.Load(_configPath, _log, out problems);
            _log.MinimumLevel = ParseLogLevel(_cfg.LogLevel);
            foreach (string problem in problems)
                _log.Warn("Конфиг: " + problem);
            if (problems.Count == 0)
                _log.Info("Конфиг перезагружен без замечаний");
        }

        // ---------- Утилиты ----------

        private VisionService CreateVision()
        {
            return new VisionService(_cfg.WindowTitleContains, Path.GetFullPath(_cfg.TemplatesDir), _log);
        }

        private void PrintHeader()
        {
            Console.Clear();
            Console.WriteLine("==================================================");
            Console.WriteLine("   AmazingBot — шахта и рыбалка                ");
            Console.WriteLine("==================================================");
            Console.WriteLine("F12 — экстренная остановка бота в любой момент.");
            Console.WriteLine();
        }

        private static void Pause()
        {
            Console.WriteLine();
            Console.Write("Enter — продолжить...");
            Console.ReadLine();
        }

        private static LogLevel ParseLogLevel(string? value)
        {
            switch ((value ?? "Info").Trim().ToLowerInvariant())
            {
                case "debug": return LogLevel.Debug;
                case "warn": return LogLevel.Warn;
                case "error": return LogLevel.Error;
                default: return LogLevel.Info;
            }
        }

        private static bool TryReadInt(string prompt, int min, int max, out int value)
        {
            Console.Write(prompt);
            string input = (Console.ReadLine() ?? string.Empty).Trim();
            if (int.TryParse(input, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) && value >= min && value <= max)
                return true;
            Console.WriteLine("Некорректное число.");
            value = 0;
            return false;
        }

        private static bool TryReadDouble(string prompt, double min, double max, out double value)
        {
            Console.Write(prompt);
            string input = (Console.ReadLine() ?? string.Empty).Trim().Replace(',', '.');
            if (double.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && value >= min && value <= max)
                return true;
            Console.WriteLine("Некорректное число.");
            value = 0;
            return false;
        }
    }
}
