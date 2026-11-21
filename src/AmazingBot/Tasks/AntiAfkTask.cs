using System;
using AmazingBot.Game;
using AmazingBot.Input;

namespace AmazingBot.Tasks
{
    /// <summary>
    /// Анти-афк: раз в antiAfk.intervalSeconds нажимает заданные клавиши
    /// (по умолчанию Space) и слегка двигает мышь, чтобы игра не кикала за бездействие.
    /// </summary>
    public sealed class AntiAfkTask
    {
        private readonly BotContext _ctx;

        public AntiAfkTask(BotContext ctx)
        {
            _ctx = ctx;
        }

        public void CheckIfDue()
        {
            var cfg = _ctx.Config.AntiAfk;
            if (!cfg.Enabled)
                return;
            if (cfg.IntervalSeconds < 5)
                cfg.IntervalSeconds = 5;

            DateTime now = DateTime.Now;
            if ((now - _ctx.State.LastAntiAfk).TotalSeconds < cfg.IntervalSeconds)
                return;
            _ctx.State.LastAntiAfk = now;

            try
            {
                foreach (string key in cfg.Keys)
                {
                    if (_ctx.IsPanic()) return;
                    if (string.IsNullOrWhiteSpace(key)) continue;
                    Keyboard.Press(key);
                    System.Threading.Thread.Sleep(80);
                }
                if (cfg.MouseWiggle && !_ctx.IsPanic())
                {
                    Native.NativeMethods.GetCursorPos(out Native.POINT p);
                    Mouse.MoveTo(p.X + 7, p.Y + 5);
                    System.Threading.Thread.Sleep(60);
                    Mouse.MoveTo(p.X - 5, p.Y - 4);
                    System.Threading.Thread.Sleep(60);
                    Mouse.MoveTo(p.X, p.Y);
                }
                _ctx.Log.Debug("Анти-афк: действия выполнены");
            }
            catch (Exception ex)
            {
                _ctx.Log.Exception("Анти-афк: ошибка выполнения", ex);
            }
        }
    }
}
