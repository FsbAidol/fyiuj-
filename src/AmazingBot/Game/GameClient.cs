using System;
using System.Threading;
using AmazingBot.Config;
using AmazingBot.Input;
using AmazingBot.Vision;

namespace AmazingBot.Game
{
    /// <summary>
    /// Низкоуровневые операции с игрой: открыть/закрыть панель грядки,
    /// клик по шаблону, использование хотбара. Все паузы — прерываемые (паник-клавиша).
    /// </summary>
    public sealed class GameClient
    {
        private readonly BotContext _ctx;

        public GameClient(BotContext ctx)
        {
            _ctx = ctx;
        }

        public void SleepInterruptible(int ms)
        {
            int remaining = ms;
            while (remaining > 0)
            {
                if (_ctx.IsPanic()) return;
                int slice = Math.Min(remaining, 40);
                Thread.Sleep(slice);
                remaining -= slice;
            }
        }

        public void ClickTemplateCenter(Match match)
        {
            int x = _ctx.OriginX + match.X + match.Width / 2;
            int y = _ctx.OriginY + match.Y + match.Height / 2;
            Mouse.Click(x, y);
        }

        public bool ClickTemplate(string templateName, double threshold)
        {
            Match? match = _ctx.Vision.Find(templateName, threshold);
            if (match == null)
                return false;
            ClickTemplateCenter(match);
            return true;
        }

        public void ClickBed(BedPoint bed)
        {
            Mouse.Click(_ctx.OriginX + bed.X, _ctx.OriginY + bed.Y);
        }

        /// <summary>Открыть панель грядки (клик по грядке).</summary>
        public bool OpenBedPanel(BedPoint bed)
        {
            if (!_ctx.Vision.Refresh())
            {
                _ctx.Log.Warn("Окно игры не найдено — не могу кликнуть грядку «" + bed.Name + "»");
                return false;
            }
            Mouse.Click(_ctx.OriginX + bed.X, _ctx.OriginY + bed.Y);
            SleepInterruptible(_ctx.Config.Timing.MenuOpenWaitMs);
            return true;
        }

        /// <summary>Закрыть панель грядки: шаблон-крестик, если задан, иначе Escape.</summary>
        public void CloseBedPanel()
        {
            string closeTemplate = _ctx.Config.Templates.ClosePanel;
            double threshold = _ctx.Config.MatchThreshold;
            if (!string.IsNullOrWhiteSpace(closeTemplate))
            {
                Match? match = _ctx.Vision.Find(closeTemplate, threshold);
                if (match != null)
                {
                    ClickTemplateCenter(match);
                    SleepInterruptible(_ctx.Config.Timing.AfterActionMs);
                    return;
                }
            }
            Keyboard.Press("Escape");
            SleepInterruptible(_ctx.Config.Timing.AfterActionMs);
        }

        /// <summary>Только выбрать слот хотбара (без клика по грядке).</summary>
        public bool UseHotbarKey(string hotbarKey)
        {
            if (string.IsNullOrWhiteSpace(hotbarKey))
            {
                _ctx.Log.Warn("Не задана горячая клавиша хотбара");
                return false;
            }
            try
            {
                Keyboard.Press(hotbarKey);
            }
            catch (ArgumentException ex)
            {
                _ctx.Log.Warn(ex.Message);
                return false;
            }
            SleepInterruptible(_ctx.Config.Timing.ActionDelayMs);
            return true;
        }

        /// <summary>Нажать клавишу хотбара и кликнуть по грядке (посадка/удобрение/химикат/лейка).</summary>
        public bool UseHotbarOnBed(string hotbarKey, BedPoint bed)
        {
            if (string.IsNullOrWhiteSpace(hotbarKey))
            {
                _ctx.Log.Warn("Не задана горячая клавиша для действия — пропуск");
                return false;
            }
            try
            {
                Keyboard.Press(hotbarKey);
            }
            catch (ArgumentException ex)
            {
                _ctx.Log.Warn(ex.Message);
                return false;
            }
            SleepInterruptible(_ctx.Config.Timing.ActionDelayMs);
            if (_ctx.IsPanic()) return false;
            Mouse.Click(_ctx.OriginX + bed.X, _ctx.OriginY + bed.Y);
            SleepInterruptible(_ctx.Config.Timing.AfterActionMs);
            return true;
        }
    }
}
