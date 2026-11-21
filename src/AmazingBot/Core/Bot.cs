using System;
using AmazingBot.Config;
using AmazingBot.Game;
using AmazingBot.Native;
using AmazingBot.Tasks;
using AmazingBot.Vision;

namespace AmazingBot.Core
{
    /// <summary>Profession cycle runner: mining, fishing rod, or fishing net. F12 stops immediately.</summary>
    public sealed class Bot
    {
        private readonly BotConfig _cfg;
        private readonly Logger _log;
        private readonly VisionService _vision;
        private readonly AntiAfkTask _antiAfk;
        private readonly KeyPromptTask _keyPrompt;
        private volatile bool _externalStop;

        public Bot(BotConfig cfg, string configPath, Logger log)
        {
            _cfg = cfg;
            _log = log;
            _vision = new VisionService(cfg.WindowTitleContains, System.IO.Path.GetFullPath(cfg.TemplatesDir), log);
            var state = new BotState
            {
                Inventory = RuntimeInventory.FromConfig(cfg.Inventory),
                CanLiters = cfg.Watering.StartWithFullCan ? cfg.Watering.CanCapacityLiters : 0.0,
                CanBroken = false
            };
            var context = new BotContext(cfg, _vision, log, state, IsPanic);
            _antiAfk = new AntiAfkTask(context);
            _keyPrompt = new KeyPromptTask(cfg.WindowTitleContains, System.IO.Path.GetFullPath(cfg.TemplatesDir), log, IsPanic, cfg.Profession?.AutoKeyPrompt ?? true);
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
            string workMode = (_cfg.Profession?.Mode ?? string.Empty).Trim().ToLowerInvariant();
            if (workMode != "mining" && workMode != "fishing_rod" && workMode != "fishing_net")
            {
                _log.Error("Доступны только режимы: mining, fishing_rod и fishing_net. Старый режим сада отключён.");
                return 5;
            }
            if (!_vision.EnsureWindow())
            {
                _log.Error("Окно игры не найдено. Заголовок должен содержать «" + _cfg.WindowTitleContains + "» (windowTitleContains).");
                return 3;
            }
            return new ProfessionTask(_cfg, _vision, _log, IsPanic, BackgroundCheck).Run(once);
        }

        private void BackgroundCheck()
        {
            _antiAfk.CheckIfDue();
            if (_cfg.Profession?.AutoKeyPrompt != false)
                _keyPrompt.CheckIfDue();
        }
    }
}
