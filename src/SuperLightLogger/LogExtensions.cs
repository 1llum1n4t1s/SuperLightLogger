using System;
using Microsoft.Extensions.Logging;

namespace SuperLightLogger
{
    /// <summary>
    /// Microsoft.Extensions.Logging式の構造化ログをサポートする拡張メソッド。
    /// log4netの<c>InfoFormat("{0}", x)</c>から段階的に
    /// <c>InfoStructured("{Name}", x)</c>へ移行する際に使用する。
    /// </summary>
    public static class LogExtensions
    {
        /// <summary>Traceレベルで構造化ログを出力する。</summary>
        public static void TraceStructured(this ILog log, string messageTemplate, params object?[] args)
            => LogStructured(log, LogLevel.Trace, null, messageTemplate, args);

        /// <summary>Debugレベルで構造化ログを出力する。</summary>
        public static void DebugStructured(this ILog log, string messageTemplate, params object?[] args)
            => LogStructured(log, LogLevel.Debug, null, messageTemplate, args);

        /// <summary>Infoレベルで構造化ログを出力する。</summary>
        public static void InfoStructured(this ILog log, string messageTemplate, params object?[] args)
            => LogStructured(log, LogLevel.Information, null, messageTemplate, args);

        /// <summary>Warnレベルで構造化ログを出力する。</summary>
        public static void WarnStructured(this ILog log, string messageTemplate, params object?[] args)
            => LogStructured(log, LogLevel.Warning, null, messageTemplate, args);

        /// <summary>Errorレベルで構造化ログを出力する。</summary>
        public static void ErrorStructured(this ILog log, string messageTemplate, params object?[] args)
            => LogStructured(log, LogLevel.Error, null, messageTemplate, args);

        /// <summary>Errorレベルで例外付き構造化ログを出力する。</summary>
        public static void ErrorStructured(this ILog log, Exception? exception, string messageTemplate, params object?[] args)
            => LogStructured(log, LogLevel.Error, exception, messageTemplate, args);

        /// <summary>Fatalレベルで構造化ログを出力する。</summary>
        public static void FatalStructured(this ILog log, string messageTemplate, params object?[] args)
            => LogStructured(log, LogLevel.Critical, null, messageTemplate, args);

        /// <summary>Fatalレベルで例外付き構造化ログを出力する。</summary>
        public static void FatalStructured(this ILog log, Exception? exception, string messageTemplate, params object?[] args)
            => LogStructured(log, LogLevel.Critical, exception, messageTemplate, args);

        private static void LogStructured(ILog log, LogLevel level, Exception? exception, string messageTemplate, object?[] args)
        {
            if (log is Log impl)
            {
                var logger = impl.InnerLogger;
                if (!logger.IsEnabled(level)) return;
#pragma warning disable CA2254
                logger.Log(level, 0, exception, messageTemplate, args);
#pragma warning restore CA2254
            }
            else
            {
                // ILogの独自実装に対するフォールバック
                var msg = string.Format(messageTemplate, args);
                switch (level)
                {
                    case LogLevel.Trace: log.Trace(msg, exception); break;
                    case LogLevel.Debug: log.Debug(msg, exception); break;
                    case LogLevel.Information: log.Info(msg, exception); break;
                    case LogLevel.Warning: log.Warn(msg, exception); break;
                    case LogLevel.Error: log.Error(msg, exception); break;
                    case LogLevel.Critical: log.Fatal(msg, exception); break;
                }
            }
        }
    }
}
