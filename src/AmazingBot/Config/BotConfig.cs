using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AmazingBot.Config
{
    public sealed class BedPoint
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        /// <summary>X относительно клиентской области окна игры.</summary>
        [JsonPropertyName("x")]
        public int X { get; set; }

        /// <summary>Y относительно клиентской области окна игры.</summary>
        [JsonPropertyName("y")]
        public int Y { get; set; }
    }

    public sealed class TimingConfig
    {
        [JsonPropertyName("actionDelayMs")]
        public int ActionDelayMs { get; set; } = 120;

        [JsonPropertyName("menuOpenWaitMs")]
        public int MenuOpenWaitMs { get; set; } = 400;

        [JsonPropertyName("afterActionMs")]
        public int AfterActionMs { get; set; } = 150;

        [JsonPropertyName("interStepDelayMs")]
        public int InterStepDelayMs { get; set; } = 40;
    }

    public sealed class WateringConfig
    {
        [JsonPropertyName("canCapacityLiters")]
        public double CanCapacityLiters { get; set; } = 10;

        [JsonPropertyName("litersPerClick")]
        public double LitersPerClick { get; set; } = 1;

        [JsonPropertyName("litersPerBed")]
        public double LitersPerBed { get; set; } = 3;

        [JsonPropertyName("flaskLiters")]
        public double FlaskLiters { get; set; } = 5;

        [JsonPropertyName("useFlaskFromInventory")]
        public bool UseFlaskFromInventory { get; set; } = true;

        [JsonPropertyName("canHotbarKey")]
        public string CanHotbarKey { get; set; } = "3";

        [JsonPropertyName("flaskHotbarKey")]
        public string FlaskHotbarKey { get; set; } = "2";

        /// <summary>Маршрут к основной бочке (записывается через меню «Записать маршрут»). Пусто — бочки отключены.</summary>
        [JsonPropertyName("mainBarrelRoute")]
        public string MainBarrelRoute { get; set; } = "barrel_main";

        /// <summary>Резервная бочка на случай, если в основной закончилась вода. Пусто — без резерва.</summary>
        [JsonPropertyName("backupBarrelRoute")]
        public string BackupBarrelRoute { get; set; } = "";

        [JsonPropertyName("barrelRefillKey")]
        public string BarrelRefillKey { get; set; } = "E";

        /// <summary>Шаблон «бочка пуста» (необязательно) — проверяется после полива из основной бочки.</summary>
        [JsonPropertyName("mainBarrelEmptyTemplate")]
        public string MainBarrelEmptyTemplate { get; set; } = "";

        [JsonPropertyName("backupBarrelEmptyTemplate")]
        public string BackupBarrelEmptyTemplate { get; set; } = "";

        [JsonPropertyName("barrelEmptyWaitMs")]
        public int BarrelEmptyWaitMs { get; set; } = 700;

        [JsonPropertyName("canTemplate")]
        public string CanTemplate { get; set; } = "can";

        [JsonPropertyName("brokenCanTemplate")]
        public string BrokenCanTemplate { get; set; } = "can_broken";

        /// <summary>Начать ли с полной лейки (true) или с пустой (false).</summary>
        [JsonPropertyName("startWithFullCan")]
        public bool StartWithFullCan { get; set; } = true;
    }

    public sealed class TemplatesConfig
    {
        /// <summary>Optional visual cue proving that a planted crop is present on the bed.</summary>
        [JsonPropertyName("cropPresent")]
        public string CropPresent { get; set; } = "crop_present";

        [JsonPropertyName("emptyBed")]
        public string EmptyBed { get; set; } = "bed_empty";

        [JsonPropertyName("cropReady")]
        public string CropReady { get; set; } = "crop_ready";

        [JsonPropertyName("needsFertilizer")]
        public string NeedsFertilizer { get; set; } = "needs_fertilizer";

        [JsonPropertyName("moistureLow")]
        public string MoistureLow { get; set; } = "moisture_low";

        [JsonPropertyName("moistureFull")]
        public string MoistureFull { get; set; } = "moisture_full";

        /// <summary>Кнопка сбора урожая в панели грядки.</summary>
        [JsonPropertyName("harvestButton")]
        public string HarvestButton { get; set; } = "btn_harvest";

        /// <summary>Кнопка клонирования в панели грядки.</summary>
        [JsonPropertyName("cloneButton")]
        public string CloneButton { get; set; } = "btn_clone";

        /// <summary>Шаблон закрытия панели грядки (крестик). Если пуст — закрываем через Escape.</summary>
        [JsonPropertyName("closePanel")]
        public string ClosePanel { get; set; } = "";
    }

    public sealed class ParasiteMap
    {
        /// <summary>Шаблон иконки паразита в панели грядки.</summary>
        [JsonPropertyName("parasite")]
        public string Parasite { get; set; } = string.Empty;

        /// <summary>Шаблон химиката от этого паразита (также ключ в inventory.chemicals).</summary>
        [JsonPropertyName("chemical")]
        public string Chemical { get; set; } = string.Empty;

        /// <summary>Горячая клавиша химиката в хотбаре (например, "4").</summary>
        [JsonPropertyName("hotbarKey")]
        public string HotbarKey { get; set; } = "";

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;
    }

    public sealed class PlantingConfig
    {
        /// <summary>Сажать семена на пустые грядки.</summary>
        [JsonPropertyName("plantWhenEmpty")]
        public bool PlantWhenEmpty { get; set; } = true;

        /// <summary>Горячая клавиша семян в хотбаре.</summary>
        [JsonPropertyName("seedHotbarKey")]
        public string SeedHotbarKey { get; set; } = "1";
    }

    public sealed class AntiAfkConfig
    {
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; } = true;

        [JsonPropertyName("intervalSeconds")]
        public int IntervalSeconds { get; set; } = 60;

        /// <summary>Клавиши, которые бот нажимает для сброса анти-афк таймера (например, ["Space"]).</summary>
        [JsonPropertyName("keys")]
        public List<string> Keys { get; set; } = new List<string> { "Space" };

        /// <summary>Дополнительно слегка двигать мышь.</summary>
        [JsonPropertyName("mouseWiggle")]
        public bool MouseWiggle { get; set; } = true;
    }

    public sealed class TrunkItemConfig
    {
        /// <summary>Шаблон предмета в интерфейсе багажника.</summary>
        [JsonPropertyName("template")]
        public string Template { get; set; } = string.Empty;

        /// <summary>Имя предмета для учёта в инвентаре: flask | fertilizer | seed | chemical_<имя шаблона химиката>.</summary>
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        /// <summary>Сколько раз кликнуть предмет в багажнике за одну погрузку (для стаков — 1).</summary>
        [JsonPropertyName("takeCount")]
        public int TakeCount { get; set; } = 1;
    }

    public sealed class TrunkConfig
    {
        /// <summary>Маршрут к машине/багажнику (записывается через меню).</summary>
        [JsonPropertyName("route")]
        public string Route { get; set; } = "to_car";

        [JsonPropertyName("openKey")]
        public string OpenKey { get; set; } = "T";

        [JsonPropertyName("closeKey")]
        public string CloseKey { get; set; } = "Escape";

        [JsonPropertyName("openWaitMs")]
        public int OpenWaitMs { get; set; } = 700;

        /// <summary>Предметы, которые бот забирает с багажника, когда они заканчиваются.</summary>
        [JsonPropertyName("items")]
        public List<TrunkItemConfig> Items { get; set; } = new List<TrunkItemConfig>();
    }

    public sealed class CarConfig
    {
        /// <summary>Шаблон уведомления «Транспорт появился / готов к занятию» (при респавне транспорта). Пусто — проверка выключена.</summary>
        [JsonPropertyName("respawnNoticeTemplate")]
        public string RespawnNoticeTemplate { get; set; } = "";

        /// <summary>Маршрут к машине для занятия транспорта.</summary>
        [JsonPropertyName("claimRoute")]
        public string ClaimRoute { get; set; } = "to_car";

        /// <summary>Клавиша посадки в машину.</summary>
        [JsonPropertyName("claimKey")]
        public string ClaimKey { get; set; } = "F";

        /// <summary>Как часто проверять респавн транспорта, секунд.</summary>
        [JsonPropertyName("checkIntervalSeconds")]
        public int CheckIntervalSeconds { get; set; } = 10;

        /// <summary>Пауза между появлением уведомления о респавне и попыткой занять машину, мс.</summary>
        [JsonPropertyName("claimDelayMs")]
        public int ClaimDelayMs { get; set; } = 500;
    }

    public sealed class InventoryConfig
    {
        [JsonPropertyName("flasks")]
        public int Flasks { get; set; } = 3;

        [JsonPropertyName("fertilizer")]
        public int Fertilizer { get; set; } = 5;

        [JsonPropertyName("seeds")]
        public int Seeds { get; set; } = 10;

        /// <summary>Остаток химикатов по шаблону химиката (см. parasites[].chemical).</summary>
        [JsonPropertyName("chemicals")]
        public Dictionary<string, int> Chemicals { get; set; } = new Dictionary<string, int>();
    }

    public sealed class ProfessionConfig
    {
        /// <summary>mining | fishing_rod | fishing_net.</summary>
        [JsonPropertyName("mode")]
        public string Mode { get; set; } = "mining";

        /// <summary>Name of a recorded full work cycle in routes/.</summary>
        [JsonPropertyName("route")]
        public string Route { get; set; } = "mine_cycle";

        /// <summary>Pause between repeated route cycles.</summary>
        [JsonPropertyName("cycleDelayMs")]
        public int CycleDelayMs { get; set; } = 1000;
    }

    public sealed class BotConfig
    {
        [JsonPropertyName("windowTitleContains")]
        public string WindowTitleContains { get; set; } = "Amazing Online";

        [JsonPropertyName("profession")]
        public ProfessionConfig Profession { get; set; } = new ProfessionConfig();

        [JsonPropertyName("templatesDir")]
        public string TemplatesDir { get; set; } = "templates";

        [JsonPropertyName("routesDir")]
        public string RoutesDir { get; set; } = "routes";

        [JsonPropertyName("logsDir")]
        public string LogsDir { get; set; } = "logs";

        /// <summary>Интервал проверки грядок (секунды). Настраивается в меню.</summary>
        [JsonPropertyName("checkIntervalSeconds")]
        public int CheckIntervalSeconds { get; set; } = 20;

        /// <summary>Отключить бота автоматически, когда все грядки собраны/склонированы за проход.</summary>
        [JsonPropertyName("autoDisableOnAllBedsDone")]
        public bool AutoDisableOnAllBedsDone { get; set; } = true;

        /// <summary>"harvest" — собирать урожай, "clone" — клонировать.</summary>
        [JsonPropertyName("harvestMode")]
        public string HarvestMode { get; set; } = "harvest";

        [JsonPropertyName("matchThreshold")]
        public double MatchThreshold { get; set; } = 0.82;

        [JsonPropertyName("maxActionsPerBed")]
        public int MaxActionsPerBed { get; set; } = 12;

        [JsonPropertyName("stallLimitSeconds")]
        public int StallLimitSeconds { get; set; } = 900;

        /// <summary>Клавиша экстренной остановки (по виртуальному коду, менять не нужно).</summary>
        [JsonPropertyName("panicKeyNote")]
        public string PanicKeyNote { get; set; } = "F12 — экстренная остановка";

        [JsonPropertyName("beds")]
        public List<BedPoint> Beds { get; set; } = new List<BedPoint>();

        [JsonPropertyName("timing")]
        public TimingConfig Timing { get; set; } = new TimingConfig();

        [JsonPropertyName("watering")]
        public WateringConfig Watering { get; set; } = new WateringConfig();

        [JsonPropertyName("templates")]
        public TemplatesConfig Templates { get; set; } = new TemplatesConfig();

        [JsonPropertyName("parasites")]
        public List<ParasiteMap> Parasites { get; set; } = new List<ParasiteMap>();

        [JsonPropertyName("planting")]
        public PlantingConfig Planting { get; set; } = new PlantingConfig();

        /// <summary>Горячая клавиша удобрения в хотбаре.</summary>
        [JsonPropertyName("fertilizerHotbarKey")]
        public string FertilizerHotbarKey { get; set; } = "5";

        [JsonPropertyName("antiAfk")]
        public AntiAfkConfig AntiAfk { get; set; } = new AntiAfkConfig();

        [JsonPropertyName("trunk")]
        public TrunkConfig Trunk { get; set; } = new TrunkConfig();

        [JsonPropertyName("car")]
        public CarConfig Car { get; set; } = new CarConfig();

        [JsonPropertyName("inventory")]
        public InventoryConfig Inventory { get; set; } = new InventoryConfig();

        [JsonPropertyName("logLevel")]
        public string LogLevel { get; set; } = "Info";
    }
}
