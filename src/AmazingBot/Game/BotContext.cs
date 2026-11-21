using System;
using System.Collections.Generic;
using System.Linq;
using AmazingBot.Config;
using AmazingBot.Core;
using AmazingBot.Vision;

namespace AmazingBot.Game
{
    /// <summary>Состояние одной грядки во время работы бота.</summary>
    public sealed class BedRuntimeState
    {
        public double WateredLiters { get; set; }
        public bool DoneThisPass { get; set; }
    }

    /// <summary>Живые счётчики инвентаря (копируются из конфига при старте, в файл не сохраняются).</summary>
    public sealed class RuntimeInventory
    {
        public int Flasks { get; set; }
        public int Fertilizer { get; set; }
        public int Seeds { get; set; }
        public Dictionary<string, int> Chemicals { get; set; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        public static RuntimeInventory FromConfig(InventoryConfig cfg)
        {
            return new RuntimeInventory
            {
                Flasks = cfg.Flasks,
                Fertilizer = cfg.Fertilizer,
                Seeds = cfg.Seeds,
                Chemicals = new Dictionary<string, int>(cfg.Chemicals ?? new Dictionary<string, int>(), StringComparer.OrdinalIgnoreCase)
            };
        }

        public static string NormalizeName(string name)
        {
            string n = (name ?? string.Empty).Trim().ToLowerInvariant();
            if (n == "flasks" || n == "flaskа" || n == "фляга" || n == "фляги") n = "flask";
            if (n == "fertilizers" || n == "fertilizer_item" || n == "удобрение" || n == "удобрения") n = "fertilizer";
            if (n == "seeds" || n == "seed_item" || n == "семена" || n == "семя") n = "seed";
            return n;
        }

        public int GetCount(string name)
        {
            string n = NormalizeName(name);
            switch (n)
            {
                case "flask": return Flasks;
                case "fertilizer": return Fertilizer;
                case "seed": return Seeds;
                default: return Chemicals.TryGetValue(n, out int v) ? v : 0;
            }
        }

        public void AddCount(string name, int delta)
        {
            string n = NormalizeName(name);
            switch (n)
            {
                case "flask":
                    Flasks = Math.Max(0, Flasks + delta);
                    break;
                case "fertilizer":
                    Fertilizer = Math.Max(0, Fertilizer + delta);
                    break;
                case "seed":
                    Seeds = Math.Max(0, Seeds + delta);
                    break;
                default:
                    int current = Chemicals.TryGetValue(n, out int v) ? v : 0;
                    Chemicals[n] = Math.Max(0, current + delta);
                    break;
            }
        }

        public string Summary()
        {
            string chem = Chemicals.Count == 0
                ? "нет"
                : string.Join(", ", Chemicals.Select(kv => kv.Key + "=" + kv.Value));
            return "фляги=" + Flasks + ", удобрение=" + Fertilizer + ", семена=" + Seeds + ", химикаты: " + chem;
        }
    }

    /// <summary>Время-пробега состояние бота.</summary>
    public sealed class BotState
    {
        public Dictionary<string, BedRuntimeState> Beds { get; } = new Dictionary<string, BedRuntimeState>(StringComparer.OrdinalIgnoreCase);
        public RuntimeInventory Inventory { get; set; } = new RuntimeInventory();
        public double CanLiters { get; set; }
        public bool CanBroken { get; set; }
        public bool BarrelsDry { get; set; }
        public int FinalActions { get; set; }
        public int Passes { get; set; }
        public DateTime LastAntiAfk { get; set; } = DateTime.MinValue;
        public DateTime LastCarCheck { get; set; } = DateTime.MinValue;

        public BedRuntimeState Bed(string name)
        {
            if (!Beds.TryGetValue(name, out BedRuntimeState? state))
            {
                state = new BedRuntimeState();
                Beds[name] = state;
            }
            return state;
        }
    }

    /// <summary>Контекст, передаваемый задачам бота: конфиг, зрение, лог, состояние.</summary>
    public sealed class BotContext
    {
        public BotConfig Config { get; }
        public VisionService Vision { get; }
        public Logger Log { get; }
        public BotState State { get; }
        public Func<bool> IsPanic { get; }

        public BotContext(BotConfig config, VisionService vision, Logger log, BotState state, Func<bool> isPanic)
        {
            Config = config;
            Vision = vision;
            Log = log;
            State = state;
            IsPanic = isPanic;
        }

        public int OriginX => Vision.ClientOriginX;
        public int OriginY => Vision.ClientOriginY;
    }
}
