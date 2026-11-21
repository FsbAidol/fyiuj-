using System;

namespace AmazingBot.Input
{
    /// <summary>
    /// Мышь через SendInput. Координаты — абсолютные экранные (пиксели),
    /// нормализуются в диапазон 0..65535 для MOUSEEVENTF_ABSOLUTE.
    /// </summary>
    public static class Mouse
    {
        private static readonly object Sync = new object();

        public static void MoveTo(int screenX, int screenY)
        {
            int vx = Native.NativeMethods.GetSystemMetrics(Native.NativeMethods.SM_XVIRTUALSCREEN);
            int vy = Native.NativeMethods.GetSystemMetrics(Native.NativeMethods.SM_YVIRTUALSCREEN);
            int vw = Native.NativeMethods.GetSystemMetrics(Native.NativeMethods.SM_CXVIRTUALSCREEN);
            int vh = Native.NativeMethods.GetSystemMetrics(Native.NativeMethods.SM_CYVIRTUALSCREEN);
            if (vw <= 1) vw = 2;
            if (vh <= 1) vh = 2;

            int ax = (int)Math.Round((screenX - vx) * 65535.0 / (vw - 1));
            int ay = (int)Math.Round((screenY - vy) * 65535.0 / (vh - 1));
            if (ax < 0) ax = 0; else if (ax > 65535) ax = 65535;
            if (ay < 0) ay = 0; else if (ay > 65535) ay = 65535;

            Send(MOUSE_FLAGS(ax, ay, Native.NativeMethods.MOUSEEVENTF_MOVE | Native.NativeMethods.MOUSEEVENTF_ABSOLUTE));
        }

        public static void LeftDown()
        {
            SendButton(Native.NativeMethods.MOUSEEVENTF_LEFTDOWN);
        }

        public static void LeftUp()
        {
            SendButton(Native.NativeMethods.MOUSEEVENTF_LEFTUP);
        }

        public static void RightDown()
        {
            SendButton(Native.NativeMethods.MOUSEEVENTF_RIGHTDOWN);
        }

        public static void RightUp()
        {
            SendButton(Native.NativeMethods.MOUSEEVENTF_RIGHTUP);
        }

        public static void Click(int screenX, int screenY)
        {
            MoveTo(screenX, screenY);
            System.Threading.Thread.Sleep(15);
            LeftDown();
            System.Threading.Thread.Sleep(30);
            LeftUp();
        }

        public static void RightClick(int screenX, int screenY)
        {
            MoveTo(screenX, screenY);
            System.Threading.Thread.Sleep(15);
            RightDown();
            System.Threading.Thread.Sleep(30);
            RightUp();
        }

        /// <summary>Координаты в системе координат клиента окна → экран.</summary>
        public static void ClickClient(int clientX, int clientY, int originX, int originY)
        {
            Click(originX + clientX, originY + clientY);
        }

        private static Native.INPUT MOUSE_FLAGS(int ax, int ay, uint flags)
        {
            var input = new Native.INPUT();
            input.type = Native.NativeMethods.INPUT_MOUSE;
            input.u.mi = new Native.MOUSEINPUT
            {
                dx = ax,
                dy = ay,
                mouseData = 0,
                dwFlags = flags,
                time = 0,
                dwExtraInfo = IntPtr.Zero
            };
            return input;
        }

        private static void SendButton(uint buttonFlag)
        {
            Send(MOUSE_FLAGS(0, 0, buttonFlag));
        }

        private static void Send(Native.INPUT input)
        {
            lock (Sync)
            {
                uint sent = Native.NativeMethods.SendInput(1, new[] { input }, System.Runtime.InteropServices.Marshal.SizeOf(typeof(Native.INPUT)));
                if (sent != 1)
                    throw new InvalidOperationException("SendInput (мышь) не выполнен. Запустите бота с правами не ниже прав игры.");
            }
        }
    }
}
