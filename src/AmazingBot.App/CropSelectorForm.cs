using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using AmazingBot.Vision;

namespace AmazingBot.App
{
    /// <summary>Lets the user drag over a screenshot to teach one visual template.</summary>
    internal sealed class CropSelectorForm : Form
    {
        private readonly PictureBox _picture;
        private readonly Bitmap _bitmap;
        private readonly BitmapImage _source;
        private Point _dragStart;
        private Rectangle _selection;
        private bool _dragging;

        public Rectangle SelectedCrop => _selection;

        public CropSelectorForm(BitmapImage source, string title)
        {
            _source = source;
            Text = "Образец распознавания — " + title;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;

            double scale = Math.Min(1.0, Math.Min(1000.0 / source.Width, 620.0 / source.Height));
            int viewWidth = Math.Max(1, (int)Math.Round(source.Width * scale));
            int viewHeight = Math.Max(1, (int)Math.Round(source.Height * scale));
            _bitmap = ToBitmap(source);
            _picture = new PictureBox
            {
                Image = _bitmap,
                SizeMode = PictureBoxSizeMode.StretchImage,
                Width = viewWidth,
                Height = viewHeight,
                Cursor = Cursors.Cross,
                BorderStyle = BorderStyle.FixedSingle
            };
            _picture.MouseDown += OnMouseDown;
            _picture.MouseMove += OnMouseMove;
            _picture.MouseUp += OnMouseUp;
            _picture.Paint += OnPaint;

            var instruction = new Label
            {
                Text = "Зажмите левую кнопку и обведите небольшой уникальный значок/надпись. Не захватывайте фон и соседние элементы.",
                AutoSize = false,
                Width = viewWidth,
                Height = 42,
                TextAlign = ContentAlignment.MiddleLeft
            };
            var panel = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 42,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(4)
            };
            var cancel = new Button { Text = "Отмена", Width = 90, DialogResult = DialogResult.Cancel };
            var ok = new Button { Text = "Сохранить образец", Width = 150, DialogResult = DialogResult.OK };
            ok.Click += (s, e) =>
            {
                if (_selection.Width < 8 || _selection.Height < 8)
                {
                    DialogResult = DialogResult.None;
                    MessageBox.Show(this, "Выделите область не меньше 8×8 пикселей.", "Образец", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            };
            panel.Controls.Add(cancel);
            panel.Controls.Add(ok);
            var content = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true,
                Padding = new Padding(8)
            };
            content.Controls.Add(instruction);
            content.Controls.Add(_picture);
            Controls.Add(content);
            Controls.Add(panel);
            ClientSize = new Size(Math.Min(viewWidth + 24, 1024), Math.Min(viewHeight + 82, 700));
            AcceptButton = ok;
            CancelButton = cancel;
        }

        private void OnMouseDown(object? sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            _dragStart = ToSource(e.Location);
            _selection = new Rectangle(_dragStart, Size.Empty);
            _dragging = true;
            _picture.Invalidate();
        }

        private void OnMouseMove(object? sender, MouseEventArgs e)
        {
            if (!_dragging) return;
            _selection = RectFromPoints(_dragStart, ToSource(e.Location));
            _picture.Invalidate();
        }

        private void OnMouseUp(object? sender, MouseEventArgs e)
        {
            if (!_dragging) return;
            _selection = RectFromPoints(_dragStart, ToSource(e.Location));
            _dragging = false;
            _picture.Invalidate();
        }

        private Point ToSource(Point point)
        {
            int x = Math.Max(0, Math.Min(_picture.Width - 1, point.X));
            int y = Math.Max(0, Math.Min(_picture.Height - 1, point.Y));
            return new Point(x * _source.Width / Math.Max(1, _picture.Width), y * _source.Height / Math.Max(1, _picture.Height));
        }

        private static Rectangle RectFromPoints(Point a, Point b)
        {
            return Rectangle.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
        }

        private void OnPaint(object? sender, PaintEventArgs e)
        {
            if (_selection.Width <= 0 || _selection.Height <= 0) return;
            float sx = (float)_picture.Width / _source.Width;
            float sy = (float)_picture.Height / _source.Height;
            var rect = new RectangleF(_selection.X * sx, _selection.Y * sy, _selection.Width * sx, _selection.Height * sy);
            using (var pen = new Pen(Color.Lime, 2f)) e.Graphics.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);
            using (var brush = new SolidBrush(Color.FromArgb(35, Color.Lime))) e.Graphics.FillRectangle(brush, rect);
        }

        internal static Bitmap ToBitmap(BitmapImage image)
        {
            var bitmap = new Bitmap(image.Width, image.Height, PixelFormat.Format32bppArgb);
            var data = bitmap.LockBits(new Rectangle(0, 0, image.Width, image.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                for (int y = 0; y < image.Height; y++)
                    Marshal.Copy(image.Pixels, y * image.Width * 4, IntPtr.Add(data.Scan0, y * data.Stride), image.Width * 4);
            }
            finally { bitmap.UnlockBits(data); }
            return bitmap;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _bitmap.Dispose();
            base.Dispose(disposing);
        }
    }
}
