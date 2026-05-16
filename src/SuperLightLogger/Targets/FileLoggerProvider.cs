using System;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace SuperLightLogger
{
    /// <summary>
    /// SuperLightLogger 内蔵のファイルターゲット用 <see cref="ILoggerProvider"/>。
    /// </summary>
    [ProviderAlias("SuperLightFile")]
    public sealed class FileLoggerProvider : ILoggerProvider
    {
        private readonly FileTargetOptions _options;
        private readonly IFileTargetWriter _writer;
        private readonly ConcurrentDictionary<string, FileLogger> _loggers
            = new ConcurrentDictionary<string, FileLogger>(StringComparer.Ordinal);

        /// <summary>
        /// 指定した <see cref="FileTargetOptions"/> でプロバイダを構築する。
        /// </summary>
        /// <remarks>
        /// 通常は <see cref="SLLogFileTargetExtensions.AddSuperLightFile(ILoggingBuilder, Action{FileTargetOptions})"/>
        /// 経由で登録することを推奨する。直接 <c>new</c> した場合は呼び出し側で
        /// <see cref="Dispose"/> を確実に呼ぶ責任がある (DI コンテナはインスタンス登録された
        /// 外部所有のプロバイダを破棄しないため、リソースリークの原因になる)。
        /// </remarks>
        public FileLoggerProvider(FileTargetOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));

            IFileTargetWriter writer = new FileTargetWriter(_options);
            if (_options.Async)
            {
                writer = new AsyncFileQueue(
                    writer,
                    _options.AsyncBufferSize,
                    _options.AsyncFlushInterval,
                    _options.AsyncDiscardOnFull);
            }
            _writer = writer;
        }

        /// <inheritdoc />
        public ILogger CreateLogger(string categoryName)
            => _loggers.GetOrAdd(categoryName, name => new FileLogger(name, _writer, _options.MinLevel));

        /// <summary>
        /// Day-2 Ops 観点でこの Provider の現在の実行時統計を取得する。
        /// </summary>
        /// <remarks>
        /// Async モードでキューが溢れていないか、ワーカーが内部例外を起こしていないかを
        /// 外部から監視するための観測点。本番では <see cref="System.Threading.Timer"/> 等で定期的に
        /// 呼び出し、Application Insights / Prometheus / Datadog 等にメトリクスとして送ることを想定。
        /// 値はベストエフォートで race の余地のある近似値 (詳細は <see cref="FileTargetStatistics"/>)。
        /// </remarks>
        /// <returns>Async モードなら実値入りの統計、同期モードでは <see cref="FileTargetStatistics.IsAsyncMode"/>=false の空統計。</returns>
        public FileTargetStatistics GetStatistics()
        {
            if (_writer is AsyncFileQueue asyncQueue)
            {
                return new FileTargetStatistics(
                    isAsyncMode: true,
                    discardedLogEventCount: asyncQueue.DiscardedCount,
                    workerErrorCount: asyncQueue.WorkerErrorCount,
                    queueDepth: asyncQueue.QueueDepth);
            }
            return FileTargetStatistics.SyncMode;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            try { _writer.Dispose(); } catch { /* ignored */ }
        }
    }
}
