using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using AmazingBot.Core;

namespace AmazingBot.Config
{
    public static class ConfigService
    {
        public static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            IgnoreReadOnlyProperties = false,
            IgnoreReadOnlyFields = false,
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip
        };

        public static string FindConfigPath(string? explicitPath)
        {
            if (!string.IsNullOrWhiteSpace(explicitPath))
                return Path.GetFullPath(explicitPath);

            string cwd = Directory.GetCurrentDirectory();
            string[] candidates =
            {
                Path.Combine(cwd, "config.json"),
                Path.Combine(cwd, "configs", "config.json"),
                Path.Combine(cwd, "configs", "config.example.json"),
                Path.Combine(AppContext.BaseDirectory, "config.json"),
                Path.Combine(AppContext.BaseDirectory, "configs", "config.example.json")
            };
            foreach (string candidate in candidates)
            {
                if (File.Exists(candidate))
                    return Path.GetFullPath(candidate);
            }
            return Path.Combine(cwd, "config.json");
        }

        public static BotConfig Load(string path, Logger? log, out List<string> problems)
        {
            problems = new List<string>();
            BotConfig? cfg = null;
            if (File.Exists(path))
            {
                try
                {
                    string json = File.ReadAllText(path);
                    cfg = JsonSerializer.Deserialize<BotConfig>(json, JsonOptions);
                }
                catch (Exception ex)
                {
                    string message = "Не удалось прочитать " + path + ": " + ex.Message;
                    problems.Add(message);
                    log?.Error(message);
                }
            }
            else
            {
                problems.Add("Файл конфига не найден: " + path + " — используется конфигурация по умолчанию. Скопируйте configs/config.example.json в config.json и настройте под себя.");
                log?.Warn(problems[problems.Count - 1]);
            }

            if (cfg == null)
                cfg = new BotConfig();

            Validate(cfg, problems);
            return cfg;
        }

        public static void Save(string path, BotConfig cfg, Logger? log)
        {
            try
            {
                if (File.Exists(path))
                    File.Copy(path, path + ".bak", overwrite: true);
                string json = JsonSerializer.Serialize(cfg, JsonOptions);
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".");
                File.WriteAllText(path, json);
                log?.Info("Конфиг сохранён: " + path + " (резервная копия: " + path + ".bak)");
            }
            catch (Exception ex)
            {
                log?.Exception("Не удалось сохранить конфиг " + path, ex);
                throw;
            }
        }

        private static void Validate(BotConfig cfg, List<string> problems)
        {
            if (string.IsNullOrWhiteSpace(cfg.WindowTitleContains))
                problems.Add("windowTitleContains пуст — бот не найдёт окно игры");
            if (cfg.CheckIntervalSeconds < 1)
                problems.Add("checkIntervalSeconds должен быть >= 1");
            if (cfg.MatchThreshold <= 0.5 || cfg.MatchThreshold > 1.0)
                problems.Add("matchThreshold должен быть в пределах (0.5; 1.0]");
            if (cfg.Watering.CanCapacityLiters <= 0)
                problems.Add("watering.canCapacityLiters должен быть > 0");
            if (cfg.Watering.LitersPerClick <= 0)
                problems.Add("watering.litersPerClick должен быть > 0");
            if (cfg.Watering.LitersPerBed <= 0)
                problems.Add("watering.litersPerBed должен быть > 0");
            if (cfg.Watering.LitersPerClick > cfg.Watering.CanCapacityLiters)
                problems.Add("watering.litersPerClick больше вместимости лейки");
            string mode = (cfg.HarvestMode ?? string.Empty).Trim().ToLowerInvariant();
            if (mode != "harvest" && mode != "clone")
                problems.Add("harvestMode должен быть \"harvest\" или \"clone\"");
            for (int i = 0; i < cfg.Parasites.Count; i++)
            {
                ParasiteMap p = cfg.Parasites[i];
                if (string.IsNullOrWhiteSpace(p.Parasite) || string.IsNullOrWhiteSpace(p.Chemical))
                    problems.Add("parasites[" + i + "]: пустые parasite или chemical");
            }
            for (int i = 0; i < cfg.Trunk.Items.Count; i++)
            {
                TrunkItemConfig t = cfg.Trunk.Items[i];
                if (string.IsNullOrWhiteSpace(t.Template) || string.IsNullOrWhiteSpace(t.Name))
                    problems.Add("trunk.items[" + i + "]: пустые template или name");
            }
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (BedPoint bed in cfg.Beds)
                if (!names.Add(bed.Name))
                    problems.Add("Дублирующаяся грядка: " + bed.Name);
        }
    }
}
