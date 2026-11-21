using System;
using System.Collections.Generic;

namespace AmazingBot.Input
{
    /// <summary>
    /// Клавиатура через SendInput. Имена клавиш регистронезависимы:
    /// "F1".."F12", "A".."Z", "0".."9", "Space", "Enter", "Escape", "Tab",
    /// "Left", "Right", "Up", "Down", "Alt", "Shift", "Backspace", "Delete", "Home", "End".
    /// </summary>
    public static class Keyboard
    {
        private static readonly object Sync = new object();

        private static readonly Dictionary<string, ushort> KeyMap = BuildKeyMap();

        public static bool TryGetVirtualKey(string name, out ushort vk)
        {
            return KeyMap.TryGetValue(Normalize(name), out vk);
        }

        public static void Press(string keyName)
        {
            ushort vk;
            if (!TryGetVirtualKey(keyName, out vk))
                throw new ArgumentException("Неизвестная клавиша: «" + keyName + "». См. список в комментарии к классу Keyboard.");
            Down(vk);
            System.Threading.Thread.Sleep(35);
            Up(vk);
        }

        public static void Down(string keyName)
        {
            ushort vk;
            if (!TryGetVirtualKey(keyName, out vk))
                throw new ArgumentException("Неизвестная клавиша: «" + keyName + "»");
            Down(vk);
        }

        public static void Up(string keyName)
        {
            ushort vk;
            if (!TryGetVirtualKey(keyName, out vk))
                throw new ArgumentException("Неизвестная клавиша: «" + keyName + "»");
            Up(vk);
        }

        public static void Down(ushort vk)
        {
            SendKey(vk, 0);
        }

        public static void Up(ushort vk)
        {
            SendKey(vk, Native.NativeMethods.KEYEVENTF_KEYUP);
        }

        private static void SendKey(ushort vk, uint flags)
        {
            var input = new Native.INPUT();
            input.type = Native.NativeMethods.INPUT_KEYBOARD;
            input.u.ki = new Native.KEYBDINPUT
            {
                wVk = vk,
                wScan = 0,
                dwFlags = flags,
                time = 0,
                dwExtraInfo = IntPtr.Zero
            };
            lock (Sync)
            {
                uint sent = Native.NativeMethods.SendInput(1, new[] { input }, System.Runtime.InteropServices.Marshal.SizeOf(typeof(Native.INPUT)));
                if (sent != 1)
                    throw new InvalidOperationException("SendInput (клавиатура) не выполнен. Запустите бота с правами не ниже прав игры.");
            }
        }

        private static string Normalize(string name)
        {
            return (name ?? string.Empty).Trim().ToUpperInvariant();
        }

        private static Dictionary<string, ushort> BuildKeyMap()
        {
            var map = new Dictionary<string, ushort>(StringComparer.Ordinal);
            for (char c = 'A'; c <= 'Z'; c++)
                map[c.ToString()] = (ushort)c;
            for (char c = '0'; c <= '9'; c++)
                map[c.ToString()] = (ushort)c;
            for (int i = 1; i <= 12; i++)
                map["F" + i] = (ushort)(0x70 + i - 1);
            map["SPACE"] = 0x20;
            map["ENTER"] = 0x0D;
            map["RETURN"] = 0x0D;
            map["ESCAPE"] = 0x1B;
            map["ESC"] = 0x1B;
            map["TAB"] = 0x09;
            map["BACKSPACE"] = 0x08;
            map["DELETE"] = 0x2E;
            map["DEL"] = 0x2E;
            map["HOME"] = 0x24;
            map["END"] = 0x23;
            map["LEFT"] = 0x25;
            map["UP"] = 0x26;
            map["RIGHT"] = 0x27;
            map["DOWN"] = 0x28;
            map["PAGEUP"] = 0x21;
            map["PAGEDOWN"] = 0x22;
            map["SHIFT"] = 0xA0; // VK_LSHIFT
            map["LEFTSHIFT"] = 0xA0;
            map["ALT"] = 0xA4; // VK_LMENU
            map["LEFTALT"] = 0xA4;
            // Раскладка по-русски / по-английски для удобства
            map["ПРОБЕЛ"] = 0x20;
            map["ВВОД"] = 0x0D;
            return map;
        }
    }
}
