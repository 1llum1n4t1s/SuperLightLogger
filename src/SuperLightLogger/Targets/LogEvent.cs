using System;
using Microsoft.Extensions.Logging;

namespace SuperLightLogger
{
    /// <summary>
    /// 1ログイベントを表す内部値型。
    /// <see cref="LayoutRenderer"/> へ渡される情報を保持する。
    /// </summary>
    internal readonly struct LogEvent
    {
        public LogEvent(DateTime timestamp, LogLevel level, string logger, string message, Exception? exception, int threadId, string? threadName)
        {
            Timestamp = timestamp;
            Level = level;
            Logger = logger;
            Message = message;
            Exception = exception;
            ThreadId = threadId;
            ThreadName = threadName;
        }

        public DateTime Timestamp { get; }
        public LogLevel Level { get; }
        public string Logger { get; }
        public string Message { get; }
        public Exception? Exception { get; }
        public int ThreadId { get; }

        /// <summary>
        /// ログ呼び出し元スレッドの <c>Thread.Name</c>。Async モードでバックグラウンドスレッドの名前が
        /// 紛れ込まないよう、生成時 (= 呼び出し元スレッド上) でキャプチャしておく必要がある。
        /// </summary>
        public string? ThreadName { get; }

        /// <summary>
        /// パステンプレート用に最小限のフィールドだけ埋めた <see cref="LogEvent"/> を返す。
        /// </summary>
        public static LogEvent ForPath(DateTime timestamp)
            => new LogEvent(timestamp, LogLevel.None, string.Empty, string.Empty, null, 0, null);
    }
}
