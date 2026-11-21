using System;

namespace AmazingBot.Vision
{
    /// <summary>
    /// Простой контейнер 32-битного BGRA-изображения (строки сверху вниз).
    /// </summary>
    public sealed class BitmapImage
    {
        public int Width { get; }
        public int Height { get; }
        public byte[] Pixels { get; } // B,G,R,A, 4 байта на пиксель

        public BitmapImage(int width, int height)
        {
            if (width <= 0 || height <= 0)
                throw new ArgumentOutOfRangeException("BitmapImage: размер должен быть положительным");
            Width = width;
            Height = height;
            Pixels = new byte[width * height * 4];
        }

        public BitmapImage(int width, int height, byte[] pixelsBgra)
        {
            if (width <= 0 || height <= 0)
                throw new ArgumentOutOfRangeException("BitmapImage: размер должен быть положительным");
            if (pixelsBgra == null || pixelsBgra.Length != width * height * 4)
                throw new ArgumentException("BitmapImage: неверный размер буфера пикселей", nameof(pixelsBgra));
            Width = width;
            Height = height;
            Pixels = pixelsBgra;
        }

        public int Stride => Width * 4;

        public byte[] ToGray(int x0, int y0, int w, int h)
        {
            byte[] gray = new byte[w * h];
            for (int ty = 0; ty < h; ty++)
            {
                int srcRow = (y0 + ty) * Width + x0;
                int dst = ty * w;
                for (int tx = 0; tx < w; tx++)
                {
                    int si = (srcRow + tx) * 4;
                    byte b = Pixels[si];
                    byte g = Pixels[si + 1];
                    byte r = Pixels[si + 2];
                    gray[dst + tx] = (byte)(0.114 * b + 0.587 * g + 0.299 * r);
                }
            }
            return gray;
        }

        public BitmapImage Crop(int x, int y, int w, int h)
        {
            if (x < 0 || y < 0 || w <= 0 || h <= 0 || x + w > Width || y + h > Height)
                throw new ArgumentOutOfRangeException("Crop: область выходит за пределы изображения");
            var crop = new BitmapImage(w, h);
            for (int ty = 0; ty < h; ty++)
            {
                int srcIndex = ((y + ty) * Width + x) * 4;
                int dstIndex = ty * w * 4;
                Buffer.BlockCopy(Pixels, srcIndex, crop.Pixels, dstIndex, w * 4);
            }
            return crop;
        }
    }
}
