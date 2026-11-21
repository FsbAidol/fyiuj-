using System;
using AmazingBot.Config;
using AmazingBot.Game;
using AmazingBot.Vision;

namespace AmazingBot.Tasks
{
    /// <summary>
    /// Проход по грядкам: лечение паразитов правильным химикатом,
    /// внесение удобрений, точный полив, посадка, сбор урожая или клонирование.
    /// </summary>
    public sealed class GardenTask
    {
        private readonly BotContext _ctx;
        private readonly WateringTask _watering;

        public GardenTask(BotContext ctx)
        {
            _ctx = ctx;
            _watering = new WateringTask(ctx);
        }

        /// <summary>
        /// Один проход по всем грядкам.
        /// Возвращает true, если ВСЕ грядки этого прохода были собраны/клонированы.
        /// </summary>
        public bool RunPass(GameClient game)
        {
            BotConfig cfg = _ctx.Config;
            if (cfg.Beds.Count == 0)
            {
                _ctx.Log.Warn("Грядки не откалиброваны. Меню → «Калибровка грядок» (F8 над грядкой, F9 — закончить)");
                return false;
            }

            bool allFinal = true;
            foreach (BedPoint bed in cfg.Beds)
            {
                if (_ctx.IsPanic()) break;
                bool finalAction = ProcessBed(game, bed);
                allFinal = allFinal && finalAction;
            }
            return allFinal;
        }

        private bool ProcessBed(GameClient game, BedPoint bed)
        {
            BotConfig cfg = _ctx.Config;
            TemplatesConfig t = cfg.Templates;
            double thr = cfg.MatchThreshold;
            string mode = (cfg.HarvestMode ?? "harvest").Trim().ToLowerInvariant();
            bool cloneMode = mode == "clone";

            _ctx.Log.Info("=== Грядка «" + bed.Name + "» ===");
            if (!game.OpenBedPanel(bed))
                return false;

            bool cropSampleAvailable = _ctx.Vision.TemplateExists(t.CropPresent);
            bool cropDetected = cropSampleAvailable && Exists(t.CropPresent, thr);
            if (cropDetected)
                _ctx.Log.Info("Грядка «" + bed.Name + "»: растение распознано по образцу «" + t.CropPresent + "»; проверяю вредителей, удобрение и воду");

            bool finalAction = false;
            bool anyProgress = false;
            try
            {
                int maxActions = Math.Max(1, cfg.MaxActionsPerBed);
                for (int iter = 0; iter < maxActions && !_ctx.IsPanic(); iter++)
                {
                    // 1) Готова к сбору / клонированию
                    if (Exists(t.CropReady, thr))
                    {
                        string button = cloneMode ? t.CloneButton : t.HarvestButton;
                        if (game.ClickTemplate(button, thr))
                        {
                            BedRuntimeState bedState = _ctx.State.Bed(bed.Name);
                            bedState.DoneThisPass = true;
                            bedState.WateredLiters = cfg.Watering.LitersPerBed;
                            _ctx.State.FinalActions++;
                            _ctx.Log.Info("Грядка «" + bed.Name + "»: " + (cloneMode ? "растение клонировано" : "урожай собран"));
                            finalAction = true;
                            anyProgress = true;
                        }
                        else
                        {
                            _ctx.Log.Warn("Грядка готова, но не найдена кнопка «" + button + "» (нужен шаблон " + button + ".bmp)");
                        }
                        break;
                    }

                    bool progressed = false;

                    // 2) Паразиты — лечим нужным химикатом
                    foreach (ParasiteMap p in cfg.Parasites)
                    {
                        if (string.IsNullOrWhiteSpace(p.Parasite)) continue;
                        Match? hit = _ctx.Vision.Find(p.Parasite, thr);
                        if (hit == null) continue;
                        string pName = string.IsNullOrWhiteSpace(p.Name) ? p.Parasite : p.Name;
                        string chemical = p.Chemical;
                        if (string.IsNullOrWhiteSpace(p.HotbarKey))
                        {
                            _ctx.Log.Warn("Паразит «" + pName + "»: не задана горячая клавиша химиката (hotbarKey в parasites[])");
                            break;
                        }
                        if (_ctx.State.Inventory.GetCount(chemical) <= 0)
                        {
                            _ctx.Log.Warn("Паразит «" + pName + "»: химикат «" + chemical + "» закончился");
                            break;
                        }
                        if (game.UseHotbarOnBed(p.HotbarKey, bed))
                        {
                            _ctx.State.Inventory.AddCount(chemical, -1);
                            _ctx.Log.Info("Паразит «" + pName + "»: применён химикат «" + chemical + "» (осталось " + _ctx.State.Inventory.GetCount(chemical) + ")");
                            progressed = true;
                        }
                        break; // один паразит за итерацию — перепроверяем экран
                    }
                    if (progressed)
                    {
                        anyProgress = true;
                        game.SleepInterruptible(cfg.Timing.AfterActionMs);
                        continue;
                    }

                    // 3) Удобрение почвы
                    if (Exists(t.NeedsFertilizer, thr))
                    {
                        if (_ctx.State.Inventory.Fertilizer > 0)
                        {
                            if (game.UseHotbarOnBed(cfg.FertilizerHotbarKey, bed))
                            {
                                _ctx.State.Inventory.AddCount("fertilizer", -1);
                                _ctx.Log.Info("Внесено удобрение (осталось " + _ctx.State.Inventory.Fertilizer + ")");
                                progressed = true;
                                anyProgress = true;
                            }
                        }
                        else
                        {
                            _ctx.Log.Warn("Нужно удобрение, но оно закончилось");
                        }
                        if (progressed)
                        {
                            game.SleepInterruptible(cfg.Timing.AfterActionMs);
                            continue;
                        }
                    }

                    // 4) Полив с точным расчётом литров
                    bool moistureLow = Exists(t.MoistureLow, thr);
                    bool moistureFull = Exists(t.MoistureFull, thr);
                    if (moistureLow && !moistureFull)
                    {
                        if (_watering.WaterBed(game, bed))
                        {
                            progressed = true;
                            anyProgress = true;
                        }
                        if (progressed)
                        {
                            game.SleepInterruptible(cfg.Timing.AfterActionMs);
                            continue;
                        }
                    }

                    // 5) Посадка на пустую грядку
                    if (cfg.Planting.PlantWhenEmpty && Exists(t.EmptyBed, thr))
                    {
                        if (_ctx.State.Inventory.Seeds > 0)
                        {
                            if (game.UseHotbarOnBed(cfg.Planting.SeedHotbarKey, bed))
                            {
                                _ctx.State.Inventory.AddCount("seed", -1);
                                _ctx.State.Bed(bed.Name).WateredLiters = 0; // новое растение — новый отсчёт полива
                                _ctx.Log.Info("Посажено растение (семян осталось " + _ctx.State.Inventory.Seeds + ")");
                                progressed = true;
                                anyProgress = true;
                            }
                        }
                        else
                        {
                            _ctx.Log.Warn("Грядка пуста, но семена закончились");
                        }
                        if (progressed)
                        {
                            game.SleepInterruptible(cfg.Timing.AfterActionMs);
                            continue;
                        }
                    }

                    if (!progressed)
                        break;
                }
            }
            finally
            {
                game.CloseBedPanel();
            }

            if (!finalAction && !anyProgress)
            {
                if (cropDetected)
                    _ctx.Log.Info("Грядка «" + bed.Name + "»: растение распознано, сейчас растёт — действий не требуется");
                else if (!cropSampleAvailable)
                    _ctx.Log.Info("Грядка «" + bed.Name + "»: действий нет; добавьте образец «Растение есть» в мастере настройки для наглядного распознавания");
                else
                    _ctx.Log.Info("Грядка «" + bed.Name + "»: не найдено состояние для действия (проверьте образцы шаблонов)");
            }
            return finalAction;
        }

        private bool Exists(string? templateName, double threshold)
        {
            if (string.IsNullOrWhiteSpace(templateName))
                return false;
            return _ctx.Vision.Exists(templateName, threshold);
        }
    }
}
