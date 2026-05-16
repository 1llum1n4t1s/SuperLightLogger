using System;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace SuperLightLogger
{
    /// <summary>
    /// <see cref="FileLoggerProvider"/> が払い出す <see cref="ILogger"/>。
    /// 内部的には共有の <see cref="IFileTargetWriter"/> へ転送するだけのシンウォール。
    /// </summary>
    internal sealed class FileLogger : ILogger
    {
        private readonly string _category;
        private readonly IFileTargetWriter _writer;
        private readonly LogLevel _minLevel;

        public FileLogger(string category, IFileTargetWriter writer, LogLevel minLevel)
        {
            _category = category;
            _writer = writer;
            _minLevel = minLevel;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= _minLevel && logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            if (formatter == null) return;

            string message = formatter(state, exception);
            // 例外しか持たないログでも、message が空ならスキップしない (NLog 互換)
            // ThreadName は Async モード対策で必ず呼び出し元スレッドで取得しておく
            // (バックグラウンドワーカー上で render すると "SuperLightLogger.FileTarget.Async" になってしまう)
            var ev = new LogEvent(
                DateTime.Now,
                logLevel,
                _category,
                message ?? string.Empty,
                exception,
#if NET5_0_OR_GREATER
                Environment.CurrentManagedThreadId,
#else
                Thread.CurrentThread.ManagedThreadId,
#endif
                Thread.CurrentThread.Name
            );
            _writer.Write(in ev);
        }
    }
}
