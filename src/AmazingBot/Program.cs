using System;
using System.Collections.Generic;
using System.Text;
using AmazingBot.Config;
using AmazingBot.Core;
using AmazingBot.Native;
using AmazingBot.Ui;

namespace AmazingBot
{
    public static class Program
    {
        [STAThread]
        public static int Main(string[] args)
        {
            try
            {
                Console.OutputEncoding = Encoding.UTF8;
                Console.InputEncoding = Encoding.UTF8;
            }
            catch (Exception)
            {
                // некоторые терминалы не дают менять кодировку — не критично
            }

            try
            {
                NativeMethods.SetProcessDPIAware();
            }
            catch (Exception)
            {
                // нет прав или старая Windows — координаты всё равно совпадут при 100% масштабировании
            }

            string? configArg = null;
            string? recordRoute = null;
            bool once = false;
            bool help = false;

            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                switch (arg)
                {
                    case "--config":
                        if (i + 1 < args.Length) configArg = args[++i];
                        break;
                    case "--once":
                        once = true;
                        break;
                    case "--record-route":
                        if (i + 1 < args.Length) recordRoute = args[++i];
                        else help = true;
                        break;
                    case "--help":
                    case "-h":
                        help = true;
                        break;
                    default:
                        Console.WriteLine("Неизвестный аргумент: " + arg);
                        help = true;
                        break;
                }
            }

            if (help)
            {
                PrintUsage();
                return 0;
            }

            string configPath = ConfigService.FindConfigPath(configArg);
            BotConfig cfg = ConfigService.Load(configPath, null, out List<string> problems);
            var log = new Logger(cfg.LogsDir);
            log.MinimumLevel = ParseLogLevel(cfg.LogLevel);
            foreach (string problem in problems)
                log.Warn("Конфиг: " + problem);

            var menu = new ConsoleMenu(cfg, configPath, log);

            if (recordRoute != null)
            {
                menu.RecordRoute(recordRoute);
                return 0;
            }
            if (once)
            {
                var bot = new Bot(cfg, configPath, log);
                return bot.Run(true);
            }

            menu.Run();
            return 0;
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

        private static void PrintUsage()
        {
            Console.WriteLine("AmazingBot — циклы профессий Amazing Online");
            Console.WriteLine();
            Console.WriteLine("Использование:");
            Console.WriteLine("  AmazingBot                      — интерактивное меню");
            Console.WriteLine("  AmazingBot --once               — выполнить один цикл профессии");
            Console.WriteLine("  AmazingBot --record-route NAME  — записать цикл (F10 — конец записи)");
            Console.WriteLine("  AmazingBot --config PATH        — использовать указанный config.json");
            Console.WriteLine();
            Console.WriteLine("F12 — экстренная остановка бота в любой момент.");
        }
    }
}
