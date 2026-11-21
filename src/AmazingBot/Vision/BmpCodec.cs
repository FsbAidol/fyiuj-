using System;
using System.IO;

namespace AmazingBot.Vision
{
    /// <summary>
    /// Чтение и запись 32/24-битных BMP-файлов без внешних библиотек.
    /// Шаблоны для распознавания — это BMP, сохранённые из меню «Сохранить скриншот» + Paint.
    /// </summary>
    public static class BmpCodec
    {
        public static BitmapImage Load(string path)
        {
            byte[] data = File.ReadAllBytes(path);
            if (data.Length < 54)
                throw new InvalidDataException("BMP: файл слишком маленький: " + path);
            if (data[0] != (byte)'B' || data[1] != (byte)'M')
                throw new InvalidDataException("BMP: не похоже на BMP-файл: " + path);

            int pixelOffset = BitConverter.ToInt32(data, 10);
            int width = BitConverter.ToInt32(data, 18);
            int rawHeight = BitConverter.ToInt32(data, 22);
            ushort planes = BitConverter.ToUInt16(data, 26);
            ushort bpp = BitConverter.ToUInt16(data, 28);
            uint compression = BitConverter.ToUInt32(data, 30);

            if (planes != 1)
                throw new InvalidDataException("BMP: неожиданный planes=" + planes + " в " + path);
            if (bpp != 24 && bpp != 32)
                throw new InvalidDataException("BMP: поддерживаются только 24 и 32 бита на пиксель: " + path);
            if (compression != 0 && compression != 3) // BI_RGB / BI_BITFIELDS
                throw new InvalidDataException("BMP: сжатые BMP не поддерживаются (сохраните как 32-битный BMP): " + path);

            bool topDown = rawHeight < 0;
            int height = Math.Abs(rawHeight);
            int bytesPerPixel = bpp / 8;
            int srcStride = ((width * bytesPerPixel + 3) / 4) * 4;

            var img = new BitmapImage(width, height);
            for (int row = 0; row < height; row++)
            {
                int srcRow = topDown ? row : (height - 1 - row);
                int srcIndex = pixelOffset + srcRow * srcStride;
                int dstIndex = row * width * 4;
                for (int x = 0; x < width; x++)
                {
                    int si = srcIndex + x * bytesPerPixel;
                    if (si + bytesPerPixel > data.Length)
                        throw new InvalidDataException("BMP: обрезанные данные в " + path);
                    img.Pixels[dstIndex + x * 4 + 0] = data[si + 0]; // B
                    img.Pixels[dstIndex + x * 4 + 1] = data[si + 1]; // G
                    img.Pixels[dstIndex + x * 4 + 2] = data[si + 2]; // R
                    img.Pixels[dstIndex + x * 4 + 3] = bpp == 32 ? data[si + 3] : (byte)255; // A
                }
            }
            return img;
        }

        public static void Save(string path, BitmapImage img)
        {
            int width = img.Width, height = img.Height;
            int rowSize = width * 4; // 32bpp всегда кратен 4
            int pixelBytes = rowSize * height;
            int fileSize = 54 + pixelBytes;

            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
            using (var bw = new BinaryWriter(fs))
            {
                // BITMAPFILEHEADER
                bw.Write((byte)'B');
                bw.Write((byte)'M');
                bw.Write(fileSize);
                bw.Write((ushort)0);
                bw.Write((ushort)0);
                bw.Write(54); // offset данных

                // BITMAPINFOHEADER
                bw.Write(40); // biSize
                bw.Write(width);
                bw.Write(height); // положительная высота = строки снизу вверх
                bw.Write((ushort)1); // planes
                bw.Write((ushort)32); // bpp
                bw.Write(0u); // BI_RGB
                bw.Write(pixelBytes);
                bw.Write(0);
                bw.Write(0);
                bw.Write(0u);
                bw.Write(0u);

                for (int row = height - 1; row >= 0; row--) // снизу вверх
                {
                    int srcIndex = row * width * 4;
                    bw.Write(img.Pixels, srcIndex, rowSize);
                }
            }
        }
    }
}
