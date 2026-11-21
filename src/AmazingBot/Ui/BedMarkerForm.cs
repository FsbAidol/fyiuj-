using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using AmazingBot.Vision;

namespace AmazingBot.Ui
{
    /// <summary>
    /// Lets the user mark garden centers on a captured game frame. This avoids relying on
    /// GetCursorPos while the game has locked the mouse to the center of the screen.
    /// </summary>
    public sealed class BedMarkerForm : Form
    {
        private readonly BitmapImage _source;
        private readonly Bitmap _bitmap;
        private readonly PictureBox _preview;
        private readonly ListBox _pointsList;
        private readonly Label _hint;
        private readonly List<Point> _points = new List<Point>();

        public IReadOnlyList<Point> Points => _points;

        public BedMarkerForm(BitmapImage source, IEnumerable<Point>? initialPoints = null)
        {
            _source = source;
            _bitmap = ToBitmap(source);
            Text = "Выбор грядок — нажимайте по центру каждой грядки";
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(820, 560);
            Size = new Size(1180, 780);
            BackColor = Color.FromArgb(242, 245, 250);

            var header = new Panel { Dock = DockStyle.Top, Height = 76, BackColor = Color.FromArgb(30, 45, 72), Padding = new Padding(16, 8, 16, 8) };
            var title = new Label
            {
                Dock = DockStyle.Top,
                Height = 28,
                Text = "Отметьте грядки на кадре",
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 15f, FontStyle.Bold)
            };
            _hint = new Label
            {
                Dock = DockStyle.Fill,
                Text = "ЛКМ по центру каждой грядки добавляет точку. Правый клик по метке удаляет её. Это не требует перемещать курсор в игровом окне.",
                ForeColor = Color.FromArgb(205, 218, 237),
                Font = new Font("Segoe UI", 9.5f)
            };
            header.Controls.Add(_hint);
            header.Controls.Add(title);

            var content = new SplitContainer
            {
                Dock = DockStyle.Fill,
                FixedPanel = FixedPanel.Panel2,
                BackColor = Color.FromArgb(242, 245, 250)
            };
            _preview = new PictureBox
            {
                Dock = DockStyle.Fill,
                Image = _bitmap,
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.FromArgb(20, 27, 38),
                Cursor = Cursors.Cross
            };
            _preview.MouseClick += OnPreviewClick;
            _preview.Paint += OnPreviewPaint;
            content.Panel1.Padding = new Padding(8);
            content.Panel1.Controls.Add(_preview);

            var side = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(8) };
            side.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            side.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            side.Controls.Add(new Label { Text = "Сохранённые точки", Dock = DockStyle.Fill, Font = new Font("Segoe UI", 10f, FontStyle.Bold), ForeColor = Color.FromArgb(42, 59, 86), TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
            _pointsList = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false, Font = new Font("Consolas", 9.5f) };
            side.Controls.Add(_pointsList, 0, 1);
            content.Panel2.Controls.Add(side);

            var footer = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 50,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(8),
                BackColor = Color.White
            };
            var cancel = new Button { Text = "Отмена", Width = 96, Height = 30, DialogResult = DialogResult.Cancel };
            var accept = new Button { Text = "Сохранить точки", Width = 150, Height = 30, DialogResult = DialogResult.OK };
            var undo = new Button { Text = "Убрать последнюю", Width = 145, Height = 30 };
            var clear = new Button { Text = "Очистить", Width = 100, Height = 30 };
            undo.Click += (s, e) => RemoveLast();
            clear.Click += (s, e) =>
            {
                if (_points.Count == 0) return;
                if (MessageBox.Show(this, "Удалить все " + _points.Count + " отмеченных точек? Это не затронет config.json, пока вы не нажмёте «Сохранить точки».", "Очистить отметки", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
                    ClearPoints();
            };
            accept.Click += (s, e) =>
            {
                if (_points.Count == 0)
                {
                    DialogResult = DialogResult.None;
                    MessageBox.Show(this, "Сначала отметьте хотя бы одну грядку.", "Выбор грядок", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            };
            footer.Controls.Add(cancel);
            footer.Controls.Add(accept);
            footer.Controls.Add(undo);
            footer.Controls.Add(clear);

            Controls.Add(content);
            Controls.Add(footer);
            Controls.Add(header);
            if (initialPoints != null)
                foreach (Point point in initialPoints)
                    _points.Add(point);
            RefreshPointList();
            Shown += (s, e) =>
            {
                int width = content.ClientSize.Width;
                int desired = width - 230;
                if (desired < 400 || desired >= width - 25) return;
                content.SplitterDistance = desired;
                content.Panel1MinSize = 400;
                content.Panel2MinSize = 190;
            };
            AcceptButton = accept;
            CancelButton = cancel;
        }

        private void OnPreviewClick(object? sender, MouseEventArgs e)
        {
            if (!TryMapToImage(e.Location, out Point imagePoint)) return;
            if (e.Button == MouseButtons.Right)
            {
                int nearest = -1;
                double distance = 28.0 * 28.0;
                for (int i = 0; i < _points.Count; i++)
                {
                    double dx = _points[i].X - imagePoint.X;
                    double dy = _points[i].Y - imagePoint.Y;
                    double d = dx * dx + dy * dy;
                    if (d < distance) { nearest = i; distance = d; }
                }
                if (nearest >= 0) RemoveAt(nearest);
                return;
            }
            if (e.Button != MouseButtons.Left) return;

            int duplicateIndex = _points.FindIndex(p => DistanceSquared(p, imagePoint) < 24L * 24L);
            if (duplicateIndex >= 0)
            {
                _hint.Text = "Эта точка слишком близко к уже отмеченной. Правый клик удаляет метку; кликните по центру другой грядки.";
                return;
            }
            _points.Add(imagePoint);
            RefreshPointList();
        }

        private bool TryMapToImage(Point viewPoint, out Point imagePoint)
        {
            imagePoint = Point.Empty;
            float scale = Math.Min((float)_preview.ClientSize.Width / _source.Width, (float)_preview.ClientSize.Height / _source.Height);
            if (scale <= 0) return false;
            float drawWidth = _source.Width * scale;
            float drawHeight = _source.Height * scale;
            float left = (_preview.ClientSize.Width - drawWidth) / 2f;
            float top = (_preview.ClientSize.Height - drawHeight) / 2f;
            if (viewPoint.X < left || viewPoint.Y < top || viewPoint.X >= left + drawWidth || viewPoint.Y >= top + drawHeight)
                return false;
            int x = (int)((viewPoint.X - left) / scale);
            int y = (int)((viewPoint.Y - top) / scale);
            if (x < 0 || y < 0 || x >= _source.Width || y >= _source.Height) return false;
            imagePoint = new Point(x, y);
            return true;
        }

        private void OnPreviewPaint(object? sender, PaintEventArgs e)
        {
            float scale = Math.Min((float)_preview.ClientSize.Width / _source.Width, (float)_preview.ClientSize.Height / _source.Height);
            if (scale <= 0) return;
            float left = (_preview.ClientSize.Width - _source.Width * scale) / 2f;
            float top = (_preview.ClientSize.Height - _source.Height * scale) / 2f;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var pen = new Pen(Color.Lime, 3f))
            using (var fill = new SolidBrush(Color.FromArgb(205, 25, 153, 93)))
            using (var labelBrush = new SolidBrush(Color.White))
            using (var labelFont = new Font("Segoe UI", 10f, FontStyle.Bold))
            {
                for (int i = 0; i < _points.Count; i++)
                {
                    float x = left + _points[i].X * scale;
                    float y = top + _points[i].Y * scale;
                    float radius = Math.Max(8f, 12f * Math.Min(1.5f, scale));
                    e.Graphics.FillEllipse(fill, x - radius, y - radius, radius * 2, radius * 2);
                    e.Graphics.DrawEllipse(pen, x - radius, y - radius, radius * 2, radius * 2);
                    string number = (i + 1).ToString();
                    SizeF size = e.Graphics.MeasureString(number, labelFont);
                    e.Graphics.DrawString(number, labelFont, labelBrush, x - size.Width / 2, y - size.Height / 2);
                }
            }
        }

        private static Bitmap ToBitmap(BitmapImage image)
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

        private void RemoveLast()
        {
            if (_points.Count > 0) RemoveAt(_points.Count - 1);
        }

        private void ClearPoints()
        {
            _points.Clear();
            RefreshPointList();
        }

        private void RemoveAt(int index)
        {
            _points.RemoveAt(index);
            RefreshPointList();
        }

        private void RefreshPointList()
        {
            _pointsList.Items.Clear();
            for (int i = 0; i < _points.Count; i++)
                _pointsList.Items.Add("bed" + (i + 1) + "   X " + _points[i].X + "   Y " + _points[i].Y);
            _hint.Text = _points.Count == 0
                ? "ЛКМ по центру каждой грядки добавляет точку. Правый клик по метке удаляет её."
                : "Отмечено грядок: " + _points.Count + ". Проверьте номера на всех грядках; лишнюю метку удалите правым кликом.";
            _preview.Invalidate();
        }

        private static long DistanceSquared(Point a, Point b)
        {
            long dx = (long)a.X - b.X;
            long dy = (long)a.Y - b.Y;
            return dx * dx + dy * dy;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _bitmap.Dispose();
            base.Dispose(disposing);
        }
    }
}
