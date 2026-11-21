using System;
using AmazingBot.Config;
using AmazingBot.Game;
using AmazingBot.Input;

namespace AmazingBot.Tasks
{
    /// <summary>
    /// Полив грядок с точным расчётом литров.
    ///
    /// Учёт: бот знает вместимость лейки и расход за одно нажатие (litersPerClick),
    /// ведёт счётчик остатка в лейке и сколько литров уже вылито в каждую грядку.
    /// Пополнение лейки: сначала фляги из инвентаря (flaskLiters каждая),
    /// затем бочка по маршруту watering.mainBarrelRoute; если воды нет и задана
    /// watering.backupBarrelRoute — идёт к резервной бочке.
    /// Маршруты к бочке нужно записывать как полный круг «от грядок к бочке и обратно».
    /// </summary>
    public sealed class WateringTask
    {
        private readonly BotContext _ctx;

        public WateringTask(BotContext ctx)
        {
            _ctx = ctx;
        }

        public int UnitsOf(double liters, double perClick)
        {
            return (int)Math.Ceiling(liters / perClick - 1e-9);
        }

        /// <summary>Полить грядку ровно до litersPerBed с учётом уже вылитого.</summary>
        public bool WaterBed(GameClient game, BedPoint bed)
        {
            WateringConfig cfg = _ctx.Config.Watering;
            BedRuntimeState bedState = _ctx.State.Bed(bed.Name);

            if (_ctx.State.CanBroken)
            {
                _ctx.Log.Warn("Лейка сломана — сначала нужно заменить её из багажника");
                return false;
            }

            double remaining = cfg.LitersPerBed - bedState.WateredLiters;
            if (remaining <= 1e-9)
            {
                _ctx.Log.Debug("Грядка «" + bed.Name + "» уже полита (" + cfg.LitersPerBed + " л)");
                return true;
            }

            double perClick = cfg.LitersPerClick;
            int totalClicks = UnitsOf(remaining, perClick);
            _ctx.Log.Info("Грядка «" + bed.Name + "»: нужно " + remaining + " л — " + totalClicks + " нажатий по " + perClick + " л");

            // Берём лейку в руки один раз
            if (!game.UseHotbarKey(cfg.CanHotbarKey))
            {
                _ctx.Log.Warn("Не удалось выбрать лейку (клавиша " + cfg.CanHotbarKey + ")");
                return false;
            }

            while (remaining > 1e-9 && !_ctx.IsPanic())
            {
                if (_ctx.State.CanLiters < perClick - 1e-9)
                {
                    if (!RefillCan(game))
                    {
                        _ctx.Log.Warn("Лейка пуста, пополнить нечем — полив грядки «" + bed.Name + "» остановлен");
                        return false;
                    }
                    // после возни с флягой лейка может сбиться — перевыбираем
                    if (!game.UseHotbarKey(cfg.CanHotbarKey))
                        return false;
                }

                double poured = Math.Min(perClick, Math.Min(remaining, _ctx.State.CanLiters));
                Mouse.Click(_ctx.OriginX + bed.X, _ctx.OriginY + bed.Y);
                game.SleepInterruptible(_ctx.Config.Timing.AfterActionMs);

                _ctx.State.CanLiters -= poured;
                bedState.WateredLiters += poured;
                remaining -= poured;

                // Ранний выход, если видно, что влаги достаточно
                string moistureFull = _ctx.Config.Templates.MoistureFull;
                string moistureLow = _ctx.Config.Templates.MoistureLow;
                bool fullVisible = !string.IsNullOrWhiteSpace(moistureFull) && _ctx.Vision.Exists(moistureFull, _ctx.Config.MatchThreshold);
                bool lowGone = !string.IsNullOrWhiteSpace(moistureLow) && !_ctx.Vision.Exists(moistureLow, _ctx.Config.MatchThreshold);
                if (fullVisible || lowGone)
                {
                    _ctx.Log.Info("Грядка «" + bed.Name + "»: влажность в норме — прекращаю полив досрочно");
                    bedState.WateredLiters = cfg.LitersPerBed;
                    remaining = 0;
                    break;
                }
            }

            _ctx.Log.Info(string.Format("Грядка «{0}»: полито. Остаток в лейке: {1:0.0} л", bed.Name, _ctx.State.CanLiters));
            return true;
        }

        /// <summary>Пополнить лейку: фляга из инвентаря → основная бочка → резервная бочка.</summary>
        public bool RefillCan(GameClient game)
        {
            WateringConfig cfg = _ctx.Config.Watering;

            // 1) Фляга из инвентаря
            if (cfg.UseFlaskFromInventory && _ctx.State.Inventory.Flasks > 0)
            {
                try
                {
                    Keyboard.Press(cfg.FlaskHotbarKey); // берём флягу
                }
                catch (ArgumentException ex)
                {
                    _ctx.Log.Warn(ex.Message);
                }
                game.SleepInterruptible(_ctx.Config.Timing.ActionDelayMs);
                try
                {
                    Keyboard.Press(cfg.CanHotbarKey); // выливаем флягу в лейку (настройте клавиши под игру)
                }
                catch (ArgumentException ex)
                {
                    _ctx.Log.Warn(ex.Message);
                }
                game.SleepInterruptible(_ctx.Config.Timing.AfterActionMs);

                _ctx.State.Inventory.AddCount("flask", -1);
                _ctx.State.CanLiters = Math.Min(cfg.CanCapacityLiters, _ctx.State.CanLiters + cfg.FlaskLiters);
                _ctx.Log.Info(string.Format("Лейка пополнена из фляги: +{0} л (в лейке {1:0.0}/{2} л, фляг осталось: {3})",
                    cfg.FlaskLiters, _ctx.State.CanLiters, cfg.CanCapacityLiters, _ctx.State.Inventory.Flasks));
                return true;
            }

            // 2) Бочки по маршрутам
            if (!string.IsNullOrWhiteSpace(cfg.MainBarrelRoute))
            {
                if (TryRefillFromBarrel(game, cfg.MainBarrelRoute, isBackup: false))
                    return true;
                if (!string.IsNullOrWhiteSpace(cfg.BackupBarrelRoute))
                {
                    _ctx.Log.Warn("В основной бочке закончилась вода — иду к резервной бочке");
                    if (TryRefillFromBarrel(game, cfg.BackupBarrelRoute, isBackup: true))
                        return true;
                }
                if (!_ctx.State.BarrelsDry)
                {
                    _ctx.State.BarrelsDry = true;
                    _ctx.Log.Error("Вода в бочках (и фляги) закончилась — полив невозможен до пополнения");
                }
                return false;
            }

            _ctx.Log.Warn("Нет фляг и не задан маршрут к бочке — пополнить лейку нечем");
            return false;
        }

        private bool TryRefillFromBarrel(GameClient game, string routeName, bool isBackup)
        {
            WateringConfig cfg = _ctx.Config.Watering;
            string label = isBackup ? "резервной" : "основной";
            RouteFile? route = RoutePlayer.Load(_ctx.Config.RoutesDir, routeName, _ctx.Log);
            if (route == null)
                return false;

            if (!_ctx.Vision.Refresh())
                return false;
            if (!RoutePlayer.Play(route, _ctx.OriginX, _ctx.OriginY, _ctx.Config.Timing.InterStepDelayMs, _ctx.Log, _ctx.IsPanic))
                return false;

            try
            {
                Keyboard.Press(cfg.BarrelRefillKey); // набрать воды в лейку
            }
            catch (ArgumentException ex)
            {
                _ctx.Log.Warn(ex.Message);
                return false;
            }
            game.SleepInterruptible(cfg.BarrelEmptyWaitMs);

            string emptyTemplate = isBackup ? cfg.BackupBarrelEmptyTemplate : cfg.MainBarrelEmptyTemplate;
            if (!string.IsNullOrWhiteSpace(emptyTemplate))
            {
                // необязательная проверка «бочка пуста»
                if (_ctx.Vision.Exists(emptyTemplate, _ctx.Config.MatchThreshold))
                {
                    _ctx.Log.Warn("Бочка (" + label + ") пуста");
                    return false;
                }
            }

            _ctx.State.CanLiters = cfg.CanCapacityLiters;
            _ctx.State.BarrelsDry = false;
            _ctx.Log.Info(string.Format("Лейка полностью пополнена от {0} бочки ({1} л)", label, cfg.CanCapacityLiters));
            return true;
        }
    }
}
