using System;
using System.Collections.Generic;

namespace AmazingBot.Vision
{
    public sealed class Match
    {
        public int X;
        public int Y;
        public int Width;
        public int Height;
        public double Score;
    }

    /// <summary>
    /// Поиск шаблона на изображении по нормированной кросс-корреляции (NCC).
    /// Двухпроходная схема: грубый проход с шагом 2 и прореживанием пикселей,
    /// затем точное вычисление вокруг кандидатов. Работает без внешних библиотек.
    /// </summary>
    public static class TemplateMatcher
    {
        public static List<Match> FindAll(BitmapImage frame, BitmapImage template, double threshold, int maxResults)
        {
            var matches = new List<Match>();
            int W = frame.Width, H = frame.Height;
            int w = template.Width, h = template.Height;
            if (w <= 0 || h <= 0 || W < w || H < h)
                return matches;
            if (maxResults <= 0)
                maxResults = 16;

            // Серые кадры
            byte[] grayT = template.ToGray(0, 0, w, h);
            int templateCount = w * h;
            double meanT = 0;
            for (int i = 0; i < templateCount; i++) meanT += grayT[i];
            meanT /= templateCount;
            double varT = 0;
            for (int i = 0; i < templateCount; i++)
            {
                double d = grayT[i] - meanT;
                varT += d * d;
            }
            varT /= templateCount;
            double stdT = Math.Sqrt(varT);
            if (stdT < 1e-6)
                return matches; // вырожденный шаблон (однотонный)

            byte[] grayF = frame.ToGray(0, 0, W, H);

            // Интегральные изображения сумм и квадратов сумм
            double[] integral = new double[(W + 1) * (H + 1)];
            double[] integralSq = new double[(W + 1) * (H + 1)];
            for (int y = 0; y < H; y++)
            {
                double rowSum = 0;
                double rowSumSq = 0;
                int rowOffset = y * W;
                int integRow = (y + 1) * (W + 1);
                int integPrevRow = y * (W + 1);
                for (int x = 0; x < W; x++)
                {
                    double v = grayF[rowOffset + x];
                    rowSum += v;
                    rowSumSq += v * v;
                    integral[integRow + x + 1] = integral[integPrevRow + x + 1] + rowSum;
                    integralSq[integRow + x + 1] = integralSq[integPrevRow + x + 1] + rowSumSq;
                }
            }

            // Прореженные координаты шаблона для грубого прохода
            int sample = 2;
            var sampleXs = new List<int>();
            var sampleYs = new List<int>();
            var sampleVals = new List<double>();
            for (int ty = 0; ty < h; ty += sample)
            {
                int trow = ty * w;
                for (int tx = 0; tx < w; tx += sample)
                {
                    sampleXs.Add(tx);
                    sampleYs.Add(ty);
                    sampleVals.Add(grayT[trow + tx]);
                }
            }
            int sampledCount = sampleVals.Count;
            double sampleScale = templateCount / (double)sampledCount;

            // Грубый проход: кандидаты с оценкой выше порога - запаса
            double coarseCut = Math.Max(0.10, threshold - 0.20);
            var candidates = new List<KeyValuePair<long, double>>(); // ключ: y * W + x
            int stride = 2;
            for (int y = 0; y <= H - h; y += stride)
            {
                for (int x = 0; x <= W - w; x += stride)
                {
                    double winSum = WindowSum(integral, W, x, y, w, h);
                    double winSumSq = WindowSum(integralSq, W, x, y, w, h);
                    double meanI = winSum / templateCount;
                    double varI = winSumSq / templateCount - meanI * meanI;
                    if (varI <= 1e-6)
                        continue;
                    double cross = 0;
                    for (int s = 0; s < sampledCount; s++)
                    {
                        cross += sampleVals[s] * grayF[(y + sampleYs[s]) * W + x + sampleXs[s]];
                    }
                    double crossFull = cross * sampleScale;
                    double stdI = Math.Sqrt(varI);
                    double score = (crossFull - templateCount * meanT * meanI) / (templateCount * stdT * stdI);
                    if (score > coarseCut)
                    {
                        candidates.Add(new KeyValuePair<long, double>((long)y * W + x, score));
                    }
                }
            }

            // Точный проход вокруг кандидатов (с соседями ±2 пикселя)
            var exact = new Dictionary<long, double>();
            int radius = 2;
            foreach (var candidate in candidates)
            {
                int cy = (int)(candidate.Key / W);
                int cx = (int)(candidate.Key % W);
                for (int dy = -radius; dy <= radius; dy++)
                {
                    int y = cy + dy;
                    if (y < 0 || y > H - h) continue;
                    for (int dx = -radius; dx <= radius; dx++)
                    {
                        int x = cx + dx;
                        if (x < 0 || x > W - w) continue;
                        long key = (long)y * W + x;
                        if (exact.ContainsKey(key)) continue;

                        double winSum = WindowSum(integral, W, x, y, w, h);
                        double winSumSq = WindowSum(integralSq, W, x, y, w, h);
                        double meanI = winSum / templateCount;
                        double varI = winSumSq / templateCount - meanI * meanI;
                        if (varI <= 1e-6) { exact[key] = 0; continue; }
                        double cross = 0;
                        double stdI = Math.Sqrt(varI);
                        for (int ty = 0; ty < h; ty++)
                        {
                            int trow = ty * w;
                            int frow = (y + ty) * W + x;
                            for (int tx = 0; tx < w; tx++)
                                cross += grayT[trow + tx] * grayF[frow + tx];
                        }
                        double score = (cross - templateCount * meanT * meanI) / (templateCount * stdT * stdI);
                        if (score > 1.0) score = 1.0;
                        if (score < -1.0) score = -1.0;
                        exact[key] = score;
                    }
                }
            }

            // Отбор и non-maximum suppression
            var sorted = new List<Match>();
            foreach (var kv in exact)
            {
                if (kv.Value >= threshold)
                {
                    int y = (int)(kv.Key / W);
                    int x = (int)(kv.Key % W);
                    sorted.Add(new Match { X = x, Y = y, Width = w, Height = h, Score = kv.Value });
                }
            }
            sorted.Sort((a, b) => b.Score.CompareTo(a.Score));
            var accepted = new List<Match>();
            foreach (var m in sorted)
            {
                if (accepted.Count >= maxResults) break;
                bool overlaps = false;
                foreach (var a in accepted)
                {
                    if (Math.Abs(a.X - m.X) < w && Math.Abs(a.Y - m.Y) < h)
                    {
                        overlaps = true;
                        break;
                    }
                }
                if (!overlaps)
                    accepted.Add(m);
            }
            return accepted;
        }

        public static Match? FindBest(BitmapImage frame, BitmapImage template, double threshold)
        {
            var all = FindAll(frame, template, threshold, 1);
            return all.Count > 0 ? all[0] : null;
        }

        private static double WindowSum(double[] integral, int frameWidth, int x, int y, int w, int h)
        {
            int W1 = frameWidth + 1;
            int x2 = x + w, y2 = y + h;
            return integral[y2 * W1 + x2]
                 - integral[y * W1 + x2]
                 - integral[y2 * W1 + x]
                 + integral[y * W1 + x];
        }
    }
}
