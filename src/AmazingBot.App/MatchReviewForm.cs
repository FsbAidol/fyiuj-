using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using AmazingBot.Vision;

namespace AmazingBot.App
{
    /// <summary>Shows auto-detected garden bed candidates before accepting them.</summary>
    internal sealed class MatchReviewForm : Form
    {
        private readonly PictureBox _picture;
        private readonly Bitmap _bitmap;
        private readonly BitmapImage _source;
        private readonly IReadOnlyList<Match> _matches;

        public MatchReviewForm(BitmapImage source, IReadOnlyList<Match> matches)
        {
            _source = source;
            _matches = matches;
            Text = "Проверка автоматически найденных грядок";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimumSize = new Size(700, 500);
            Size = new Size(1000, 740);
            _bitmap = CropSelectorForm.ToBitmap(source);
            _picture = new PictureBox
            {
                Dock = DockStyle.Fill,
                Image = _bitmap,
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.FromArgb(24, 31, 43)
            };
            _picture.Paint += OnPaint;

            var note = new Label
            {
                Dock = DockStyle.Top,
                Height = 54,
                Padding = new Padding(12, 8, 12, 8),
                Text = "Найдено совпадений: " + matches.Count + ". Зелёные рамки должны попадать по центру каждой нужной грядки. Если захватило фон или лишние объекты — отмените и выберите более уникальный образец.",
                BackColor = Color.White,
                ForeColor = Color.FromArgb(46, 56, 73)
            };
            var footer = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 48,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(8),
                BackColor = Color.WhiteSmoke
            };
            var cancel = new Button { Text = "Отмена", Width = 100, Height = 30, DialogResult = DialogResult.Cancel };
            var accept = new Button { Text = "Использовать эти грядки", Width = 190, Height = 30, DialogResult = DialogResult.OK };
            footer.Controls.Add(cancel);
            footer.Controls.Add(accept);
            Controls.Add(_picture);
            Controls.Add(note);
            Controls.Add(footer);
            AcceptButton = accept;
            CancelButton = cancel;
        }

        private void OnPaint(object? sender, PaintEventArgs e)
        {
            if (_picture.Image == null) return;
            float scale = Math.Min((float)_picture.ClientSize.Width / _source.Width, (float)_picture.ClientSize.Height / _source.Height);
            float drawnWidth = _source.Width * scale;
            float drawnHeight = _source.Height * scale;
            float offsetX = (_picture.ClientSize.Width - drawnWidth) / 2f;
            float offsetY = (_picture.ClientSize.Height - drawnHeight) / 2f;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var pen = new Pen(Color.Lime, 3f))
            using (var brush = new SolidBrush(Color.FromArgb(35, Color.Lime)))
            using (var font = new Font("Segoe UI", 10f, FontStyle.Bold))
            using (var textBrush = new SolidBrush(Color.White))
            using (var textBg = new SolidBrush(Color.FromArgb(190, 18, 45, 33)))
            {
                for (int i = 0; i < _matches.Count; i++)
                {
                    Match m = _matches[i];
                    float x = offsetX + m.X * scale;
                    float y = offsetY + m.Y * scale;
                    float w = Math.Max(8f, m.Width * scale);
                    float h = Math.Max(8f, m.Height * scale);
                    e.Graphics.FillRectangle(brush, x, y, w, h);
                    e.Graphics.DrawRectangle(pen, x, y, w, h);
                    string label = (i + 1).ToString();
                    SizeF labelSize = e.Graphics.MeasureString(label, font);
                    e.Graphics.FillRectangle(textBg, x, y, labelSize.Width + 8, labelSize.Height + 2);
                    e.Graphics.DrawString(label, font, textBrush, x + 4, y + 1);
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _bitmap.Dispose();
            base.Dispose(disposing);
        }
    }
}
