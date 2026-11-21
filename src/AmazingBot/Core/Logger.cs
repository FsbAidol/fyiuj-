using System;
using System.IO;
using System.Text;
using System.Threading;

namespace AmazingBot.Core
{
    public enum LogLevel
    {
        Debug = 0,
        Info = 1,
        Warn = 2,
        Error = 3
    }

    /// <summary>
    /// Лог в файл и в консоль одновременно. Потокобезопасен.
    /// </summary>
    public sealed class Logger
    {
        private readonly object _gate = new object();
        private readonly string _logDir;
        private readonly string _logFile;
        public LogLevel MinimumLevel { get; set; } = LogLevel.Info;

        /// <summary>Вызывается для каждой строки лога (используется GUI-меню для журнала).</summary>
        public event Action<string, LogLevel>? LineWritten;

        public Logger(string logDir)
        {
            _logDir = logDir;
            try
            {
                Directory.CreateDirectory(_logDir);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Не удалось создать каталог логов: " + ex.Message);
            }
            _logFile = Path.Combine(_logDir, "amazingbot-" + DateTime.Now.ToString("yyyyMMdd") + ".log");
        }

        public string LogFile => _logFile;

        public void Debug(string message) => Write(LogLevel.Debug, message);
        public void Info(string message) => Write(LogLevel.Info, message);
        public void Warn(string message) => Write(LogLevel.Warn, message);
        public void Error(string message) => Write(LogLevel.Error, message);

        public void Exception(string message, Exception ex)
        {
            Write(LogLevel.Error, message + ": " + ex.GetType().Name + ": " + ex.Message);
            Write(LogLevel.Debug, ex.StackTrace ?? "(нет стека)");
        }

        private void Write(LogLevel level, string message)
        {
            if (level < MinimumLevel) return;
            string line = "[" + DateTime.Now.ToString("HH:mm:ss") + "] [" + LevelText(level) + "] " + message;
            Action<string, LogLevel>? subscribers = LineWritten;
            if (subscribers != null)
            {
                try
                {
                    subscribers(line, level);
                }
                catch (Exception)
                {
                    // подписчик (GUI) не должен ронять бота
                }
            }
            lock (_gate)
            {
                ConsoleColor previous = Console.ForegroundColor;
                Console.ForegroundColor = ColorFor(level);
                try
                {
                    Console.WriteLine(line);
                }
                catch (Exception)
                {
                    // WinExe-сборка без консоли — писать некуда, это нормально
                }
                Console.ForegroundColor = previous;
                try
                {
                    File.AppendAllText(_logFile, line + Environment.NewLine, Encoding.UTF8);
                }
                catch (Exception)
                {
                    // лог-файл необязателен — бот не должен падать из-за него
                }
            }
        }

        private static string LevelText(LogLevel level)
        {
            switch (level)
            {
                case LogLevel.Debug: return "DEBUG";
                case LogLevel.Info: return "INFO ";
                case LogLevel.Warn: return "WARN ";
                case LogLevel.Error: return "ERROR";
                default: return "?????";
            }
        }

        private static ConsoleColor ColorFor(LogLevel level)
        {
            switch (level)
            {
                case LogLevel.Debug: return ConsoleColor.DarkGray;
                case LogLevel.Info: return ConsoleColor.White;
                case LogLevel.Warn: return ConsoleColor.Yellow;
                case LogLevel.Error: return ConsoleColor.Red;
                default: return ConsoleColor.White;
            }
        }
    }
}
