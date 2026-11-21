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
using AmazingBot.Native;
using AmazingBot.Ui;
using AmazingBot.Vision;

namespace AmazingBot.App
{
    /// <summary>
    /// Графическое меню AmazingBot. Вкладки: Главная, Грядки, Маршруты, Шаблоны, Настройки.
    /// </summary>
    public sealed class MainForm : Form
    {
        private const int WizLogMaxLines = 600;

        private readonly string _configPath;
        private BotConfig _cfg;
        private readonly Logger _log;
        private Bot? _bot;
        private bool _botRunning;

        private readonly List<BedPoint> _beds = new List<BedPoint>();

        // Общие элементы
        private TabControl _tabs = null!;
        private Label _statusLabel = null!;

        // Вкладка «Главная»
        private RichTextBox _logBox = null!;
        private Button _startButton = null!;
        private Button _stopButton = null!;
        private ComboBox _harvestModeCombo = null!;
        private NumericUpDown _intervalBox = null!;
        private CheckBox _autoDisableCheck = null!;
        private CheckBox _antiAfkCheck = null!;

        // Вкладка «Грядки»
        private ListView _bedsList = null!;
        // Bed picker writes image-relative points; no cursor hotkey polling is used in the GUI.

        // Вкладка «Маршруты»
        private ComboBox _routeNameCombo = null!;
        private ListBox _routesList = null!;
        private Label _recordingHint = null!;
        private Button _recordButton = null!;
        private bool _recording;

        // Вкладка «Мастер настройки»
        private ComboBox _templatePresetCombo = null!;
        private DataGridView _parasiteGrid = null!;

        // Вкладка «Шаблоны»
        private ListBox _templateResults = null!;

        // Вкладка «Настройки»
        private TextBox _windowTitleBox = null!;
        private NumericUpDown _thresholdBox = null!;
        private NumericUpDown _canCapacityBox = null!;
        private NumericUpDown _litersPerClickBox = null!;
        private NumericUpDown _litersPerBedBox = null!;
        private NumericUpDown _flaskLitersBox = null!;
        private TextBox _canKeyBox = null!;
        private TextBox _flaskKeyBox = null!;
        private TextBox _seedKeyBox = null!;
        private TextBox _fertilizerKeyBox = null!;
        private NumericUpDown _afkIntervalBox = null!;
        private TextBox _afkKeysBox = null!;
        private ComboBox _logLevelCombo = null!;

        public MainForm()
        {
            _configPath = ConfigService.FindConfigPath(null);
            _cfg = ConfigService.Load(_configPath, null, out List<string> problems);
            _log = new Logger(_cfg.LogsDir) { MinimumLevel = ParseLogLevel(_cfg.LogLevel) };
            _log.LineWritten += OnLogLine;
            foreach (string problem in problems)
                _log.Warn("Конфиг: " + problem);
            _log.Info("AmazingBot запущен. Конфиг: " + Path.GetFullPath(_configPath));

            _beds.AddRange(_cfg.Beds);

            Text = "AmazingBot — Амазинг Онлайн";
            Size = new Size(1024, 680);
            MinimumSize = new Size(900, 600);
            StartPosition = FormStartPosition.CenterScreen;

            BuildUi();
            ApplyTheme();
            LoadConfigIntoControls();
            LoadWizardMappings();
            ReloadBedsList();
            LoadRoutesList();
            SetStatus("остановлен");

            FormClosing += OnFormClosing;
        }

        // ==================== Построение интерфейса ====================

        private void BuildUi()
        {
            _tabs = new TabControl { Dock = DockStyle.Fill };
            _tabs.TabPages.Add(BuildMainTab());
            _tabs.TabPages.Add(BuildSetupTab());
            _tabs.TabPages.Add(BuildBedsTab());
            _tabs.TabPages.Add(BuildRoutesTab());
            _tabs.TabPages.Add(BuildTemplatesTab());
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
                Text = "AMAZINGBOT   ·   САД",
                ForeColor = Color.White,
                Font = new Font("Segoe UI Semibold", 15f, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft
            };
            var subtitle = new Label
            {
                Dock = DockStyle.Fill,
                Text = "Визуальное управление грядками",
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

            // --- Что делает бот
            var descGroup = new GroupBox { Text = "Что делает бот на каждой грядке", Dock = DockStyle.Fill, Padding = new Padding(8) };
            var desc = new Label
            {
                Dock = DockStyle.Fill,
                Text = "Открывает грядку → если готова: Собрать/Клонировать → если паразит: нужный химикат → если просит удобрение: удобряет → если низкая влажность: поливает ровно до нормы литром → если пусто: сажает семя.\nЗакончилось что-то (фляги, химикаты, удобрение, семена, сломанная лейка) — едет к машине и берёт из багажника. Респавн транспорта — занимает машину сам."
            };
            descGroup.Controls.Add(desc);

            // --- Быстрые настройки
            var quickGroup = new GroupBox { Text = "Быстрые настройки", Dock = DockStyle.Fill, Padding = new Padding(10) };
            var quickFlow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = true };

            var harvestPanel = new Panel { Width = 250, Height = 84 };
            harvestPanel.Controls.Add(new Label { Text = "Когда грядка готова:", Top = 0, Left = 0, AutoSize = true });
            _harvestModeCombo = new ComboBox { Top = 22, Left = 0, Width = 220, DropDownStyle = ComboBoxStyle.DropDownList };
            _harvestModeCombo.Items.Add("Собирать урожай");
            _harvestModeCombo.Items.Add("Клонировать");
            _harvestModeCombo.SelectedIndex = 0;
            harvestPanel.Controls.Add(_harvestModeCombo);

            var intervalPanel = new Panel { Width = 210, Height = 84 };
            intervalPanel.Controls.Add(new Label { Text = "Проверять грядки раз в (сек):", Top = 0, Left = 0, AutoSize = true });
            _intervalBox = new NumericUpDown { Top = 22, Left = 0, Width = 90, Minimum = 1, Maximum = 3600, Value = 20 };
            intervalPanel.Controls.Add(_intervalBox);

            var checksPanel = new Panel { Width = 330, Height = 84 };
            _autoDisableCheck = new CheckBox { Text = "Отключать бота, когда собраны ВСЕ грядки", Top = 4, Left = 0, Width = 330, Checked = true };
            _antiAfkCheck = new CheckBox { Text = "Анти-афк (не кикает за бездействие)", Top = 32, Left = 0, Width = 330, Checked = true };
            checksPanel.Controls.Add(_autoDisableCheck);
            checksPanel.Controls.Add(_antiAfkCheck);

            var saveQuick = new Button { Text = "Сохранить", Width = 120, Height = 34, Top = 18, Left = 0, Margin = new Padding(0, 22, 0, 0) };
            saveQuick.Click += (s, e) => SaveAllSettings();
            var saveQuickPanel = new Panel { Width = 130, Height = 84 };
            saveQuick.Top = 22;
            saveQuickPanel.Controls.Add(saveQuick);

            quickFlow.Controls.Add(harvestPanel);
            quickFlow.Controls.Add(intervalPanel);
            quickFlow.Controls.Add(checksPanel);
            quickFlow.Controls.Add(saveQuickPanel);
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

        private TabPage BuildSetupTab()
        {
            var page = new TabPage("Простая настройка");
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(10) };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 74));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 116));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));

            var intro = new Label
            {
                Dock = DockStyle.Fill,
                Text = "Мастер без ручного редактирования JSON. Сначала добавьте координаты грядок во вкладке «Грядки». Для распознавания откройте нужное состояние в игре, выберите его ниже и обведите значок на снимке. Бот сравнивает экран с этими образцами; он не угадывает неизвестные значки сам."
            };

            var sampleGroup = new GroupBox { Text = "1. Обучить распознаванию", Dock = DockStyle.Fill, Padding = new Padding(8) };
            var sampleFlow = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true, FlowDirection = FlowDirection.LeftToRight };
            _templatePresetCombo = new ComboBox { Width = 270, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(4, 10, 8, 4) };
            _templatePresetCombo.Items.AddRange(new object[]
            {
                new TemplatePreset("Растение есть / растёт", "crop_present"),
                new TemplatePreset("Пустая грядка", "bed_empty"),
                new TemplatePreset("Урожай готов", "crop_ready"),
                new TemplatePreset("Нужно удобрение", "needs_fertilizer"),
                new TemplatePreset("Мало воды", "moisture_low"),
                new TemplatePreset("Воды достаточно", "moisture_full"),
                new TemplatePreset("Кнопка «Собрать»", "btn_harvest"),
                new TemplatePreset("Кнопка «Клонировать»", "btn_clone")
            });
            _templatePresetCombo.SelectedIndex = 0;
            var captureSample = new Button { Text = "Снять образец с экрана…", AutoSize = true, Height = 34, Margin = new Padding(4, 8, 8, 4) };
            captureSample.Click += (s, e) => CaptureTemplateSample();
            var missing = new Label
            {
                AutoSize = false,
                Width = 420,
                Height = 48,
                Text = "Для жуков добавьте запись в таблице ниже, затем выделите его и нажмите «Снять образец жука».",
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.DimGray
            };
            sampleFlow.Controls.Add(_templatePresetCombo);
            sampleFlow.Controls.Add(captureSample);
            sampleFlow.Controls.Add(missing);
            sampleGroup.Controls.Add(sampleFlow);

            var bugsGroup = new GroupBox { Text = "2. Жуки / вредители — чем лечить и какая клавиша", Dock = DockStyle.Fill, Padding = new Padding(8) };
            var bugLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
            bugLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            bugLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            _parasiteGrid = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = true,
                AllowUserToDeleteRows = true,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false
            };
            _parasiteGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "displayName", HeaderText = "Название", FillWeight = 90 });
            _parasiteGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "template", HeaderText = "Имя образца (латиница)", FillWeight = 130 });
            _parasiteGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "chemical", HeaderText = "Химикат / ключ", FillWeight = 120 });
            _parasiteGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "hotbarKey", HeaderText = "Слот", FillWeight = 55 });
            _parasiteGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "count", HeaderText = "Сколько есть", FillWeight = 75 });
            var bugButtons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
            var bugSample = new Button { Text = "Снять образец выбранного жука…", AutoSize = true, Height = 30 };
            bugSample.Click += (s, e) => CaptureSelectedParasiteSample();
            var saveMap = new Button { Text = "Сохранить таблицу", AutoSize = true, Height = 30 };
            saveMap.Click += (s, e) => SaveWizardMappings(true);
            bugButtons.Controls.Add(bugSample);
            bugButtons.Controls.Add(saveMap);
            bugLayout.Controls.Add(_parasiteGrid, 0, 0);
            bugLayout.Controls.Add(bugButtons, 0, 1);
            bugsGroup.Controls.Add(bugLayout);

            var bottom = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
            var bedsButton = new Button { Text = "Дальше: расставить грядки →", AutoSize = true, Height = 32 };
            bedsButton.Click += (s, e) => _tabs.SelectedIndex = 2;
            var validateButton = new Button { Text = "Проверить образцы", AutoSize = true, Height = 32 };
            validateButton.Click += (s, e) => { _tabs.SelectedIndex = 4; CheckTemplates(); };
            bottom.Controls.Add(bedsButton);
            bottom.Controls.Add(validateButton);

            layout.Controls.Add(intro, 0, 0);
            layout.Controls.Add(sampleGroup, 0, 1);
            layout.Controls.Add(bugsGroup, 0, 2);
            layout.Controls.Add(bottom, 0, 3);
            page.Controls.Add(layout);
            return page;
        }

        private void LoadWizardMappings()
        {
            _parasiteGrid.Rows.Clear();
            foreach (ParasiteMap map in _cfg.Parasites)
            {
                int count = _cfg.Inventory.Chemicals.TryGetValue(map.Chemical, out int found) ? found : 0;
                _parasiteGrid.Rows.Add(map.Name, map.Parasite, map.Chemical, map.HotbarKey, count);
            }
        }

        private void SaveWizardMappings(bool showMessage)
        {
            if (_parasiteGrid.IsCurrentCellInEditMode)
                _parasiteGrid.EndEdit();
            var maps = new List<ParasiteMap>();
            bool invalidRow = false;
            foreach (DataGridViewRow row in _parasiteGrid.Rows)
            {
                if (row.IsNewRow) continue;
                string name = Convert.ToString(row.Cells[0].Value)?.Trim() ?? string.Empty;
                string template = Convert.ToString(row.Cells[1].Value)?.Trim() ?? string.Empty;
                string chemical = Convert.ToString(row.Cells[2].Value)?.Trim() ?? string.Empty;
                string key = Convert.ToString(row.Cells[3].Value)?.Trim() ?? string.Empty;
                if (name.Length == 0 && template.Length == 0 && chemical.Length == 0 && key.Length == 0) continue;
                if (template.Length == 0 || chemical.Length == 0 || key.Length == 0)
                {
                    invalidRow = true;
                    continue;
                }
                int count = 0;
                int.TryParse(Convert.ToString(row.Cells[4].Value), out count);
                maps.Add(new ParasiteMap
                {
                    Name = name.Length == 0 ? template : name,
                    Parasite = template,
                    Chemical = chemical,
                    HotbarKey = key
                });
                _cfg.Inventory.Chemicals[chemical] = Math.Max(0, count);
            }
            if (invalidRow)
            {
                if (showMessage)
                    MessageBox.Show("В таблице есть незаполненная строка. Заполните образец, химикат и слот хотбара либо удалите строку, затем сохраните снова.", "Настройка вредителей", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            _cfg.Parasites = maps;
            ConfigService.Save(_configPath, _cfg, _log);
            if (showMessage)
                MessageBox.Show("Настройка вредителей сохранена. Снимите образец каждого значка.", "AmazingBot", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void CaptureTemplateSample()
        {
            if (!(_templatePresetCombo.SelectedItem is TemplatePreset preset)) return;
            CaptureTemplate(preset.TemplateName, preset.DisplayName);
            ApplyTemplatePreset(preset.TemplateName);
        }

        private void CaptureSelectedParasiteSample()
        {
            if (_parasiteGrid.SelectedRows.Count == 0)
            {
                MessageBox.Show("Сначала выберите строку вредителя в таблице.", "AmazingBot", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            DataGridViewRow row = _parasiteGrid.SelectedRows[0];
            string template = Convert.ToString(row.Cells[1].Value)?.Trim() ?? string.Empty;
            if (template.Length == 0)
            {
                MessageBox.Show("Заполните колонку «Имя образца» (например, bug_spider), затем сохраните образец.", "AmazingBot", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            template = new string(template.Where(c => char.IsLetterOrDigit(c) || c == '_' || c == '-').ToArray());
            row.Cells[1].Value = template;
            SaveWizardMappings(false);
            CaptureTemplate(template, "вредитель «" + (Convert.ToString(row.Cells[0].Value) ?? template) + "»");
        }

        private void CaptureTemplate(string templateName, string displayName)
        {
            if (string.IsNullOrWhiteSpace(templateName)) return;
            CapturedFrame? frame = CaptureGameFrameWithoutBot();
            if (frame == null)
            {
                MessageBox.Show("Окно игры не найдено. Запустите игру в оконном/безрамочном режиме и проверьте заголовок в настройках.", "Обучение распознавания", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            using (var selector = new CropSelectorForm(frame.Image, displayName))
            {
                if (selector.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    BitmapImage crop = frame.Image.Crop(selector.SelectedCrop.X, selector.SelectedCrop.Y, selector.SelectedCrop.Width, selector.SelectedCrop.Height);
                    string dir = Path.GetFullPath(_cfg.TemplatesDir);
                    Directory.CreateDirectory(dir);
                    string path = Path.Combine(dir, templateName + ".bmp");
                    BmpCodec.Save(path, crop);
                    _templateResults.Items.Add("Образец сохранён: " + templateName + " (" + crop.Width + "×" + crop.Height + ")");
                    _log.Info("Создан образец распознавания «" + displayName + "»: " + path);
                    MessageBox.Show("Образец сохранён. Для проверки нажмите «Проверить образцы».", "AmazingBot", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Не удалось сохранить образец: " + ex.Message, "AmazingBot", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void ApplyTemplatePreset(string templateName)
        {
            switch (templateName)
            {
                case "crop_present": _cfg.Templates.CropPresent = templateName; break;
                case "bed_empty": _cfg.Templates.EmptyBed = templateName; break;
                case "crop_ready": _cfg.Templates.CropReady = templateName; break;
                case "needs_fertilizer": _cfg.Templates.NeedsFertilizer = templateName; break;
                case "moisture_low": _cfg.Templates.MoistureLow = templateName; break;
                case "moisture_full": _cfg.Templates.MoistureFull = templateName; break;
                case "btn_harvest": _cfg.Templates.HarvestButton = templateName; break;
                case "btn_clone": _cfg.Templates.CloneButton = templateName; break;
            }
            ConfigService.Save(_configPath, _cfg, _log);
        }

        private sealed class TemplatePreset
        {
            public string DisplayName { get; }
            public string TemplateName { get; }
            public TemplatePreset(string displayName, string templateName) { DisplayName = displayName; TemplateName = templateName; }
            public override string ToString() => DisplayName;
        }

        private TabPage BuildBedsTab()
        {
            var page = new TabPage("Грядки");
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Padding = new Padding(10)
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 74));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));

            var info = new Label
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(12, 8, 12, 8),
                Text = "Нажмите «Отметить грядки на превью». Окно бота временно скроется. Старые точки сохраняются; щёлкните по центрам новых грядок и нажмите «Сохранить точки». Для полного сброса используйте «Очистить» в окне разметки."
            };
            var infoPanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
            infoPanel.Controls.Add(info);

            _bedsList = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                MultiSelect = false
            };
            _bedsList.Columns.Add("Имя", 140);
            _bedsList.Columns.Add("X (от окна)", 130);
            _bedsList.Columns.Add("Y (от окна)", 130);

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = true };
            var markBedsBtn = new Button { Text = "✦ Отметить грядки на превью…", AutoSize = true, Height = 34 };
            markBedsBtn.Click += (s, e) => SelectBedsOnPreview();
            var renameBtn = new Button { Text = "Переименовать", AutoSize = true, Height = 34 };
            renameBtn.Click += (s, e) => RenameSelectedBed();
            var deleteBtn = new Button { Text = "Удалить выбранное", AutoSize = true, Height = 34 };
            deleteBtn.Click += (s, e) => DeleteSelectedBed();
            var clearBtn = new Button { Text = "Очистить все", AutoSize = true, Height = 34 };
            clearBtn.Click += (s, e) => ClearBeds();
            var saveBtn = new Button { Text = "Сохранить грядки", AutoSize = true, Height = 34 };
            saveBtn.Click += (s, e) => SaveBeds();
            buttons.Controls.Add(markBedsBtn);
            buttons.Controls.Add(renameBtn);
            buttons.Controls.Add(deleteBtn);
            buttons.Controls.Add(clearBtn);
            buttons.Controls.Add(saveBtn);

            layout.Controls.Add(infoPanel, 0, 0);
            layout.Controls.Add(_bedsList, 0, 1);
            layout.Controls.Add(buttons, 0, 2);
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

            recordLayout.Controls.Add(new Label { Text = "Имя маршрута:", TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
            _routeNameCombo = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDown };
            _routeNameCombo.Items.Add("barrel_main");
            _routeNameCombo.Items.Add("barrel_backup");
            _routeNameCombo.Items.Add("to_car");
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
            recordLayout.Controls.Add(new Label { Text = "От грядок до цели и ОБРАТНО к грядкам", TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.DimGray }, 1, 1);

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

        private TabPage BuildTemplatesTab()
        {
            var page = new TabPage("Шаблоны");
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Padding = new Padding(10)
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 90));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var info = new Label
            {
                Dock = DockStyle.Fill,
                Text = "Для основных состояний и значков используйте вкладку «Простая настройка»: там можно выделить образец мышью прямо на снимке. Здесь доступны снимок экрана и проверка сохранённых BMP-шаблонов. Имена шаблонов и подробности: templates/README.md."
            };

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
            var shotBtn = new Button { Text = "Сохранить скриншот окна игры", AutoSize = true, Height = 32 };
            shotBtn.Click += (s, e) => SaveScreenshot();
            var checkBtn = new Button { Text = "Проверить шаблоны на экране", AutoSize = true, Height = 32 };
            checkBtn.Click += (s, e) => CheckTemplates();
            buttons.Controls.Add(shotBtn);
            buttons.Controls.Add(checkBtn);

            _templateResults = new ListBox { Dock = DockStyle.Fill, Font = new Font("Consolas", 9f) };

            layout.Controls.Add(info, 0, 0);
            layout.Controls.Add(buttons, 0, 1);
            layout.Controls.Add(_templateResults, 0, 2);
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
                RowCount = 15,
                Padding = new Padding(10)
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 300));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < 14; i++)
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            int row = 0;
            AddRow(layout, ref row, "Заголовок окна игры содержит:", _windowTitleBox = new TextBox { Dock = DockStyle.Fill });
            AddRow(layout, ref row, "Точность распознавания (0.5–1.0):", _thresholdBox = MakeDecimal(0.50m, 1.00m, 0.82m, 2));
            AddRow(layout, ref row, "Вместимость лейки, литров:", _canCapacityBox = MakeDecimal(0.5m, 1000m, 10m, 1));
            AddRow(layout, ref row, "Литров за одно нажатие:", _litersPerClickBox = MakeDecimal(0.1m, 100m, 1m, 1));
            AddRow(layout, ref row, "Литров на одну грядку:", _litersPerBedBox = MakeDecimal(0.5m, 100m, 3m, 1));
            AddRow(layout, ref row, "Воды в одной фляге, литров:", _flaskLitersBox = MakeDecimal(0.5m, 100m, 5m, 1));
            AddRow(layout, ref row, "Клавиша лейки в хотбаре:", _canKeyBox = new TextBox { Dock = DockStyle.Fill, MaxLength = 12 });
            AddRow(layout, ref row, "Клавиша фляги в хотбаре:", _flaskKeyBox = new TextBox { Dock = DockStyle.Fill, MaxLength = 12 });
            AddRow(layout, ref row, "Клавиша семян в хотбаре:", _seedKeyBox = new TextBox { Dock = DockStyle.Fill, MaxLength = 12 });
            AddRow(layout, ref row, "Клавиша удобрения в хотбаре:", _fertilizerKeyBox = new TextBox { Dock = DockStyle.Fill, MaxLength = 12 });
            AddRow(layout, ref row, "Анти-афк, раз в (сек):", _afkIntervalBox = new NumericUpDown { Dock = DockStyle.Fill, Minimum = 5, Maximum = 3600, Value = 60 });
            AddRow(layout, ref row, "Анти-афк, клавиши (через +):", _afkKeysBox = new TextBox { Dock = DockStyle.Fill });
            AddRow(layout, ref row, "Уровень лога:", _logLevelCombo = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList });
            _logLevelCombo.Items.AddRange(new object[] { "Info", "Debug", "Warn", "Error" });
            _logLevelCombo.SelectedIndex = 0;

            var saveAll = new Button { Text = "💾  Сохранить все настройки", Dock = DockStyle.Fill, Font = new Font(Font, FontStyle.Bold) };
            saveAll.Click += (s, e) => SaveAllSettings();
            layout.Controls.Add(saveAll, 0, row);
            layout.SetColumnSpan(saveAll, 2);

            var hint = new Label
            {
                Dock = DockStyle.Fill,
                Text = "Файл настроек: " + Path.GetFullPath(_configPath) + "\nИзменения сохраняются туда же (рядом создаётся .bak). Остальные параметры (паразиты→химикаты, предметы багажника, маршруты, шкала химикатов) правятся в config.json вручную."
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
                else if (control is DataGridView grid)
                {
                    grid.BackgroundColor = Color.White;
                    grid.BorderStyle = BorderStyle.FixedSingle;
                    grid.GridColor = Color.FromArgb(226, 231, 239);
                    grid.EnableHeadersVisualStyles = false;
                    grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(232, 238, 247);
                    grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(42, 59, 86);
                    grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(215, 229, 247);
                    grid.DefaultCellStyle.SelectionForeColor = Color.FromArgb(32, 45, 63);
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

        private static NumericUpDown MakeDecimal(decimal min, decimal max, decimal value, int decimals)
        {
            return new NumericUpDown
            {
                Dock = DockStyle.Fill,
                Minimum = min,
                Maximum = max,
                DecimalPlaces = decimals,
                Increment = decimals == 0 ? 1m : (decimals == 2 ? 0.01m : 0.5m),
                Value = value
            };
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

        // ==================== Запуск / остановка бота ====================

        private async Task StartBotAsync()
        {
            if (_botRunning) return;
            SaveAllSettings(); // заодно зафиксируем последние правки
            if (_cfg.Beds.Count == 0 && _beds.Count == 0)
            {
                MessageBox.Show(
                    "Сначала настройте грядки: вкладка «Грядки» → «Захват точек».",
                    "AmazingBot", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _tabs.SelectedIndex = 2;
                return;
            }

            List<string> missingCore = MissingCoreTemplates();
            if (missingCore.Count > 0)
            {
                MessageBox.Show(
                    "Бот пока не запущен: не хватает образцов для распознавания:\n\n• " + string.Join(".bmp\n• ", missingCore) + ".bmp\n\nОткройте «Простая настройка», снимите эти состояния с экрана игры, затем запустите снова.",
                    "Сначала настройте распознавание", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _tabs.SelectedIndex = 1;
                return;
            }

            // Гарантируем, что грядки в конфиге совпадают со списком в окне
            _cfg.Beds = new List<BedPoint>(_beds);
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

        // ==================== Грядки ====================

        private void SelectBedsOnPreview()
        {
            if (_botRunning || _recording) return;
            var priorBeds = new List<BedPoint>(_beds);
            SaveAllSettings();
            CapturedFrame? frame = CaptureGameFrameWithoutBot();
            if (frame == null)
            {
                MessageBox.Show("Окно игры не найдено или кадр не захвачен. Оставьте игру открытой в оконном режиме и проверьте заголовок в настройках.", "Выбор грядок", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            using (var picker = new BedMarkerForm(frame.Image, priorBeds.Select(b => new Point(b.X, b.Y))))
            {
                if (picker.ShowDialog(this) != DialogResult.OK) return;
                if (picker.Points.Count == 0) return;

                _beds.Clear();
                var usedNames = new HashSet<string>(priorBeds.Select(b => b.Name), StringComparer.OrdinalIgnoreCase);
                int nextNumber = 1;
                foreach (Point point in picker.Points)
                {
                    BedPoint? old = priorBeds.FirstOrDefault(b => b.X == point.X && b.Y == point.Y);
                    string name;
                    if (old != null)
                    {
                        name = old.Name;
                    }
                    else
                    {
                        do { name = "bed" + nextNumber++; } while (usedNames.Contains(name));
                        usedNames.Add(name);
                    }
                    _beds.Add(new BedPoint { Name = name, X = point.X, Y = point.Y });
                }
                ReloadBedsList();
                SaveBeds();
                _log.Info("С превью захвачено и сохранено грядок: " + _beds.Count);
                MessageBox.Show("Сохранено грядок: " + _beds.Count + ". Проверьте список и не меняйте положение/масштаб камеры до запуска бота.", "Выбор грядок", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void ReloadBedsList()
        {
            _bedsList.Items.Clear();
            foreach (BedPoint bed in _beds)
            {
                var item = new ListViewItem(bed.Name);
                item.SubItems.Add(bed.X.ToString());
                item.SubItems.Add(bed.Y.ToString());
                _bedsList.Items.Add(item);
            }
        }

        private void RenameSelectedBed()
        {
            if (_bedsList.SelectedItems.Count == 0)
            {
                MessageBox.Show("Выберите грядку в списке.", "AmazingBot");
                return;
            }
            int index = _bedsList.SelectedIndices[0];
            string? name = PromptDialog.Show("Переименовать грядку", "Имя грядки:", _beds[index].Name);
            if (string.IsNullOrWhiteSpace(name))
                return;
            _beds[index].Name = name.Trim();
            ReloadBedsList();
            SaveBeds();
        }

        private void DeleteSelectedBed()
        {
            if (_bedsList.SelectedIndices.Count == 0)
            {
                MessageBox.Show("Выберите грядку в списке.", "AmazingBot");
                return;
            }
            _beds.RemoveAt(_bedsList.SelectedIndices[0]);
            ReloadBedsList();
            SaveBeds();
        }

        private void ClearBeds()
        {
            if (_beds.Count == 0) return;
            if (MessageBox.Show("Удалить все " + _beds.Count + " грядок из конфига?", "AmazingBot",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;
            _beds.Clear();
            ReloadBedsList();
            SaveBeds();
        }

        private void SaveBeds()
        {
            _cfg.Beds = new List<BedPoint>(_beds);
            ConfigService.Save(_configPath, _cfg, _log);
            SetStatus("грядки сохранены (" + _beds.Count + ")");
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
                "Запись маршрута «" + name + "» начнётся после нажатия ОК.\n\nДелайте всё как обычно: дойдите до цели и ВЕРНИТЕСЬ к грядкам. Клики и клавиши запишутся автоматически.\nОкно игры не двигайте!\n\nЗакончить запись — F10.",
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

        // ==================== Шаблоны ====================

        private void SaveScreenshot()
        {
            CapturedFrame? frame = CaptureGameFrameWithoutBot();
            if (frame == null)
            {
                MessageBox.Show(
                    "Окно игры не найдено. Запустите Amazing Online в оконном режиме.",
                    "AmazingBot", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            string outPath = Path.GetFullPath("screenshot.bmp");
            BmpCodec.Save(outPath, frame.Image);
            _templateResults.Items.Add("Снимок сохранён: " + outPath + " (" + frame.Width + "x" + frame.Height + ")");
            _templateResults.Items.Add("Вырежьте элементы в Paint → сохраните как 32-битный BMP в templates/");
            MessageBox.Show("Снимок сохранён:\n" + outPath, "AmazingBot", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void CheckTemplates()
        {
            Hide();
            try
            {
                Thread.Sleep(250);
                var game = CreateVision();
                if (game.EnsureWindow())
                {
                    NativeMethods.SetForegroundWindow(game.WindowHandle);
                    Thread.Sleep(200);
                }
                CheckTemplatesCore();
            }
            finally
            {
                Show();
                Activate();
            }
        }

        private void CheckTemplatesCore()
        {
            _templateResults.Items.Clear();
            var vision = CreateVision();
            if (!vision.Refresh())
            {
                _templateResults.Items.Add("Окно игры не найдено.");
                return;
            }

            double threshold = _cfg.MatchThreshold;
            string dir = _cfg.TemplatesDir;
            if (!Directory.Exists(dir))
            {
                _templateResults.Items.Add("Папка шаблонов не найдена: " + Path.GetFullPath(dir));
                return;
            }

            string[] files = Directory.GetFiles(dir, "*.bmp");
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            if (files.Length == 0)
            {
                _templateResults.Items.Add("В templates/ нет ни одного .bmp — сначала сохраните скриншот и нарежьте шаблоны.");
            }
            else
            {
                _templateResults.Items.Add("=== Шаблоны на текущем экране ===");
                foreach (string file in files)
                {
                    string name = Path.GetFileNameWithoutExtension(file);
                    try
                    {
                        Match? m = vision.Find(name, Math.Max(0.55, threshold - 0.25));
                        if (m != null)
                            _templateResults.Items.Add("[виден]     " + name.PadRight(26) + " score=" + m.Score.ToString("0.000") + " при (" + m.X + "," + m.Y + ")");
                        else
                            _templateResults.Items.Add("[не виден]  " + name);
                    }
                    catch (Exception ex)
                    {
                        _templateResults.Items.Add("[ошибка]    " + name + ": " + ex.Message);
                    }
                }
            }

            _templateResults.Items.Add("");
            _templateResults.Items.Add("=== Шаблоны из конфига, которых нет на диске ===");
            bool anyMissing = false;
            foreach (string required in RequiredTemplateNames())
            {
                if (!File.Exists(vision.TemplatePath(required)))
                {
                    _templateResults.Items.Add("НЕТ ФАЙЛА: " + required + ".bmp");
                    anyMissing = true;
                }
            }
            if (!anyMissing)
                _templateResults.Items.Add("(все на месте)");
        }

        private List<string> MissingCoreTemplates()
        {
            string harvestButton = string.Equals(_cfg.HarvestMode, "clone", StringComparison.OrdinalIgnoreCase)
                ? _cfg.Templates.CloneButton
                : _cfg.Templates.HarvestButton;
            string[] required =
            {
                _cfg.Templates.EmptyBed,
                _cfg.Templates.CropReady,
                _cfg.Templates.NeedsFertilizer,
                _cfg.Templates.MoistureLow,
                harvestButton
            };
            VisionService vision = CreateVision();
            return required
                .Where(name => !string.IsNullOrWhiteSpace(name) && !vision.TemplateExists(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private IEnumerable<string> RequiredTemplateNames()
        {
            var names = new List<string>
            {
                _cfg.Templates.CropPresent,
                _cfg.Templates.CropReady,
                _cfg.Templates.EmptyBed,
                _cfg.Templates.NeedsFertilizer,
                _cfg.Templates.MoistureLow,
                _cfg.Templates.MoistureFull,
                string.Equals(_cfg.HarvestMode, "clone", StringComparison.OrdinalIgnoreCase) ? _cfg.Templates.CloneButton : _cfg.Templates.HarvestButton,
                _cfg.Watering.CanTemplate,
                _cfg.Watering.BrokenCanTemplate
            };
            if (!string.IsNullOrWhiteSpace(_cfg.Templates.ClosePanel)) names.Add(_cfg.Templates.ClosePanel);
            if (!string.IsNullOrWhiteSpace(_cfg.Watering.MainBarrelEmptyTemplate)) names.Add(_cfg.Watering.MainBarrelEmptyTemplate);
            if (!string.IsNullOrWhiteSpace(_cfg.Watering.BackupBarrelEmptyTemplate)) names.Add(_cfg.Watering.BackupBarrelEmptyTemplate);
            if (!string.IsNullOrWhiteSpace(_cfg.Car.RespawnNoticeTemplate)) names.Add(_cfg.Car.RespawnNoticeTemplate);
            foreach (ParasiteMap p in _cfg.Parasites)
            {
                names.Add(p.Parasite);
                names.Add(p.Chemical);
            }
            foreach (TrunkItemConfig t in _cfg.Trunk.Items)
                names.Add(t.Template);
            return names.Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.OrdinalIgnoreCase);
        }

        // ==================== Настройки ====================

        private void LoadConfigIntoControls()
        {
            _windowTitleBox.Text = _cfg.WindowTitleContains;
            _thresholdBox.Value = (decimal)Math.Max(0.5, Math.Min(1.0, _cfg.MatchThreshold));
            _canCapacityBox.Value = (decimal)Math.Min((double)_canCapacityBox.Maximum, Math.Max((double)_canCapacityBox.Minimum, _cfg.Watering.CanCapacityLiters));
            _litersPerClickBox.Value = (decimal)Math.Min((double)_litersPerClickBox.Maximum, Math.Max((double)_litersPerClickBox.Minimum, _cfg.Watering.LitersPerClick));
            _litersPerBedBox.Value = (decimal)Math.Min((double)_litersPerBedBox.Maximum, Math.Max((double)_litersPerBedBox.Minimum, _cfg.Watering.LitersPerBed));
            _flaskLitersBox.Value = (decimal)Math.Min((double)_flaskLitersBox.Maximum, Math.Max((double)_flaskLitersBox.Minimum, _cfg.Watering.FlaskLiters));
            _canKeyBox.Text = _cfg.Watering.CanHotbarKey;
            _flaskKeyBox.Text = _cfg.Watering.FlaskHotbarKey;
            _seedKeyBox.Text = _cfg.Planting.SeedHotbarKey;
            _fertilizerKeyBox.Text = _cfg.FertilizerHotbarKey;
            _afkIntervalBox.Value = Math.Min(_afkIntervalBox.Maximum, Math.Max(_afkIntervalBox.Minimum, _cfg.AntiAfk.IntervalSeconds));
            _afkKeysBox.Text = string.Join(" + ", _cfg.AntiAfk.Keys);
            int logIndex = _logLevelCombo.Items.IndexOf(_cfg.LogLevel);
            _logLevelCombo.SelectedIndex = logIndex >= 0 ? logIndex : 0;

            _harvestModeCombo.SelectedIndex = string.Equals(_cfg.HarvestMode, "clone", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
            _intervalBox.Value = Math.Min(_intervalBox.Maximum, Math.Max(_intervalBox.Minimum, _cfg.CheckIntervalSeconds));
            _autoDisableCheck.Checked = _cfg.AutoDisableOnAllBedsDone;
            _antiAfkCheck.Checked = _cfg.AntiAfk.Enabled;
        }

        private void SaveAllSettings()
        {
            _cfg.WindowTitleContains = _windowTitleBox.Text.Trim();
            _cfg.MatchThreshold = (double)_thresholdBox.Value;
            _cfg.Watering.CanCapacityLiters = (double)_canCapacityBox.Value;
            _cfg.Watering.LitersPerClick = (double)_litersPerClickBox.Value;
            _cfg.Watering.LitersPerBed = (double)_litersPerBedBox.Value;
            _cfg.Watering.FlaskLiters = (double)_flaskLitersBox.Value;
            _cfg.Watering.CanHotbarKey = _canKeyBox.Text.Trim();
            _cfg.Watering.FlaskHotbarKey = _flaskKeyBox.Text.Trim();
            _cfg.Planting.SeedHotbarKey = _seedKeyBox.Text.Trim();
            _cfg.FertilizerHotbarKey = _fertilizerKeyBox.Text.Trim();
            _cfg.AntiAfk.IntervalSeconds = (int)_afkIntervalBox.Value;
            _cfg.AntiAfk.Keys = _afkKeysBox.Text
                .Split(new[] { '+', ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(k => k.Trim())
                .Where(k => k.Length > 0)
                .ToList();
            _cfg.LogLevel = _logLevelCombo.SelectedItem?.ToString() ?? "Info";

            _cfg.HarvestMode = _harvestModeCombo.SelectedIndex == 1 ? "clone" : "harvest";
            _cfg.CheckIntervalSeconds = (int)_intervalBox.Value;
            _cfg.AutoDisableOnAllBedsDone = _autoDisableCheck.Checked;
            _cfg.AntiAfk.Enabled = _antiAfkCheck.Checked;
            _cfg.Beds = new List<BedPoint>(_beds);

            _log.MinimumLevel = ParseLogLevel(_cfg.LogLevel);
            SaveWizardMappings(false);
            ConfigService.Save(_configPath, _cfg, _log);
            SetStatus("настройки сохранены");
        }

        // ==================== Утилиты ====================

        private CapturedFrame? CaptureGameFrameWithoutBot()
        {
            Hide();
            try
            {
                Thread.Sleep(250); // let Windows redraw the game after hiding this form
                var vision = CreateVision();
                if (!vision.EnsureWindow()) return null;
                NativeMethods.SetForegroundWindow(vision.WindowHandle);
                Thread.Sleep(200);
                return vision.Refresh() ? vision.Current : null;
            }
            finally
            {
                Show();
                Activate();
            }
        }

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

        // ==================== Вложенный диалог ввода ====================

        private sealed class PromptDialog : Form
        {
            private readonly TextBox _box;

            private PromptDialog(string title, string prompt, string initial)
            {
                Text = title;
                FormBorderStyle = FormBorderStyle.FixedDialog;
                StartPosition = FormStartPosition.CenterParent;
                ClientSize = new Size(380, 130);
                MaximizeBox = false;
                MinimizeBox = false;
                ShowInTaskbar = false;

                var label = new Label { Text = prompt, Left = 12, Top = 12, Width = 356, AutoSize = true };
                _box = new TextBox { Left = 12, Top = 36, Width = 356, Text = initial };
                var ok = new Button { Text = "ОК", Left = 212, Top = 66, Width = 70, DialogResult = DialogResult.OK };
                var cancel = new Button { Text = "Отмена", Left = 292, Top = 66, Width = 76, DialogResult = DialogResult.Cancel };
                AcceptButton = ok;
                CancelButton = cancel;
                Controls.Add(label);
                Controls.Add(_box);
                Controls.Add(ok);
                Controls.Add(cancel);
            }

            public static string? Show(string title, string prompt, string initial)
            {
                using (var dialog = new PromptDialog(title, prompt, initial))
                {
                    return dialog.ShowDialog() == DialogResult.OK ? dialog._box.Text : null;
                }
            }
        }
    }
}
