using System;
using System.Collections.Generic;
using AmazingBot.Config;
using AmazingBot.Game;
using AmazingBot.Input;
using AmazingBot.Vision;

namespace AmazingBot.Tasks
{
    /// <summary>
    /// Багажник машины: пополнение закончившихся предметов (фляги, химикаты,
    /// удобрения, семена) и замена сломанной лейки на целую.
    /// Маршрут trunk.route нужно записывать как полный круг «к машине и обратно к грядкам».
    /// </summary>
    public sealed class TrunkTask
    {
        private readonly BotContext _ctx;

        public TrunkTask(BotContext ctx)
        {
            _ctx = ctx;
        }

        /// <summary>
        /// Проверка и пополнение всего, что закончилось. Вызывается перед каждым проходом по грядкам.
        /// </summary>
        public void EnsureStock(GameClient game)
        {
            BotConfig cfg = _ctx.Config;
            string broken = cfg.Watering.BrokenCanTemplate;
            double threshold = cfg.MatchThreshold;

            // Сломанная лейка — приоритетная проблема
            if (!_ctx.State.CanBroken && !string.IsNullOrWhiteSpace(broken))
            {
                if (_ctx.Vision.Exists(broken, threshold))
                {
                    _ctx.Log.Warn("Обнаружена сломанная лейка — иду менять на целую из багажника");
                    _ctx.State.CanBroken = true;
                }
            }
            if (_ctx.State.CanBroken)
            {
                if (!TryReplaceWateringCan(game))
                    _ctx.Log.Warn("Не удалось заменить лейку — продолжаю без полива");
            }

            foreach (TrunkItemConfig item in cfg.Trunk.Items)
            {
                if (_ctx.IsPanic()) return;
                if (string.IsNullOrWhiteSpace(item.Template) || string.IsNullOrWhiteSpace(item.Name))
                    continue;
                int current = _ctx.State.Inventory.GetCount(item.Name);
                if (current > 0)
                    continue;
                _ctx.Log.Info("«" + item.Name + "» закончился — забираю из багажника");
                int taken = RestockItem(game, item);
                if (taken > 0)
                {
                    _ctx.State.Inventory.AddCount(item.Name, taken);
                    _ctx.Log.Info("Из багажника взято «" + item.Name + "»: +" + taken + " (теперь " + _ctx.State.Inventory.GetCount(item.Name) + ")");
                }
                else
                {
                    _ctx.Log.Warn("Не удалось взять «" + item.Name + "» из багажника — шаблон предмета не найден или маршрут не настроен");
                }
            }
        }

        /// <summary>Поменять сломанную лейку на целую из багажника.</summary>
        public bool TryReplaceWateringCan(GameClient game)
        {
            BotConfig cfg = _ctx.Config;
            string canTemplate = cfg.Watering.CanTemplate;
            double threshold = cfg.MatchThreshold;

            if (!GoToTrunkAndOpen(game))
                return false;

            bool replaced = false;
            try
            {
                Match? can = _ctx.Vision.Find(canTemplate, threshold);
                if (can != null)
                {
                    game.ClickTemplateCenter(can);
                    game.SleepInterruptible(cfg.Timing.AfterActionMs);
                    replaced = true;
                }
                else
                {
                    _ctx.Log.Warn("В багажнике не найдена целая лейка (шаблон «" + canTemplate + "»)");
                }
            }
            finally
            {
                CloseTrunk(game);
            }

            if (replaced)
            {
                _ctx.State.CanBroken = false;
                _ctx.State.CanLiters = cfg.Watering.CanCapacityLiters;
                _ctx.Log.Info("Лейка заменена на целую из багажника (полная, " + cfg.Watering.CanCapacityLiters + " л)");
            }
            return replaced;
        }

        private int RestockItem(GameClient game, TrunkItemConfig item)
        {
            BotConfig cfg = _ctx.Config;
            double threshold = cfg.MatchThreshold;
            int taken = 0;

            if (!GoToTrunkAndOpen(game))
                return 0;

            try
            {
                for (int i = 0; i < Math.Max(1, item.TakeCount); i++)
                {
                    if (_ctx.IsPanic()) break;
                    Match? match = _ctx.Vision.Find(item.Template, threshold);
                    if (match == null)
                        break;
                    game.ClickTemplateCenter(match);
                    taken++;
                    game.SleepInterruptible(cfg.Timing.AfterActionMs);
                }
            }
            finally
            {
                CloseTrunk(game);
            }
            return taken;
        }

        private bool GoToTrunkAndOpen(GameClient game)
        {
            BotConfig cfg = _ctx.Config;
            string routeName = cfg.Trunk.Route;
            if (string.IsNullOrWhiteSpace(routeName))
            {
                _ctx.Log.Warn("Маршрут к машине (trunk.route) не задан");
                return false;
            }
            RouteFile? route = RoutePlayer.Load(cfg.RoutesDir, routeName, _ctx.Log);
            if (route == null)
            {
                _ctx.Log.Warn("Маршрут «" + routeName + "» не найден — запишите его через меню «Записать маршрут»");
                return false;
            }
            if (!_ctx.Vision.Refresh())
                return false;
            if (!RoutePlayer.Play(route, _ctx.OriginX, _ctx.OriginY, cfg.Timing.InterStepDelayMs, _ctx.Log, _ctx.IsPanic))
                return false;
            try
            {
                Keyboard.Press(cfg.Trunk.OpenKey); // открыть багажник
            }
            catch (ArgumentException ex)
            {
                _ctx.Log.Warn(ex.Message);
                return false;
            }
            game.SleepInterruptible(cfg.Trunk.OpenWaitMs);
            return true;
        }

        private void CloseTrunk(GameClient game)
        {
            try
            {
                Keyboard.Press(_ctx.Config.Trunk.CloseKey);
            }
            catch (ArgumentException ex)
            {
                _ctx.Log.Warn(ex.Message);
            }
            game.SleepInterruptible(_ctx.Config.Timing.AfterActionMs);
        }
    }
}
