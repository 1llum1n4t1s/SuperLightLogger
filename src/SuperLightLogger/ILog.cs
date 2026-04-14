using System;

namespace SuperLightLogger
{
    /// <summary>
    /// log4net互換のログインターフェース。
    /// Microsoft.Extensions.Logging.ILoggerへの薄いラッパーとして機能する。
    /// </summary>
    public interface ILog
    {
        #region IsEnabled プロパティ

        /// <summary>Traceレベルが有効かどうか（NLog互換）。</summary>
        bool IsTraceEnabled { get; }

        /// <summary>Debugレベルが有効かどうか。</summary>
        bool IsDebugEnabled { get; }

        /// <summary>Infoレベルが有効かどうか。</summary>
        bool IsInfoEnabled { get; }

        /// <summary>Warnレベルが有効かどうか。</summary>
        bool IsWarnEnabled { get; }

        /// <summary>Errorレベルが有効かどうか。</summary>
        bool IsErrorEnabled { get; }

        /// <summary>Fatalレベルが有効かどうか。</summary>
        bool IsFatalEnabled { get; }

        #endregion

        #region Trace（NLog互換）

        /// <summary>Traceレベルでログを出力する。</summary>
        void Trace(object? message);

        /// <summary>Traceレベルでログと例外を出力する。</summary>
        void Trace(object? message, Exception? exception);

        /// <summary>Traceレベルでフォーマット済みログを出力する。</summary>
        void TraceFormat(string format, params object?[] args);

        /// <summary>Traceレベルでフォーマット済みログを出力する（1引数）。</summary>
        void TraceFormat(string format, object? arg0);

        /// <summary>Traceレベルでフォーマット済みログを出力する（2引数）。</summary>
        void TraceFormat(string format, object? arg0, object? arg1);

        /// <summary>Traceレベルでフォーマット済みログを出力する（3引数）。</summary>
        void TraceFormat(string format, object? arg0, object? arg1, object? arg2);

        /// <summary>Traceレベルでフォーマットプロバイダ付きログを出力する。</summary>
        void TraceFormat(IFormatProvider? provider, string format, params object?[] args);

        #endregion

        #region Debug

        /// <summary>Debugレベルでログを出力する。</summary>
        void Debug(object? message);

        /// <summary>Debugレベルでログと例外を出力する。</summary>
        void Debug(object? message, Exception? exception);

        /// <summary>Debugレベルでフォーマット済みログを出力する。</summary>
        void DebugFormat(string format, params object?[] args);

        /// <summary>Debugレベルでフォーマット済みログを出力する（1引数）。</summary>
        void DebugFormat(string format, object? arg0);

        /// <summary>Debugレベルでフォーマット済みログを出力する（2引数）。</summary>
        void DebugFormat(string format, object? arg0, object? arg1);

        /// <summary>Debugレベルでフォーマット済みログを出力する（3引数）。</summary>
        void DebugFormat(string format, object? arg0, object? arg1, object? arg2);

        /// <summary>Debugレベルでフォーマットプロバイダ付きログを出力する。</summary>
        void DebugFormat(IFormatProvider? provider, string format, params object?[] args);

        #endregion

        #region Info

        /// <summary>Infoレベルでログを出力する。</summary>
        void Info(object? message);

        /// <summary>Infoレベルでログと例外を出力する。</summary>
        void Info(object? message, Exception? exception);

        /// <summary>Infoレベルでフォーマット済みログを出力する。</summary>
        void InfoFormat(string format, params object?[] args);

        /// <summary>Infoレベルでフォーマット済みログを出力する（1引数）。</summary>
        void InfoFormat(string format, object? arg0);

        /// <summary>Infoレベルでフォーマット済みログを出力する（2引数）。</summary>
        void InfoFormat(string format, object? arg0, object? arg1);

        /// <summary>Infoレベルでフォーマット済みログを出力する（3引数）。</summary>
        void InfoFormat(string format, object? arg0, object? arg1, object? arg2);

        /// <summary>Infoレベルでフォーマットプロバイダ付きログを出力する。</summary>
        void InfoFormat(IFormatProvider? provider, string format, params object?[] args);

        #endregion

        #region Warn

        /// <summary>Warnレベルでログを出力する。</summary>
        void Warn(object? message);

        /// <summary>Warnレベルでログと例外を出力する。</summary>
        void Warn(object? message, Exception? exception);

        /// <summary>Warnレベルでフォーマット済みログを出力する。</summary>
        void WarnFormat(string format, params object?[] args);

        /// <summary>Warnレベルでフォーマット済みログを出力する（1引数）。</summary>
        void WarnFormat(string format, object? arg0);

        /// <summary>Warnレベルでフォーマット済みログを出力する（2引数）。</summary>
        void WarnFormat(string format, object? arg0, object? arg1);

        /// <summary>Warnレベルでフォーマット済みログを出力する（3引数）。</summary>
        void WarnFormat(string format, object? arg0, object? arg1, object? arg2);

        /// <summary>Warnレベルでフォーマットプロバイダ付きログを出力する。</summary>
        void WarnFormat(IFormatProvider? provider, string format, params object?[] args);

        #endregion

        #region Error

        /// <summary>Errorレベルでログを出力する。</summary>
        void Error(object? message);

        /// <summary>Errorレベルでログと例外を出力する。</summary>
        void Error(object? message, Exception? exception);

        /// <summary>Errorレベルでフォーマット済みログを出力する。</summary>
        void ErrorFormat(string format, params object?[] args);

        /// <summary>Errorレベルでフォーマット済みログを出力する（1引数）。</summary>
        void ErrorFormat(string format, object? arg0);

        /// <summary>Errorレベルでフォーマット済みログを出力する（2引数）。</summary>
        void ErrorFormat(string format, object? arg0, object? arg1);

        /// <summary>Errorレベルでフォーマット済みログを出力する（3引数）。</summary>
        void ErrorFormat(string format, object? arg0, object? arg1, object? arg2);

        /// <summary>Errorレベルでフォーマットプロバイダ付きログを出力する。</summary>
        void ErrorFormat(IFormatProvider? provider, string format, params object?[] args);

        #endregion

        #region Fatal

        /// <summary>Fatalレベルでログを出力する。</summary>
        void Fatal(object? message);

        /// <summary>Fatalレベルでログと例外を出力する。</summary>
        void Fatal(object? message, Exception? exception);

        /// <summary>Fatalレベルでフォーマット済みログを出力する。</summary>
        void FatalFormat(string format, params object?[] args);

        /// <summary>Fatalレベルでフォーマット済みログを出力する（1引数）。</summary>
        void FatalFormat(string format, object? arg0);

        /// <summary>Fatalレベルでフォーマット済みログを出力する（2引数）。</summary>
        void FatalFormat(string format, object? arg0, object? arg1);

        /// <summary>Fatalレベルでフォーマット済みログを出力する（3引数）。</summary>
        void FatalFormat(string format, object? arg0, object? arg1, object? arg2);

        /// <summary>Fatalレベルでフォーマットプロバイダ付きログを出力する。</summary>
        void FatalFormat(IFormatProvider? provider, string format, params object?[] args);

        #endregion
    }
}
