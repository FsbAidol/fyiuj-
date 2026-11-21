using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using AmazingBot.Core;
using AmazingBot.Native;
using AmazingBot.Vision;

namespace AmazingBot.App
{
    /// <summary>
    /// Transparent screen-space ore highlighter. It recognizes a user-provided ore image
    /// in captured frames; it does not inspect game memory or detect ores behind walls.
    /// </summary>
    internal sealed class OreEspOverlayForm : Form
    {
        private readonly VisionService _vision;
        private readonly Logger _log;
        private readonly string _templateName;
        private readonly Timer _timer;
        private readonly double _threshold;
        private List<Match> _matches = new List<Match>();
        private bool _scanInProgress;
        private bool _f11WasDown;
        private bool _templateError;
        private string _status = "Поиск руды на изображении экрана…  F11 — закрыть";

        public OreEspOverlayForm(string windowTitle, string templatesDir, string templateName, Logger log)
        {
            _vision = new VisionService(windowTitle, templatesDir, log);
            _log = log;
            _templateName = templateName;
            _threshold = 0.82;
            _timer = new Timer { Interval = 700 };
            _timer.Tick += async (sender, args) => await ScanAsync();

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.Magenta;
            TransparencyKey = Color.Magenta;
            DoubleBuffered = true;
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x00000020; // WS_EX_TRANSPARENT: mouse input passes through
                cp.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW
                cp.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE
                return cp;
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _timer.Start();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _timer.Stop();
            _timer.Dispose();
            base.OnFormClosed(e);
        }

        private async Task ScanAsync()
        {
            bool f11 = NativeMethods.KeyDown(NativeMethods.VK_F11);
            if (f11 && !_f11WasDown)
            {
                Close();
                return;
            }
            _f11WasDown = f11;
            if (_scanInProgress || IsDisposed || _templateError) return;
            _scanInProgress = true;
            try
            {
                // Hide our overlay while capturing so its own boxes cannot be detected as ores.
                Hide();
                CapturedFrame? frame = await Task.Run(() =>
                {
                    if (!_vision.Refresh()) return null;
                    return _vision.Current;
                });
                if (IsDisposed) return;
                Show();
                TopMost = true;

                if (frame == null)
                {
                    _status = "Окно игры не найдено. F11 — закрыть";
                    _matches.Clear();
                    Invalidate();
                    return;
                }

                BitmapImage template;
                try
                {
                    template = _vision.LoadTemplate(_templateName);
                }
                catch (FileNotFoundException)
                {
                    _templateError = true;
                    _status = "Нет templates/" + _templateName + ".bmp — добавьте снимок руды. F11 — закрыть";
                    _log.Warn("Ore overlay: не найден шаблон " + _vision.TemplatePath(_templateName));
                    Invalidate();
                    return;
                }

                List<Match> found = await Task.Run(() => TemplateMatcher.FindAll(frame.Image, template, _threshold, 32));
                if (IsDisposed) return;
                _matches = found;
                _status = "Экранные совпадения: " + found.Count + " · F11 — закрыть · это не поиск сквозь стены";
                Bounds = new Rectangle(frame.OriginX, frame.OriginY, frame.Width, frame.Height);
                Invalidate();
            }
            catch (Exception ex)
            {
                _status = "Ошибка поиска руды: " + ex.Message;
                _log.Exception("Ошибка экранного поиска руды", ex);
                Invalidate();
            }
            finally
            {
                if (!IsDisposed)
                {
                    Show();
                    TopMost = true;
                }
                _scanInProgress = false;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
            using (var pen = new Pen(Color.Lime, 2f))
            using (var labelBrush = new SolidBrush(Color.FromArgb(220, 0, 0, 0)))
            using (var textBrush = new SolidBrush(Color.Lime))
            using (var font = new Font("Segoe UI", 9f, FontStyle.Bold))
            {
                foreach (Match match in _matches)
                {
                    var rect = new Rectangle(match.X, match.Y, match.Width, match.Height);
                    e.Graphics.DrawRectangle(pen, rect);
                    string label = "РУДА " + match.Score.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
                    SizeF size = e.Graphics.MeasureString(label, font);
                    float y = Math.Max(0, rect.Top - size.Height);
                    e.Graphics.FillRectangle(labelBrush, rect.Left, y, size.Width + 6, size.Height + 2);
                    e.Graphics.DrawString(label, font, textBrush, rect.Left + 3, y + 1);
                }
                if (!string.IsNullOrEmpty(_status))
                {
                    SizeF size = e.Graphics.MeasureString(_status, font);
                    e.Graphics.FillRectangle(labelBrush, 6, 6, size.Width + 12, size.Height + 8);
                    e.Graphics.DrawString(_status, font, textBrush, 12, 10);
                }
            }
        }
    }
}
