using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using AmazingBot.Config;
using AmazingBot.Core;
using AmazingBot.Input;
using AmazingBot.Vision;

namespace AmazingBot.App
{
    /// <summary>
    /// Интерфейс для циклов профессий: шахта, рыбалка удочкой, рыбалка сетью.
    /// </summary>
    public sealed class MainForm : Form
    {
        private const int WizLogMaxLines = 600;

        private readonly string _configPath;
        private BotConfig _cfg;
        private readonly Logger _log;
        private Bot? _bot;
        private OreEspOverlayForm? _oreEspOverlay;
        private bool _botRunning;

        private TabControl _tabs = null!;
        private Label _statusLabel = null!;

        // Главная
        private RichTextBox _logBox = null!;
        private Button _startButton = null!;
        private Button _stopButton = null!;
        private CheckBox _antiAfkCheck = null!;

        // Маршруты
        private ComboBox _routeNameCombo = null!;
        private ListBox _routesList = null!;
        private Label _recordingHint = null!;
        private Button _recordButton = null!;
        private bool _recording;

        // Настройки
        private TextBox _windowTitleBox = null!;
        private NumericUpDown _afkIntervalBox = null!;
        private TextBox _afkKeysBox = null!;
        private ComboBox _logLevelCombo = null!;
        private ComboBox _professionModeCombo = null!;
        private TextBox _professionRouteBox = null!;
        private NumericUpDown _professionDelayBox = null!;

        public MainForm()
        {
            _configPath = ConfigService.FindConfigPath(null);
            _cfg = ConfigService.Load(_configPath, null, out List<string> problems);
            _log = new Logger(_cfg.LogsDir) { MinimumLevel = ParseLogLevel(_cfg.LogLevel) };
            _log.LineWritten += OnLogLine;
            foreach (string problem in problems)
                _log.Warn("Конфиг: " + problem);
            _log.Info("AmazingBot запущен. Конфиг: " + Path.GetFullPath(_configPath));

            Text = "AmazingBot — Амазинг Онлайн";
            Size = new Size(1024, 680);
            MinimumSize = new Size(900, 600);
            StartPosition = FormStartPosition.CenterScreen;

            BuildUi();
            ApplyTheme();
            LoadConfigIntoControls();
            LoadRoutesList();
            SetStatus("остановлен");

            FormClosing += OnFormClosing;
        }

        // ==================== Построение интерфейса ====================

        private void BuildUi()
        {
            _tabs = new TabControl { Dock = DockStyle.Fill };
            _tabs.TabPages.Add(BuildMainTab());
            _tabs.TabPages.Add(BuildRoutesTab());
            _tabs.TabPages.Add(BuildSettingsTab());

            Panel statusPanel = new Panel { Dock = DockStyle.Bottom, Height = 32, Padding = new Padding(12, 5, 12, 5), BackColor = Color.White };
            _statusLabel = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Text = "●  остановлен", ForeColor = Color.FromArgb(74, 88, 108) };
            Label panicHint = new Label
            {
                Dock = DockStyle.Right,
                Width = 250,
                TextAlign = ContentAlignment.MiddleRight,
                Text = "F12  ·  экстренная остановка",
                ForeColor = Color.Firebrick
            };
            statusPanel.Controls.Add(_statusLabel);
            statusPanel.Controls.Add(panicHint);

            var header = new Panel { Dock = DockStyle.Top, Height = 58, BackColor = Color.FromArgb(30, 45, 72), Padding = new Padding(16, 7, 16, 7) };
            var brand = new Label
            {
                Dock = DockStyle.Left,
                Width = 310,
                Text = "AMAZINGBOT   ·   ПРОФЕССИИ",
                ForeColor = Color.White,
                Font = new Font("Segoe UI Semibold", 15f, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft
            };
            var subtitle = new Label
            {
                Dock = DockStyle.Fill,
                Text = "Шахта · рыбалка удочкой / сетью",
                ForeColor = Color.FromArgb(190, 205, 225),
                Font = new Font("Segoe UI", 9.5f),
                TextAlign = ContentAlignment.MiddleRight
            };
            header.Controls.Add(subtitle);
            header.Controls.Add(brand);

            Controls.Add(_tabs);
            Controls.Add(statusPanel);
            Controls.Add(header);
        }

        private TabPage BuildMainTab()
        {
            var page = new TabPage("Главная");
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                Padding = new Padding(10)
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 120));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 96));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 128));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            // --- Блок запуска
            var runGroup = new GroupBox { Text = "Запуск", Dock = DockStyle.Fill, Padding = new Padding(10) };
            var runFlow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
            _startButton = new Button
            {
                Text = "▶  ЗАПУСТИТЬ БОТА",
                Width = 280,
                Height = 64,
                Font = new Font(Font.FontFamily, 14f, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = Color.ForestGreen,
                FlatStyle = FlatStyle.Flat,
                Margin = new Padding(4, 12, 12, 4)
            };
            _startButton.Click += async (s, e) => await StartBotAsync();
            _stopButton = new Button
            {
                Text = "■  СТОП",
                Width = 180,
                Height = 64,
                Font = new Font(Font.FontFamily, 14f, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = Color.Firebrick,
                FlatStyle = FlatStyle.Flat,
                Enabled = false,
                Margin = new Padding(4, 12, 4, 4)
            };
            _stopButton.Click += (s, e) => StopBot();
            runFlow.Controls.Add(_startButton);
            runFlow.Controls.Add(_stopButton);
            runGroup.Controls.Add(runFlow);

            var descGroup = new GroupBox { Text = "Режимы работы", Dock = DockStyle.Fill, Padding = new Padding(8) };
            var desc = new Label
            {
                Dock = DockStyle.Fill,
                Text = "Шахта · рыбалка удочкой · рыбалка сетью. Бот повторяет записанный цикл. Экранный подсветчик ищет руды по образцу, но не видит их сквозь стены. F12 — стоп бота, F11 — закрыть подсветчик."
            };
            descGroup.Controls.Add(desc);

            var quickGroup = new GroupBox { Text = "Поведение", Dock = DockStyle.Fill, Padding = new Padding(10) };
            var quickFlow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = true };
            _antiAfkCheck = new CheckBox { Text = "Анти-афк", AutoSize = true, Margin = new Padding(4, 12, 20, 4), Checked = true };
            var saveQuick = new Button { Text = "Сохранить", Width = 120, Height = 34, Margin = new Padding(4, 8, 4, 4) };
            saveQuick.Click += (s, e) => SaveAllSettings();
            var oreOverlayButton = new Button { Text = "Подсветка руды", Width = 150, Height = 34, Margin = new Padding(12, 8, 4, 4) };
            oreOverlayButton.Click += (s, e) => ShowOreOverlay();
            quickFlow.Controls.Add(_antiAfkCheck);
            quickFlow.Controls.Add(saveQuick);
            quickFlow.Controls.Add(oreOverlayButton);
            quickGroup.Controls.Add(quickFlow);

            // --- Журнал
            var logGroup = new GroupBox { Text = "Журнал работы", Dock = DockStyle.Fill, Padding = new Padding(8) };
            _logBox = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                BackColor = Color.FromArgb(24, 24, 24),
                ForeColor = Color.Gainsboro,
                Font = new Font("Consolas", 9.5f),
                BorderStyle = BorderStyle.None
            };
            var clearLog = new Button { Text = "Очистить журнал", Dock = DockStyle.Bottom, Height = 26 };
            clearLog.Click += (s, e) => _logBox.Clear();
            logGroup.Controls.Add(_logBox);
            logGroup.Controls.Add(clearLog);

            layout.Controls.Add(runGroup, 0, 0);
            layout.Controls.Add(descGroup, 0, 1);
            layout.Controls.Add(quickGroup, 0, 2);
            layout.Controls.Add(logGroup, 0, 3);
            page.Controls.Add(layout);
            return page;
        }

        private TabPage BuildRoutesTab()
        {
            var page = new TabPage("Маршруты");
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Padding = new Padding(10)
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 150));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var recordGroup = new GroupBox { Text = "Записать новый маршрут", Dock = DockStyle.Fill, Padding = new Padding(10) };
            var recordLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3 };
            recordLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 260));
            recordLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            recordLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            recordLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            recordLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            recordLayout.Controls.Add(new Label { Text = "Имя цикла:", TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
            _routeNameCombo = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDown };
            _routeNameCombo.Items.Add("mine_cycle");
            _routeNameCombo.Items.Add("fishing_rod_cycle");
            _routeNameCombo.Items.Add("fishing_net_cycle");
            _routeNameCombo.SelectedIndex = 0;
            recordLayout.Controls.Add(_routeNameCombo, 1, 0);

            _recordButton = new Button
            {
                Text = "● Начать запись (F10 — закончить запись)",
                Dock = DockStyle.Fill,
                ForeColor = Color.White,
                BackColor = Color.DarkOrange,
                FlatStyle = FlatStyle.Flat
            };
            _recordButton.Click += (s, e) => StartRecording();
            recordLayout.Controls.Add(_recordButton, 0, 1);
            recordLayout.Controls.Add(new Label { Text = "Запишите полный цикл; повторяемый маршрут должен заканчиваться в точке старта", TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.DimGray }, 1, 1);

            _recordingHint = new Label { Text = "", Visible = false, ForeColor = Color.Firebrick, TextAlign = ContentAlignment.MiddleLeft };
            recordLayout.Controls.Add(_recordingHint, 0, 2);
            recordLayout.SetColumnSpan(_recordingHint, 2);

            recordGroup.Controls.Add(recordLayout);

            var listGroup = new GroupBox { Text = "Сохранённые маршруты (папка routes/)", Dock = DockStyle.Fill, Padding = new Padding(8) };
            _routesList = new ListBox { Dock = DockStyle.Fill, Font = new Font("Consolas", 10f) };
            var refreshRoutes = new Button { Text = "Обновить список", Dock = DockStyle.Bottom, Height = 30 };
            refreshRoutes.Click += (s, e) => LoadRoutesList();
            listGroup.Controls.Add(_routesList);
            listGroup.Controls.Add(refreshRoutes);

            layout.Controls.Add(recordGroup, 0, 0);
            layout.Controls.Add(listGroup, 0, 1);
            page.Controls.Add(layout);
            return page;
        }

        private TabPage BuildSettingsTab()
        {
            var page = new TabPage("Настройки");
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 9,
                Padding = new Padding(10)
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 300));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < 8; i++)
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            int row = 0;
            AddRow(layout, ref row, "Заголовок окна игры содержит:", _windowTitleBox = new TextBox { Dock = DockStyle.Fill });
            AddRow(layout, ref row, "Профессия:", _professionModeCombo = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList });
            _professionModeCombo.Items.AddRange(new object[] { "Шахта", "Рыбалка — удочка", "Рыбалка — сеть" });
            AddRow(layout, ref row, "Маршрут цикла:", _professionRouteBox = new TextBox { Dock = DockStyle.Fill });
            AddRow(layout, ref row, "Пауза между циклами (мс):", _professionDelayBox = new NumericUpDown { Dock = DockStyle.Fill, Minimum = 0, Maximum = 60000, Increment = 250, Value = 1000 });
            AddRow(layout, ref row, "Анти-афк, раз в (сек):", _afkIntervalBox = new NumericUpDown { Dock = DockStyle.Fill, Minimum = 5, Maximum = 3600, Value = 60 });
            AddRow(layout, ref row, "Анти-афк, клавиши (через +):", _afkKeysBox = new TextBox { Dock = DockStyle.Fill });
            AddRow(layout, ref row, "Уровень лога:", _logLevelCombo = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList });
            _logLevelCombo.Items.AddRange(new object[] { "Info", "Debug", "Warn", "Error" });
            _logLevelCombo.SelectedIndex = 0;

            _professionModeCombo.SelectedIndexChanged += (s, e) =>
            {
                string current = _professionRouteBox.Text.Trim();
                if (string.IsNullOrEmpty(current) || current == "mine_cycle" || current == "fishing_rod_cycle" || current == "fishing_net_cycle")
                    _professionRouteBox.Text = _professionModeCombo.SelectedIndex == 0 ? "mine_cycle"
                        : _professionModeCombo.SelectedIndex == 1 ? "fishing_rod_cycle"
                        : _professionModeCombo.SelectedIndex == 2 ? "fishing_net_cycle" : current;
            };

            var saveAll = new Button { Text = "💾  Сохранить настройки", Dock = DockStyle.Fill, Font = new Font(Font, FontStyle.Bold) };
            saveAll.Click += (s, e) => SaveAllSettings();
            layout.Controls.Add(saveAll, 0, row);
            layout.SetColumnSpan(saveAll, 2);

            var hint = new Label
            {
                Dock = DockStyle.Fill,
                Text = "Запишите один полный цикл во вкладке «Маршруты», выбрав mine_cycle, fishing_rod_cycle или fishing_net_cycle. Для безопасного повтора закончите в точке старта. Макрос не распознаёт случайную поклёвку; F12 — стоп."
            };
            row++;
            layout.Controls.Add(hint, 0, row);
            layout.SetColumnSpan(hint, 2);

            page.Controls.Add(layout);
            return page;
        }

        private void ApplyTheme()
        {
            BackColor = Color.FromArgb(242, 245, 250);
            ForeColor = Color.FromArgb(39, 50, 67);
            Font = new Font("Segoe UI", 9.5f);
            _tabs.Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold);
            _tabs.Padding = new Point(12, 5);
            foreach (TabPage tab in _tabs.TabPages)
            {
                tab.BackColor = Color.FromArgb(242, 245, 250);
                tab.Padding = new Padding(3);
                StyleControls(tab);
            }
            _statusLabel.Parent!.BackColor = Color.White;
        }

        private static void StyleControls(Control parent)
        {
            foreach (Control control in parent.Controls)
            {
                if (control is GroupBox group)
                {
                    group.ForeColor = Color.FromArgb(42, 59, 86);
                    group.Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold);
                }
                else if (control is Button button)
                {
                    button.FlatStyle = FlatStyle.Flat;
                    button.UseVisualStyleBackColor = false;
                    button.FlatAppearance.BorderColor = Color.FromArgb(205, 214, 227);
                    button.FlatAppearance.MouseOverBackColor = Color.FromArgb(229, 237, 248);
                    button.Font = new Font("Segoe UI Semibold", 9f, FontStyle.Bold);
                    if (button.Text.Contains("ЗАПУСТИТЬ", StringComparison.OrdinalIgnoreCase))
                    {
                        button.BackColor = Color.FromArgb(28, 132, 91);
                        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(23, 113, 78);
                        button.ForeColor = Color.White;
                    }
                    else if (button.Text.Contains("СТОП", StringComparison.OrdinalIgnoreCase))
                    {
                        button.BackColor = Color.FromArgb(184, 62, 68);
                        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(158, 48, 55);
                        button.ForeColor = Color.White;
                    }
                    else
                    {
                        button.UseVisualStyleBackColor = false;
                        button.BackColor = Color.White;
                        button.ForeColor = Color.FromArgb(42, 59, 86);
                    }
                }
                else if (control is TextBox || control is ComboBox || control is NumericUpDown)
                {
                    control.BackColor = Color.White;
                    control.ForeColor = Color.FromArgb(39, 50, 67);
                }
                else if (control is Label label && label.ForeColor == SystemColors.ControlText)
                {
                    label.ForeColor = Color.FromArgb(59, 72, 92);
                }
                if (control.HasChildren) StyleControls(control);
            }
        }

        private static void AddRow(TableLayoutPanel layout, ref int row, string label, Control control)
        {
            layout.Controls.Add(new Label { Text = label, TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, row);
            layout.Controls.Add(control, 1, row);
            row++;
        }

        // ==================== Журнал ====================

        private void OnLogLine(string line, LogLevel level)
        {
            if (IsDisposed) return;
            if (InvokeRequired)
            {
                try
                {
                    BeginInvoke(new Action(() => AppendLog(line, level)));
                }
                catch (Exception)
                {
                    // форма закрывается — игнорируем
                }
                return;
            }
            AppendLog(line, level);
        }

        private void AppendLog(string line, LogLevel level)
        {
            if (_logBox == null || _logBox.IsDisposed) return;
            Color color = Color.Gainsboro;
            switch (level)
            {
                case LogLevel.Debug: color = Color.DarkGray; break;
                case LogLevel.Info: color = Color.Gainsboro; break;
                case LogLevel.Warn: color = Color.Gold; break;
                case LogLevel.Error: color = Color.Tomato; break;
            }
            _logBox.SelectionStart = _logBox.TextLength;
            _logBox.SelectionLength = 0;
            _logBox.SelectionColor = color;
            _logBox.AppendText(line + Environment.NewLine);
            if (_logBox.Lines.Length > WizLogMaxLines)
            {
                int remove = _logBox.Text.IndexOf('\n');
                if (remove > 0)
                {
                    _logBox.SelectionStart = 0;
                    _logBox.SelectionLength = remove + 1;
                    _logBox.SelectionColor = color;
                    _logBox.SelectedText = string.Empty;
                }
            }
            _logBox.SelectionStart = _logBox.TextLength;
            _logBox.ScrollToCaret();
        }

        private void SetStatus(string text)
        {
            if (_statusLabel != null && !_statusLabel.IsDisposed)
                _statusLabel.Text = "Статус: " + text;
        }

        // ==================== Подсветка руды ====================

        private void ShowOreOverlay()
        {
            if (_oreEspOverlay != null && !_oreEspOverlay.IsDisposed)
            {
                _oreEspOverlay.Close();
                _oreEspOverlay = null;
                return;
            }

            string templateDir = Path.GetFullPath(_cfg.TemplatesDir);
            string templatePath = Path.Combine(templateDir, "ore.bmp");
            if (!File.Exists(templatePath))
            {
                MessageBox.Show(
                    "Для экранного поиска нужен файл templates/ore.bmp — небольшой уникальный фрагмент видимой руды.\n\n" +
                    "Сделайте снимок руды в игре, вырежьте участок и сохраните как BMP. В следующем диалоге выберите этот файл.\n" +
                    "Важно: этот поиск подсвечивает только руду, видимую на экране; через стены он не обнаруживает.",
                    "Настройка подсветки руды", MessageBoxButtons.OK, MessageBoxIcon.Information);
                using (var dialog = new OpenFileDialog
                {
                    Title = "Выберите BMP-образец руды",
                    Filter = "BMP image (*.bmp)|*.bmp",
                    CheckFileExists = true,
                    Multiselect = false
                })
                {
                    if (dialog.ShowDialog(this) != DialogResult.OK) return;
                    Directory.CreateDirectory(templateDir);
                    File.Copy(dialog.FileName, templatePath, true);
                }
            }

            _oreEspOverlay = new OreEspOverlayForm(_cfg.WindowTitleContains, templateDir, "ore", _log);
            _oreEspOverlay.FormClosed += (s, e) => _oreEspOverlay = null;
            _oreEspOverlay.Show();
        }

        // ==================== Запуск / остановка бота ====================

        private async Task StartBotAsync()
        {
            if (_botRunning) return;
            SaveAllSettings();
            string mode = (_cfg.Profession?.Mode ?? "mining").Trim().ToLowerInvariant();
            if (mode != "mining" && mode != "fishing_rod" && mode != "fishing_net")
            {
                MessageBox.Show("Доступны только режимы: шахта, рыбалка удочкой и рыбалка сетью.", "AmazingBot", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string route = (_cfg.Profession?.Route ?? string.Empty).Trim();
            if (route.Length == 0 || !File.Exists(RoutePlayer.RoutePath(_cfg.RoutesDir, route)))
            {
                MessageBox.Show(
                    "Не найден маршрут профессии «" + route + "». Во вкладке «Маршруты» запишите один полный цикл и завершите запись у точки старта.",
                    "Нужно записать цикл работы", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _tabs.SelectedIndex = 1;
                return;
            }

            ConfigService.Save(_configPath, _cfg, _log);

            Bot bot = new Bot(_cfg, _configPath, _log);
            _bot = bot;
            _botRunning = true;
            UpdateRunButtons();
            SetStatus("работает… (F12 — стоп)");

            Task<int> runTask = Task.Run(() => bot.Run(false));
            int code = await runTask.ConfigureAwait(true);
            OnBotFinished(code);
        }

        private void StopBot()
        {
            _bot?.RequestStop();
            SetStatus("останавливаю…");
            _log.Info("Запрошена остановка бота (кнопка «Стоп»)");
        }

        private void OnBotFinished(int code)
        {
            _botRunning = false;
            _bot = null;
            UpdateRunButtons();
            SetStatus("остановлен (код выхода " + code + ")");
            LoadRoutesList();
        }

        private void UpdateRunButtons()
        {
            _startButton.Enabled = !_botRunning && !_recording;
            _stopButton.Enabled = _botRunning;
            _recordButton.Enabled = !_recording && !_botRunning;
        }

        private void OnFormClosing(object? sender, FormClosingEventArgs e)
        {
            if (_oreEspOverlay != null && !_oreEspOverlay.IsDisposed)
            {
                _oreEspOverlay.Close();
                _oreEspOverlay = null;
            }
            if (_botRunning && _bot != null)
            {
                DialogResult answer = MessageBox.Show(
                    "Бот сейчас работает. Остановить его и выйти?",
                    "AmazingBot", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (answer == DialogResult.Yes)
                {
                    _bot.RequestStop();
                }
                else
                {
                    e.Cancel = true;
                }
            }
        }

        // ==================== Маршруты ====================

        private void StartRecording()
        {
            if (_recording) return;
            string name = (_routeNameCombo.Text ?? string.Empty).Trim();
            if (name.Length == 0)
            {
                MessageBox.Show("Введите имя маршрута.", "AmazingBot");
                return;
            }
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            name = name.Replace(' ', '_');
            _routeNameCombo.Text = name;

            var vision = CreateVision();
            if (!vision.Refresh())
            {
                MessageBox.Show(
                    "Окно игры не найдено. Запустите Amazing Online в оконном режиме.",
                    "AmazingBot", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            int originX = vision.ClientOriginX;
            int originY = vision.ClientOriginY;

            MessageBox.Show(
                "Запись маршрута «" + name + "» начнётся после нажатия ОК.\n\nЗапишите полный цикл работы. Для проф-режима закончите в той же точке, где начали; клики и клавиши запишутся автоматически.\nОкно игры не двигайте!\n\nЗакончить запись — F10.",
                "AmazingBot", MessageBoxButtons.OK, MessageBoxIcon.Information);

            _recording = true;
            UpdateRunButtons();
            _recordingHint.Text = "ИДЁТ ЗАПИСЬ маршрута «" + name + "»… F10 — закончить.";
            _recordingHint.Visible = true;
            SetStatus("запись маршрута «" + name + "»…");

            Thread recorderThread = new Thread(() =>
            {
                try
                {
                    var recorder = new RouteRecorder(_log);
                    RouteFile route = recorder.Record(name, originX, originY);
                    RoutePlayer.Save(_cfg.RoutesDir, route, _log);
                    int steps = route.Steps.Count;
                    BeginInvoke(new Action(() => FinishRecording(name, steps, null)));
                }
                catch (Exception ex)
                {
                    BeginInvoke(new Action(() => FinishRecording(name, 0, ex)));
                }
            })
            {
                IsBackground = true,
                Name = "RouteRecorderThread"
            };
            recorderThread.SetApartmentState(ApartmentState.STA);
            recorderThread.Start();
        }

        private void FinishRecording(string name, int stepCount, Exception? error)
        {
            _recording = false;
            UpdateRunButtons();
            _recordingHint.Visible = false;
            _recordingHint.Text = string.Empty;
            if (error != null)
            {
                _log.Exception("Ошибка записи маршрута «" + name + "»", error);
                MessageBox.Show("Ошибка записи: " + error.Message, "AmazingBot", MessageBoxButtons.OK, MessageBoxIcon.Error);
                SetStatus("ошибка записи маршрута");
                return;
            }
            _log.Info("Маршрут «" + name + "» записан: " + stepCount + " шагов");
            SetStatus("маршрут «" + name + "» сохранён (" + stepCount + " шагов)");
            LoadRoutesList();
        }

        private void LoadRoutesList()
        {
            if (_routesList == null || _routesList.IsDisposed) return;
            _routesList.Items.Clear();
            string dir = _cfg.RoutesDir;
            if (!Directory.Exists(dir))
            {
                _routesList.Items.Add("(папка routes/ ещё не создана)");
                return;
            }
            string[] files = Directory.GetFiles(dir, "*.json");
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            if (files.Length == 0)
            {
                _routesList.Items.Add("(пока нет записанных маршрутов)");
                return;
            }
            foreach (string file in files)
            {
                string name = Path.GetFileNameWithoutExtension(file);
                long size = new FileInfo(file).Length;
                _routesList.Items.Add(name.PadRight(24) + "  " + size + " байт");
            }
        }

        // ==================== Настройки ====================

        private void LoadConfigIntoControls()
        {
            _windowTitleBox.Text = _cfg.WindowTitleContains;
            string mode = (_cfg.Profession?.Mode ?? "mining").Trim().ToLowerInvariant();
            _professionModeCombo.SelectedIndex = mode == "fishing_rod" ? 1 : mode == "fishing_net" ? 2 : 0;
            _professionRouteBox.Text = _cfg.Profession?.Route ?? "mine_cycle";
            _professionDelayBox.Value = Math.Min(_professionDelayBox.Maximum, Math.Max(_professionDelayBox.Minimum, _cfg.Profession?.CycleDelayMs ?? 1000));
            _afkIntervalBox.Value = Math.Min(_afkIntervalBox.Maximum, Math.Max(_afkIntervalBox.Minimum, _cfg.AntiAfk.IntervalSeconds));
            _afkKeysBox.Text = string.Join(" + ", _cfg.AntiAfk.Keys);
            _antiAfkCheck.Checked = _cfg.AntiAfk.Enabled;
            int logIndex = _logLevelCombo.Items.IndexOf(_cfg.LogLevel);
            _logLevelCombo.SelectedIndex = logIndex >= 0 ? logIndex : 0;
        }

        private void SaveAllSettings()
        {
            _cfg.WindowTitleContains = _windowTitleBox.Text.Trim();
            if (_cfg.Profession == null) _cfg.Profession = new ProfessionConfig();
            _cfg.Profession.Mode = _professionModeCombo.SelectedIndex == 1 ? "fishing_rod"
                : _professionModeCombo.SelectedIndex == 2 ? "fishing_net" : "mining";
            _cfg.Profession.Route = _professionRouteBox.Text.Trim();
            _cfg.Profession.CycleDelayMs = (int)_professionDelayBox.Value;
            _cfg.AntiAfk.Enabled = _antiAfkCheck.Checked;
            _cfg.AntiAfk.IntervalSeconds = (int)_afkIntervalBox.Value;
            _cfg.AntiAfk.Keys = _afkKeysBox.Text
                .Split(new[] { '+', ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(k => k.Trim())
                .Where(k => k.Length > 0)
                .ToList();
            _cfg.LogLevel = _logLevelCombo.SelectedItem?.ToString() ?? "Info";

            _log.MinimumLevel = ParseLogLevel(_cfg.LogLevel);
            ConfigService.Save(_configPath, _cfg, _log);
            SetStatus("настройки сохранены");
        }

        // ==================== Утилиты ====================

        private VisionService CreateVision()
        {
            return new VisionService(_cfg.WindowTitleContains, Path.GetFullPath(_cfg.TemplatesDir), _log);
        }

        private static LogLevel ParseLogLevel(string? value)
        {
            switch ((value ?? "Info").Trim().ToLowerInvariant())
            {
                case "debug": return LogLevel.Debug;
                case "warn": return LogLevel.Warn;
                case "error": return LogLevel.Error;
                default: return LogLevel.Info;
            }
        }

    }
}
