using System;
using System.Threading;
using AmazingBot.Config;
using AmazingBot.Game;
using AmazingBot.Input;
using AmazingBot.Native;
using AmazingBot.Tasks;
using AmazingBot.Vision;

namespace AmazingBot.Core
{
    /// <summary>
    /// Главный цикл бота: пополнение из багажника → проход по грядкам →
    /// анти-афк и проверка респавна транспорта между проходами.
    /// F12 — экстренная остановка в любой момент.
    /// </summary>
    public sealed class Bot
    {
        private readonly BotConfig _cfg;
        private readonly Logger _log;
        private readonly VisionService _vision;
        private readonly BotState _state;
        private readonly BotContext _ctx;
        private readonly GameClient _game;
        private readonly GardenTask _garden;
        private readonly TrunkTask _trunk;
        private readonly AntiAfkTask _antiAfk;
        private readonly CarClaimTask _car;
        private volatile bool _externalStop;

        public Bot(BotConfig cfg, string configPath, Logger log)
        {
            _cfg = cfg;
            _log = log;
            _vision = new VisionService(cfg.WindowTitleContains, System.IO.Path.GetFullPath(cfg.TemplatesDir), log);
            _state = new BotState
            {
                Inventory = RuntimeInventory.FromConfig(cfg.Inventory),
                CanLiters = cfg.Watering.StartWithFullCan ? cfg.Watering.CanCapacityLiters : 0.0,
                CanBroken = false
            };
            _ctx = new BotContext(cfg, _vision, log, _state, IsPanic);
            _game = new GameClient(_ctx);
            _garden = new GardenTask(_ctx);
            _trunk = new TrunkTask(_ctx);
            _antiAfk = new AntiAfkTask(_ctx);
            _car = new CarClaimTask(_ctx);
        }

        /// <summary>Внешняя остановка (кнопка «Стоп» в GUI). Эквивалентна F12.</summary>
        public void RequestStop()
        {
            _externalStop = true;
        }

        public bool IsPanic()
        {
            return _externalStop || NativeMethods.KeyDown(NativeMethods.VK_F12);
        }

        public int Run(bool once)
        {
            _externalStop = false;
            if (_cfg.Beds.Count == 0)
            {
                _log.Error("Не настроены грядки. Запустите меню → «Калибровка грядок».");
                return 2;
            }
            if (!_vision.EnsureWindow())
            {
                _log.Error("Окно игры не найдено. Заголовок должен содержать «" + _cfg.WindowTitleContains + "» (настройка windowTitleContains).");
                return 3;
            }

            string mode = string.Equals(_cfg.HarvestMode, "clone", StringComparison.OrdinalIgnoreCase) ? "клонирование" : "сбор урожая";
            _log.Info("Бот запущен. F12 — экстренная остановка.");
            _log.Info("Грядок: " + _cfg.Beds.Count + " | интервал проверки: " + _cfg.CheckIntervalSeconds + " с | режим: " + mode + " | авто-отключение: " + (_cfg.AutoDisableOnAllBedsDone ? "вкл" : "выкл"));
            _log.Info("Инвентарь на старте: " + _state.Inventory.Summary());
            _log.Info(string.Format("Лейка: {0:0.0}/{1:0.0} л, по {2} л за нажатие, полив {3} л на грядку",
                _state.CanLiters, _cfg.Watering.CanCapacityLiters, _cfg.Watering.LitersPerClick, _cfg.Watering.LitersPerBed));

            int consecutiveErrors = 0;
            while (true)
            {
                if (IsPanic())
                {
                    _log.Warn("Нажата F12 — экстренная остановка бота");
                    return 0;
                }

                _state.Passes++;
                _log.Info("----- Проход #" + _state.Passes + " | инвентарь: " + _state.Inventory.Summary() + " | лейка: " + string.Format("{0:0.0} л", _state.CanLiters) + " -----");

                try
                {
                    // респавн транспорта — до всего остального
                    _car.CheckIfDue(_game);
                    // пополнение из багажника при закончившихся предметах / сломанной лейке
                    _trunk.EnsureStock(_game);
                    if (_ctx.IsPanic()) break;

                    // основной проход по грядкам
                    bool allFinal = _garden.RunPass(_game);

                    if (allFinal && _cfg.AutoDisableOnAllBedsDone && _cfg.Beds.Count > 0)
                    {
                        _log.Info("Все грядки собраны/клонированы — бот автоматически отключён. Итоговых действий: " + _state.FinalActions);
                        try
                        {
                            Console.Beep(880, 350);
                            Console.Beep(1175, 500);
                        }
                        catch (Exception)
                        {
                            // звуковой сигнал необязателен
                        }
                        return 0;
                    }

                    consecutiveErrors = 0;
                }
                catch (Exception ex)
                {
                    consecutiveErrors++;
                    _log.Exception("Ошибка в проходе #" + _state.Passes, ex);
                    if (consecutiveErrors >= 5)
                    {
                        _log.Error("5 ошибок подряд — бот остановлен. Смотрите логи в " + _cfg.LogsDir);
                        return 4;
                    }
                    SleepSliced(5);
                }

                if (once)
                {
                    _log.Info("Режим «один проход» — завершаю работу");
                    return 0;
                }

                SleepWithBackgroundChecks(_cfg.CheckIntervalSeconds);
            }

            _log.Info("Бот остановлен после " + _state.Passes + " проходов. Итоговых действий: " + _state.FinalActions);
            return 0;
        }

        private void SleepSliced(int seconds)
        {
            DateTime deadline = DateTime.Now.AddSeconds(seconds);
            while (DateTime.Now < deadline && !IsPanic())
                Thread.Sleep(100);
        }

        /// <summary>
        /// Прерываемая пауза между проверками грядок: внутри живут паник-клавиша,
        /// анти-афк и проверка респавна транспорта.
        /// </summary>
        private void SleepWithBackgroundChecks(int seconds)
        {
            DateTime deadline = DateTime.Now.AddSeconds(seconds);
            while (DateTime.Now < deadline)
            {
                if (IsPanic()) return;
                _antiAfk.CheckIfDue();
                _car.CheckIfDue(_game);
                Thread.Sleep(250);
            }
        }
    }
}
