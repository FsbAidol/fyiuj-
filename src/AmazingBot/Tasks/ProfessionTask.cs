using System;
using System.Threading;
using AmazingBot.Config;
using AmazingBot.Core;
using AmazingBot.Input;
using AmazingBot.Vision;

namespace AmazingBot.Tasks
{
    /// <summary>
    /// Repeats a recorded full work cycle for mining or fishing.
    /// Recording the first cycle keeps game-specific keys and timings out of hardcoded guesses.
    /// F12 and the GUI Stop button interrupt playback and its between-cycle wait.
    /// </summary>
    public sealed class ProfessionTask
    {
        private readonly BotConfig _config;
        private readonly VisionService _vision;
        private readonly Logger _log;
        private readonly Func<bool> _shouldStop;
        private readonly Action _backgroundCheck;

        public ProfessionTask(BotConfig config, VisionService vision, Logger log, Func<bool> shouldStop, Action backgroundCheck)
        {
            _config = config;
            _vision = vision;
            _log = log;
            _shouldStop = shouldStop;
            _backgroundCheck = backgroundCheck;
        }

        public int Run(bool once)
        {
            ProfessionConfig settings = _config.Profession ?? new ProfessionConfig();
            string mode = (settings.Mode ?? string.Empty).Trim().ToLowerInvariant();
            string routeName = (settings.Route ?? string.Empty).Trim();
            if (mode != "mining" && mode != "fishing_rod" && mode != "fishing_net")
            {
                _log.Error("Неизвестный режим профессии «" + settings.Mode + "». Допустимо: mining, fishing_rod, fishing_net.");
                return 5;
            }
            if (routeName.Length == 0)
            {
                _log.Error("Для режима «" + mode + "» не задан маршрут полного цикла в profession.route.");
                return 5;
            }

            RouteFile? route = RoutePlayer.Load(_config.RoutesDir, routeName, _log);
            if (route == null || route.Steps.Count == 0)
            {
                _log.Error("Запишите один полный цикл работы в маршруте «" + routeName + "» (вкладка «Маршруты», F10 завершает запись).");
                return 5;
            }

            int delayMs = Math.Max(0, Math.Min(60000, settings.CycleDelayMs));
            _log.Info("Режим профессии: " + DescribeMode(mode) + ". Маршрут «" + routeName + "», шагов: " + route.Steps.Count + ". F12 — стоп.");
            _log.Warn("Проф-режим воспроизводит записанный цикл. Запишите его от начала до конца и завершите запись в безопасной точке, чтобы повтор маршрута не уводил персонажа.");

            int cycle = 0;
            while (!_shouldStop())
            {
                if (!_vision.Refresh())
                {
                    _log.Error("Не удалось захватить окно игры для маршрута профессии.");
                    return 3;
                }

                cycle++;
                _log.Info("Цикл «" + mode + "» #" + cycle);
                bool completed = RoutePlayer.Play(
                    route,
                    _vision.ClientOriginX,
                    _vision.ClientOriginY,
                    _config.Timing.InterStepDelayMs,
                    _log,
                    _shouldStop,
                    _backgroundCheck);
                if (!completed || _shouldStop())
                    break;

                _backgroundCheck();
                if (once)
                {
                    _log.Info("Один цикл завершён.");
                    return 0;
                }

                if (!SleepInterruptible(delayMs))
                    break;
            }

            _log.Info("Проф-режим остановлен после " + cycle + " циклов.");
            return 0;
        }

        private bool SleepInterruptible(int milliseconds)
        {
            int remaining = milliseconds;
            while (remaining > 0)
            {
                if (_shouldStop()) return false;
                _backgroundCheck();
                int slice = Math.Min(remaining, 100);
                Thread.Sleep(slice);
                remaining -= slice;
            }
            return !_shouldStop();
        }

        private static string DescribeMode(string mode)
        {
            switch (mode)
            {
                case "mining": return "шахта";
                case "fishing_rod": return "рыбалка удочкой";
                case "fishing_net": return "рыбалка сетью";
                default: return mode;
            }
        }
    }
}
