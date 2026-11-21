using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace AmazingBot.Vision
{
    public sealed class CapturedFrame
    {
        public int Width { get; set; }
        public int Height { get; set; }
        public int OriginX { get; set; }
        public int OriginY { get; set; }
        public BitmapImage Image { get; set; } = null!;
    }

    /// <summary>
    /// Поиск окна игры и захват его клиентской области через GDI BitBlt.
    /// Работает с оконным и безрамочным режимами; эксклюзивный полноэкранный режим не поддерживается.
    /// </summary>
    public static class ScreenCapture
    {
        public static IntPtr FindWindowByTitle(string titlePart)
        {
            IntPtr found = IntPtr.Zero;
            Native.NativeMethods.EnumWindows(delegate (IntPtr hWnd, IntPtr lParam)
            {
                if (!Native.NativeMethods.IsWindowVisible(hWnd))
                    return true;
                var sb = new StringBuilder(512);
                Native.NativeMethods.GetWindowText(hWnd, sb, sb.Capacity);
                string title = sb.ToString();
                if (title.Length == 0)
                    return true;
                if (title.IndexOf(titlePart, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    found = hWnd;
                    return false; // стоп перечисление
                }
                return true;
            }, IntPtr.Zero);
            return found;
        }

        public static bool TryGetClientRect(IntPtr hwnd, out int width, out int height, out int originX, out int originY)
        {
            width = height = originX = originY = 0;
            if (hwnd == IntPtr.Zero || !Native.NativeMethods.IsWindow(hwnd))
                return false;
            Native.RECT rc;
            if (!Native.NativeMethods.GetClientRect(hwnd, out rc))
                return false;
            var origin = new Native.POINT { X = 0, Y = 0 };
            if (!Native.NativeMethods.ClientToScreen(hwnd, ref origin))
                return false;
            width = rc.Right - rc.Left;
            height = rc.Bottom - rc.Top;
            originX = origin.X;
            originY = origin.Y;
            return width > 0 && height > 0;
        }

        public static CapturedFrame CaptureClient(IntPtr hwnd)
        {
            int w, h, originX, originY;
            if (!TryGetClientRect(hwnd, out w, out h, out originX, out originY))
                throw new InvalidOperationException("Не удалось получить клиентскую область окна игры");

            IntPtr screenDc = Native.NativeMethods.GetDC(IntPtr.Zero);
            IntPtr memDc = IntPtr.Zero;
            IntPtr hBitmap = IntPtr.Zero;
            try
            {
                memDc = Native.NativeMethods.CreateCompatibleDC(screenDc);
                if (memDc == IntPtr.Zero)
                    throw new InvalidOperationException("CreateCompatibleDC failed");
                hBitmap = Native.NativeMethods.CreateCompatibleBitmap(screenDc, w, h);
                if (hBitmap == IntPtr.Zero)
                    throw new InvalidOperationException("CreateCompatibleBitmap failed");
                IntPtr oldBitmap = Native.NativeMethods.SelectObject(memDc, hBitmap);

                uint rop = Native.NativeMethods.SRCCOPY | Native.NativeMethods.CAPTUREBLT;
                bool ok = Native.NativeMethods.BitBlt(memDc, 0, 0, w, h, screenDc, originX, originY, rop);
                if (!ok)
                {
                    // CAPTUREBLT не всегда поддерживается — пробуем без него
                    ok = Native.NativeMethods.BitBlt(memDc, 0, 0, w, h, screenDc, originX, originY, Native.NativeMethods.SRCCOPY);
                }
                if (!ok)
                    throw new InvalidOperationException("BitBlt не смог захватить окно (запустите бота с правами не ниже прав игры)");

                var info = Native.NativeMethods.BITMAPINFOHEADER.CreateTopDown(w, h, 32, 0);
                int bufferSize = w * h * 4;
                byte[] pixels = new byte[bufferSize];
                IntPtr bitsPtr = Marshal.AllocHGlobal(bufferSize);
                try
                {
                    int scanLines = Native.NativeMethods.GetDIBits(memDc, hBitmap, 0, (uint)h, bitsPtr, ref info, 0);
                    if (scanLines == 0)
                        throw new InvalidOperationException("GetDIBits вернул 0 строк");
                    Marshal.Copy(bitsPtr, pixels, 0, bufferSize);
                }
                finally
                {
                    Marshal.FreeHGlobal(bitsPtr);
                }

                if (oldBitmap != IntPtr.Zero)
                    Native.NativeMethods.SelectObject(memDc, oldBitmap);

                return new CapturedFrame
                {
                    Width = w,
                    Height = h,
                    OriginX = originX,
                    OriginY = originY,
                    Image = new BitmapImage(w, h, pixels)
                };
            }
            finally
            {
                if (hBitmap != IntPtr.Zero) Native.NativeMethods.DeleteObject(hBitmap);
                if (memDc != IntPtr.Zero) Native.NativeMethods.DeleteDC(memDc);
                if (screenDc != IntPtr.Zero) Native.NativeMethods.ReleaseDC(IntPtr.Zero, screenDc);
            }
        }

        public static Native.POINT GetCursorPosition()
        {
            Native.NativeMethods.GetCursorPos(out Native.POINT p);
            return p;
        }
    }
}
