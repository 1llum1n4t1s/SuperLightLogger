using System;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;

namespace SuperLightLogger
{
    /// <summary>
    /// <see cref="ILog"/>の内部実装。
    /// <see cref="Microsoft.Extensions.Logging.ILogger"/>に委譲する。
    /// </summary>
    internal sealed class Log : ILog
    {
        private readonly Microsoft.Extensions.Logging.ILogger _logger;

        internal Log(Microsoft.Extensions.Logging.ILogger logger)
        {
            _logger = logger;
        }

        /// <summary>内部の<see cref="Microsoft.Extensions.Logging.ILogger"/>インスタンスを取得する。</summary>
        internal Microsoft.Extensions.Logging.ILogger InnerLogger => _logger;

        #region IsEnabled プロパティ

        /// <inheritdoc />
        public bool IsTraceEnabled => _logger.IsEnabled(LogLevel.Trace);

        /// <inheritdoc />
        public bool IsDebugEnabled => _logger.IsEnabled(LogLevel.Debug);

        /// <inheritdoc />
        public bool IsInfoEnabled => _logger.IsEnabled(LogLevel.Information);

        /// <inheritdoc />
        public bool IsWarnEnabled => _logger.IsEnabled(LogLevel.Warning);

        /// <inheritdoc />
        public bool IsErrorEnabled => _logger.IsEnabled(LogLevel.Error);

        /// <inheritdoc />
        public bool IsFatalEnabled => _logger.IsEnabled(LogLevel.Critical);

        #endregion

        #region ヘルパーメソッド

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void LogMessage(LogLevel level, object? message, Exception? exception)
        {
            if (!_logger.IsEnabled(level)) return;
#pragma warning disable CA2254 // テンプレートは定数ではないが、log4net互換のため意図的
            _logger.Log(level, 0, exception, message?.ToString());
#pragma warning restore CA2254
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void LogFormatted(LogLevel level, IFormatProvider? provider, string format, object?[] args)
        {
            if (!_logger.IsEnabled(level)) return;
            var msg = provider != null
                ? string.Format(provider, format, args)
                : string.Format(format, args);
#pragma warning disable CA2254
            _logger.Log(level, 0, null, msg);
#pragma warning restore CA2254
        }

        #endregion

        #region Trace

        /// <inheritdoc />
        public void Trace(object? message) => LogMessage(LogLevel.Trace, message, null);

        /// <inheritdoc />
        public void Trace(object? message, Exception? exception) => LogMessage(LogLevel.Trace, message, exception);

        /// <inheritdoc />
        public void TraceFormat(string format, params object?[] args) => LogFormatted(LogLevel.Trace, null, format, args);

        /// <inheritdoc />
        public void TraceFormat(string format, object? arg0) => LogFormatted(LogLevel.Trace, null, format, new[] { arg0 });

        /// <inheritdoc />
        public void TraceFormat(string format, object? arg0, object? arg1) => LogFormatted(LogLevel.Trace, null, format, new[] { arg0, arg1 });

        /// <inheritdoc />
        public void TraceFormat(string format, object? arg0, object? arg1, object? arg2) => LogFormatted(LogLevel.Trace, null, format, new[] { arg0, arg1, arg2 });

        /// <inheritdoc />
        public void TraceFormat(IFormatProvider? provider, string format, params object?[] args) => LogFormatted(LogLevel.Trace, provider, format, args);

        #endregion

        #region Debug

        /// <inheritdoc />
        public void Debug(object? message) => LogMessage(LogLevel.Debug, message, null);

        /// <inheritdoc />
        public void Debug(object? message, Exception? exception) => LogMessage(LogLevel.Debug, message, exception);

        /// <inheritdoc />
        public void DebugFormat(string format, params object?[] args) => LogFormatted(LogLevel.Debug, null, format, args);

        /// <inheritdoc />
        public void DebugFormat(string format, object? arg0) => LogFormatted(LogLevel.Debug, null, format, new[] { arg0 });

        /// <inheritdoc />
        public void DebugFormat(string format, object? arg0, object? arg1) => LogFormatted(LogLevel.Debug, null, format, new[] { arg0, arg1 });

        /// <inheritdoc />
        public void DebugFormat(string format, object? arg0, object? arg1, object? arg2) => LogFormatted(LogLevel.Debug, null, format, new[] { arg0, arg1, arg2 });

        /// <inheritdoc />
        public void DebugFormat(IFormatProvider? provider, string format, params object?[] args) => LogFormatted(LogLevel.Debug, provider, format, args);

        #endregion

        #region Info

        /// <inheritdoc />
        public void Info(object? message) => LogMessage(LogLevel.Information, message, null);

        /// <inheritdoc />
        public void Info(object? message, Exception? exception) => LogMessage(LogLevel.Information, message, exception);

        /// <inheritdoc />
        public void InfoFormat(string format, params object?[] args) => LogFormatted(LogLevel.Information, null, format, args);

        /// <inheritdoc />
        public void InfoFormat(string format, object? arg0) => LogFormatted(LogLevel.Information, null, format, new[] { arg0 });

        /// <inheritdoc />
        public void InfoFormat(string format, object? arg0, object? arg1) => LogFormatted(LogLevel.Information, null, format, new[] { arg0, arg1 });

        /// <inheritdoc />
        public void InfoFormat(string format, object? arg0, object? arg1, object? arg2) => LogFormatted(LogLevel.Information, null, format, new[] { arg0, arg1, arg2 });

        /// <inheritdoc />
        public void InfoFormat(IFormatProvider? provider, string format, params object?[] args) => LogFormatted(LogLevel.Information, provider, format, args);

        #endregion

        #region Warn

        /// <inheritdoc />
        public void Warn(object? message) => LogMessage(LogLevel.Warning, message, null);

        /// <inheritdoc />
        public void Warn(object? message, Exception? exception) => LogMessage(LogLevel.Warning, message, exception);

        /// <inheritdoc />
        public void WarnFormat(string format, params object?[] args) => LogFormatted(LogLevel.Warning, null, format, args);

        /// <inheritdoc />
        public void WarnFormat(string format, object? arg0) => LogFormatted(LogLevel.Warning, null, format, new[] { arg0 });

        /// <inheritdoc />
        public void WarnFormat(string format, object? arg0, object? arg1) => LogFormatted(LogLevel.Warning, null, format, new[] { arg0, arg1 });

        /// <inheritdoc />
        public void WarnFormat(string format, object? arg0, object? arg1, object? arg2) => LogFormatted(LogLevel.Warning, null, format, new[] { arg0, arg1, arg2 });

        /// <inheritdoc />
        public void WarnFormat(IFormatProvider? provider, string format, params object?[] args) => LogFormatted(LogLevel.Warning, provider, format, args);

        #endregion

        #region Error

        /// <inheritdoc />
        public void Error(object? message) => LogMessage(LogLevel.Error, message, null);

        /// <inheritdoc />
        public void Error(object? message, Exception? exception) => LogMessage(LogLevel.Error, message, exception);

        /// <inheritdoc />
        public void ErrorFormat(string format, params object?[] args) => LogFormatted(LogLevel.Error, null, format, args);

        /// <inheritdoc />
        public void ErrorFormat(string format, object? arg0) => LogFormatted(LogLevel.Error, null, format, new[] { arg0 });

        /// <inheritdoc />
        public void ErrorFormat(string format, object? arg0, object? arg1) => LogFormatted(LogLevel.Error, null, format, new[] { arg0, arg1 });

        /// <inheritdoc />
        public void ErrorFormat(string format, object? arg0, object? arg1, object? arg2) => LogFormatted(LogLevel.Error, null, format, new[] { arg0, arg1, arg2 });

        /// <inheritdoc />
        public void ErrorFormat(IFormatProvider? provider, string format, params object?[] args) => LogFormatted(LogLevel.Error, provider, format, args);

        #endregion

        #region Fatal

        /// <inheritdoc />
        public void Fatal(object? message) => LogMessage(LogLevel.Critical, message, null);

        /// <inheritdoc />
        public void Fatal(object? message, Exception? exception) => LogMessage(LogLevel.Critical, message, exception);

        /// <inheritdoc />
        public void FatalFormat(string format, params object?[] args) => LogFormatted(LogLevel.Critical, null, format, args);

        /// <inheritdoc />
        public void FatalFormat(string format, object? arg0) => LogFormatted(LogLevel.Critical, null, format, new[] { arg0 });

        /// <inheritdoc />
        public void FatalFormat(string format, object? arg0, object? arg1) => LogFormatted(LogLevel.Critical, null, format, new[] { arg0, arg1 });

        /// <inheritdoc />
        public void FatalFormat(string format, object? arg0, object? arg1, object? arg2) => LogFormatted(LogLevel.Critical, null, format, new[] { arg0, arg1, arg2 });

        /// <inheritdoc />
        public void FatalFormat(IFormatProvider? provider, string format, params object?[] args) => LogFormatted(LogLevel.Critical, provider, format, args);

        #endregion
    }
}
