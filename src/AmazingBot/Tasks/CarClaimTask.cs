using System;
using AmazingBot.Game;
using AmazingBot.Input;

namespace AmazingBot.Tasks
{
    /// <summary>
    /// Занятие транспорта при респавне: обнаруживает уведомление о появлении машины
    /// (шаблон car.respawnNoticeTemplate), идёт к ней по маршруту car.claimRoute
    /// и занимает её клавишей car.claimKey (по умолчанию F), чтобы машина не пропала.
    /// </summary>
    public sealed class CarClaimTask
    {
        private readonly BotContext _ctx;

        public CarClaimTask(BotContext ctx)
        {
            _ctx = ctx;
        }

        public void CheckIfDue(GameClient game)
        {
            var cfg = _ctx.Config.Car;
            if (string.IsNullOrWhiteSpace(cfg.RespawnNoticeTemplate))
                return; // проверка отключена конфигом
            if (cfg.CheckIntervalSeconds < 2)
                cfg.CheckIntervalSeconds = 2;

            DateTime now = DateTime.Now;
            if ((now - _ctx.State.LastCarCheck).TotalSeconds < cfg.CheckIntervalSeconds)
                return;
            _ctx.State.LastCarCheck = now;

            try
            {
                double threshold = _ctx.Config.MatchThreshold;
                if (!_ctx.Vision.Exists(cfg.RespawnNoticeTemplate, threshold))
                    return;

                _ctx.Log.Info("Замечен респавн транспорта — иду занимать машину");
                game.SleepInterruptible(cfg.ClaimDelayMs);
                if (_ctx.IsPanic()) return;

                RouteFile? route = RoutePlayer.Load(_ctx.Config.RoutesDir, cfg.ClaimRoute, _ctx.Log);
                if (route == null)
                {
                    _ctx.Log.Warn("Маршрут «" + cfg.ClaimRoute + "» не найден — машину занять не удалось. Запишите маршрут через меню (пункт «Записать маршрут»).");
                    return;
                }
                if (!_ctx.Vision.Refresh())
                    return;
                bool played = RoutePlayer.Play(route, _ctx.OriginX, _ctx.OriginY, _ctx.Config.Timing.InterStepDelayMs, _ctx.Log, _ctx.IsPanic);
                if (!played)
                    return;
                Keyboard.Press(cfg.ClaimKey);
                _ctx.Log.Info("Машина занята (клавиша " + cfg.ClaimKey + ")");
            }
            catch (Exception ex)
            {
                _ctx.Log.Exception("Не удалось занять машину", ex);
            }
        }
    }
}
