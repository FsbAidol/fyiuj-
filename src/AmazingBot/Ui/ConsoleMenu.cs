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
    /// Консольное меню AmazingBot: запуск, запись маршрутов, калибровка, проверка шаблонов, настройки.
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
                Console.WriteLine(" 1) Запустить бота");
                Console.WriteLine(" 2) Один проход по грядкам (проверка настройки)");
                Console.WriteLine(" 3) Записать маршрут (к бочке / к машине)");
                Console.WriteLine(" 4) Выбрать грядки курсором (Insert — точка, End — закончить)");
                Console.WriteLine(" 5) Сохранить скриншот окна игры (screenshot.bmp)");
                Console.WriteLine(" 6) Проверить шаблоны на экране");
                Console.WriteLine(" 7) Настройки (интервал, режим, анти-афк, авто-отключение, полив)");
                Console.WriteLine(" 8) Сводка конфига");
                Console.WriteLine(" 9) Перезагрузить config.json с диска");
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
                        case "4": CalibrateBeds(); break;
                        case "5": SaveScreenshot(); break;
                        case "6": CheckTemplates(); break;
                        case "7": SettingsMenu(); break;
                        case "8": PrintSummary(); Pause(); break;
                        case "9": ReloadConfig(); Pause(); break;
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
                ? "Один проход. F12 — экстренная остановка. Enter для старта..."
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
                Console.Write("Имя маршрута (например, barrel_main / to_car / barrel_backup): ");
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
            Console.WriteLine("Сейчас начнётся запись. Сделайте маршрут целиком:");
            Console.WriteLine("  • для бочки: от грядок к бочке, наполнить лейку и ВЕРНУТЬСЯ к грядкам;");
            Console.WriteLine("  • для машины: от грядок к багажнику/машине и обратно.");
            Console.WriteLine("Если игра движет персонажа кликами или WASD — просто делайте как обычно,");
            Console.WriteLine("действия запишутся. F10 — закончить запись.");
            Console.WriteLine("ВАЖНО: не двигайте окно игры во время записи. Enter для старта...");
            Console.ReadLine();

            var recorder = new RouteRecorder(_log);
            RouteFile route = recorder.Record(name, vision.ClientOriginX, vision.ClientOriginY);
            RoutePlayer.Save(_cfg.RoutesDir, route, _log);

            string suggested = name == "barrel_main"
                ? "watering.mainBarrelRoute"
                : name == "barrel_backup" || name == "barrel_main_backup"
                    ? "watering.backupBarrelRoute"
                    : name == "to_car" ? "trunk.route / car.claimRoute" : "(используйте имя в соответствующем поле config.json)";
            Console.WriteLine("Маршрут сохранён. Пропишите его в config.json: " + suggested);
            Pause();
        }

        public void CalibrateBeds()
        {
            var vision = CreateVision();
            if (!vision.Refresh())
            {
                Console.WriteLine("Окно игры не найдено — сначала запустите игру в оконном режиме.");
                return;
            }
            Console.WriteLine("Текущих грядок в конфиге: " + _cfg.Beds.Count + ". Они будут ЗАМЕНЕНЫ новым списком.");
                Console.WriteLine("Наведите курсор на центр грядки и нажмите Insert. End — закончить. Каждая точка сохраняется сразу; F8 не используется (в игре это скриншот).");
            Console.WriteLine("Enter для старта...");
            Console.ReadLine();

            var beds = new List<BedPoint>();
            bool pointKeyWasDown = false;
            int index = 0;
            while (true)
            {
                if (NativeMethods.KeyDown(NativeMethods.VK_END))
                    break;
                bool pointKeyDown = NativeMethods.KeyDown(NativeMethods.VK_INSERT);
                if (pointKeyDown && !pointKeyWasDown)
                {
                    Native.POINT p = ScreenCapture.GetCursorPosition();
                    int relX = p.X - vision.ClientOriginX;
                    int relY = p.Y - vision.ClientOriginY;
                    if (relX >= 0 && relY >= 0 && relX < vision.ClientWidth && relY < vision.ClientHeight)
                    {
                        BedPoint? duplicate = beds.FirstOrDefault(b =>
                        {
                            long dx = (long)b.X - relX;
                            long dy = (long)b.Y - relY;
                            return dx * dx + dy * dy < 25L * 25L;
                        });
                        if (duplicate != null)
                        {
                            Console.WriteLine("Точка слишком близко к " + duplicate.Name + " — похоже, это та же грядка, повтор пропущен.");
                        }
                        else
                        {
                            index++;
                            beds.Add(new BedPoint { Name = "bed" + index, X = relX, Y = relY });
                            _cfg.Beds = new List<BedPoint>(beds);
                            ConfigService.Save(_configPath, _cfg, _log);
                            _log.Info("Точка bed" + index + " сохранена: X=" + relX + ", Y=" + relY + " относительно окна игры");
                            Console.WriteLine("Сохранена точка bed" + index + ". Всего: " + index + ". Продолжайте Insert, закончить — End.");
                        }
                    }
                    else
                    {
                        Console.WriteLine("Курсор вне окна игры — точка не сохранена.");
                    }
                }
                pointKeyWasDown = pointKeyDown;
                Thread.Sleep(25);
            }

            if (beds.Count == 0)
            {
                Console.WriteLine("Ни одной точки не сохранено — конфиг не изменён.");
                Pause();
                return;
            }
            _cfg.Beds = beds;
            ConfigService.Save(_configPath, _cfg, _log);
            Console.WriteLine("Сохранено грядок: " + beds.Count + ". Не забудьте переименовать bed1..bedN в config.json, если нужно.");
            Pause();
        }

        public void SaveScreenshot()
        {
            var vision = CreateVision();
            if (!vision.Refresh() || vision.Current == null)
            {
                Console.WriteLine("Окно игры не найдено — сначала запустите игру в оконном режиме.");
                return;
            }
            string outPath = Path.GetFullPath("screenshot.bmp");
            BmpCodec.Save(outPath, vision.Current.Image);
            Console.WriteLine("Снимок клиента сохранён: " + outPath + " (" + vision.Current.Width + "x" + vision.Current.Height + ")");
            Console.WriteLine("Откройте в Paint, вырежьте нужные элементы, сохраните как 32-битный BMP в папку templates/:");
            Console.WriteLine("  crop_ready, bed_empty, btn_harvest, btn_clone, moisture_low, moisture_full,");
            Console.WriteLine("  needs_fertilizer, can, can_broken, flask, barrel_empty, btn_close,");
            Console.WriteLine("  иконки паразитов и химикатов (по именам из parasites[]), предметы багажника,");
            Console.WriteLine("  transport_respawn (уведомление о респавне транспорта)");
            Console.WriteLine("Подробности: templates/README.md");
            Pause();
        }

        public void CheckTemplates()
        {
            var vision = CreateVision();
            if (!vision.Refresh())
            {
                Console.WriteLine("Окно игры не найдено — сначала запустите игру в оконном режиме.");
                return;
            }

            double threshold = _cfg.MatchThreshold;
            Console.WriteLine("Папка шаблонов: " + Path.GetFullPath(_cfg.TemplatesDir));
            Console.WriteLine();

            string[] files = Directory.Exists(_cfg.TemplatesDir)
                ? Directory.GetFiles(_cfg.TemplatesDir, "*.bmp")
                : new string[0];
            if (files.Length == 0)
            {
                Console.WriteLine("Нет ни одного .bmp шаблона. Сделайте снимок (пункт 5) и нарежьте шаблоны.");
            }
            else
            {
                foreach (string file in files.OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                {
                    string name = Path.GetFileNameWithoutExtension(file);
                    try
                    {
                        Match? m = vision.Find(name, Math.Max(0.55, threshold - 0.25));
                        if (m != null)
                            Console.WriteLine("  [найден ] " + name.PadRight(28) + " score=" + m.Score.ToString("0.000", CultureInfo.InvariantCulture) + " при (" + m.X + "," + m.Y + ")");
                        else
                            Console.WriteLine("  [не виден] " + name);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("  [ошибка ] " + name + ": " + ex.Message);
                    }
                }
            }

            Console.WriteLine();
            Console.WriteLine("Шаблоны, на которые ссылается конфиг, но файлов нет:");
            bool anyMissing = false;
            foreach (string required in RequiredTemplateNames())
            {
                if (!File.Exists(vision.TemplatePath(required)))
                {
                    Console.WriteLine("  ОТСУТСТВУЕТ: " + required + ".bmp");
                    anyMissing = true;
                }
            }
            if (!anyMissing)
                Console.WriteLine("  (все на месте)");
            Pause();
        }

        private IEnumerable<string> RequiredTemplateNames()
        {
            var names = new List<string>
            {
                _cfg.Templates.CropReady,
                _cfg.Templates.EmptyBed,
                _cfg.Templates.NeedsFertilizer,
                _cfg.Templates.MoistureLow,
                _cfg.Templates.MoistureFull,
                _cfg.HarvestMode == "clone" ? _cfg.Templates.CloneButton : _cfg.Templates.HarvestButton,
                _cfg.Watering.CanTemplate,
                _cfg.Watering.BrokenCanTemplate
            };
            if (!string.IsNullOrWhiteSpace(_cfg.Templates.ClosePanel)) names.Add(_cfg.Templates.ClosePanel);
            if (!string.IsNullOrWhiteSpace(_cfg.Watering.MainBarrelEmptyTemplate)) names.Add(_cfg.Watering.MainBarrelEmptyTemplate);
            if (!string.IsNullOrWhiteSpace(_cfg.Watering.BackupBarrelEmptyTemplate)) names.Add(_cfg.Watering.BackupBarrelEmptyTemplate);
            if (!string.IsNullOrWhiteSpace(_cfg.Car.RespawnNoticeTemplate)) names.Add(_cfg.Car.RespawnNoticeTemplate);
            foreach (ParasiteMap p in _cfg.Parasites)
            {
                names.Add(p.Parasite);
                names.Add(p.Chemical);
            }
            foreach (TrunkItemConfig t in _cfg.Trunk.Items)
                names.Add(t.Template);
            return names.Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.OrdinalIgnoreCase);
        }

        private void SettingsMenu()
        {
            while (true)
            {
                Console.WriteLine();
                Console.WriteLine("——— Настройки ———");
                Console.WriteLine(" 1) Интервал проверки грядок: " + _cfg.CheckIntervalSeconds + " с");
                Console.WriteLine(" 2) Режим после готовности: " + _cfg.HarvestMode + "  (harvest | clone)");
                Console.WriteLine(" 3) Анти-афк: " + (_cfg.AntiAfk.Enabled ? "вкл" : "выкл") + " | интервал " + _cfg.AntiAfk.IntervalSeconds + " с");
                Console.WriteLine(" 4) Авто-отключение после сбора ВСЕХ грядок: " + (_cfg.AutoDisableOnAllBedsDone ? "вкл" : "выкл"));
                Console.WriteLine(" 5) Полив: " + _cfg.Watering.LitersPerBed + " л на грядку, лейка " + _cfg.Watering.CanCapacityLiters + " л, за нажатие " + _cfg.Watering.LitersPerClick + " л");
                Console.WriteLine(" 6) Литров на грядку");
                Console.WriteLine(" 7) Вместимость лейки (л)");
                Console.WriteLine(" 8) Литров за нажатие");
                Console.WriteLine(" 9) Точность распознавания (matchThreshold): " + _cfg.MatchThreshold.ToString("0.00"));
                Console.WriteLine(" 0) Назад");
                Console.Write("Выбор: ");
                string choice = (Console.ReadLine() ?? string.Empty).Trim();
                bool changed = false;
                switch (choice)
                {
                    case "1":
                        if (TryReadInt("Интервал в секундах (1..3600): ", 1, 3600, out int interval))
                        {
                            _cfg.CheckIntervalSeconds = interval;
                            changed = true;
                        }
                        break;
                    case "2":
                        _cfg.HarvestMode = string.Equals(_cfg.HarvestMode, "clone", StringComparison.OrdinalIgnoreCase) ? "harvest" : "clone";
                        Console.WriteLine("Режим: " + _cfg.HarvestMode);
                        changed = true;
                        break;
                    case "3":
                        Console.WriteLine(" 1 — включить / 2 — выключить анти-афк / 3 — изменить интервал");
                        Console.Write("Выбор: ");
                        string sub = (Console.ReadLine() ?? string.Empty).Trim();
                        if (sub == "1") { _cfg.AntiAfk.Enabled = true; changed = true; }
                        else if (sub == "2") { _cfg.AntiAfk.Enabled = false; changed = true; }
                        else if (sub == "3" && TryReadInt("Интервал анти-афк в секундах (5..3600): ", 5, 3600, out int afk))
                        {
                            _cfg.AntiAfk.IntervalSeconds = afk;
                            changed = true;
                        }
                        break;
                    case "4":
                        _cfg.AutoDisableOnAllBedsDone = !_cfg.AutoDisableOnAllBedsDone;
                        Console.WriteLine("Авто-отключение: " + (_cfg.AutoDisableOnAllBedsDone ? "вкл" : "выкл"));
                        changed = true;
                        break;
                    case "5":
                        break;
                    case "6":
                        if (TryReadDouble("Литров на грядку (0.5..100): ", 0.5, 100, out double perBed))
                        {
                            _cfg.Watering.LitersPerBed = perBed;
                            changed = true;
                        }
                        break;
                    case "7":
                        if (TryReadDouble("Вместимость лейки в литрах (0.5..1000): ", 0.5, 1000, out double capacity))
                        {
                            _cfg.Watering.CanCapacityLiters = capacity;
                            changed = true;
                        }
                        break;
                    case "8":
                        if (TryReadDouble("Литров за одно нажатие (0.1..100): ", 0.1, 100, out double perClick))
                        {
                            _cfg.Watering.LitersPerClick = perClick;
                            changed = true;
                        }
                        break;
                    case "9":
                        if (TryReadDouble("Точность 0.5..1.0 (например 0.82): ", 0.5, 1.0, out double thr))
                        {
                            _cfg.MatchThreshold = thr;
                            changed = true;
                        }
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
            Console.WriteLine("Шаблоны: " + Path.GetFullPath(_cfg.TemplatesDir) + " | маршруты: " + Path.GetFullPath(_cfg.RoutesDir) + " | логи: " + Path.GetFullPath(_cfg.LogsDir));
            Console.WriteLine("Грядок: " + _cfg.Beds.Count + (_cfg.Beds.Count == 0 ? "" : " — " + string.Join(", ", _cfg.Beds.Select(b => b.Name + "(" + b.X + "," + b.Y + ")"))));
            Console.WriteLine("Режим: " + _cfg.HarvestMode + " | интервал: " + _cfg.CheckIntervalSeconds + " с | авто-отключение: " + (_cfg.AutoDisableOnAllBedsDone ? "вкл" : "выкл"));
            Console.WriteLine("Полив: " + _cfg.Watering.LitersPerBed + " л/грядку, лейка " + _cfg.Watering.CanCapacityLiters + " л, клик = " + _cfg.Watering.LitersPerClick + " л");
            Console.WriteLine("  фляга = " + _cfg.Watering.FlaskLiters + " л | бочка основная: " + Empty(_cfg.Watering.MainBarrelRoute) + " | резервная: " + Empty(_cfg.Watering.BackupBarrelRoute));
            Console.WriteLine("Инвентарь на старте: фляги " + _cfg.Inventory.Flasks + ", удобрение " + _cfg.Inventory.Fertilizer + ", семена " + _cfg.Inventory.Seeds + ", химикаты: " + (_cfg.Inventory.Chemicals.Count == 0 ? "нет" : string.Join(", ", _cfg.Inventory.Chemicals.Select(kv => kv.Key + "=" + kv.Value))));
            Console.WriteLine("Багажник (" + _cfg.Trunk.Items.Count + " позиций): " + string.Join(", ", _cfg.Trunk.Items.Select(i => i.Name + " x" + i.TakeCount)));
            Console.WriteLine("Паразиты: " + (_cfg.Parasites.Count == 0 ? "не настроены" : string.Join(", ", _cfg.Parasites.Select(p => p.Name + "→" + p.Chemical))));
            Console.WriteLine("Анти-афк: " + (_cfg.AntiAfk.Enabled ? "вкл, раз в " + _cfg.AntiAfk.IntervalSeconds + " с, клавиши [" + string.Join("+", _cfg.AntiAfk.Keys) + "]" : "выкл"));
            Console.WriteLine("Респавн транспорта: " + (string.IsNullOrWhiteSpace(_cfg.Car.RespawnNoticeTemplate) ? "проверка выключена" : "шаблон " + _cfg.Car.RespawnNoticeTemplate + ", маршрут " + _cfg.Car.ClaimRoute));
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
            Console.WriteLine("   AmazingBot — бот для Amazing Online (грядки)   ");
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

        private static string Empty(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "(не задано)" : value;
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
