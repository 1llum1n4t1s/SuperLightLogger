using System;
using System.Collections.Concurrent;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
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
        /// 通常は <see cref="FileLoggerExtensions.AddSuperLightFile(ILoggingBuilder, Action{FileTargetOptions})"/>
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

        /// <inheritdoc />
        public void Dispose()
        {
            try { _writer.Dispose(); } catch { /* ignored */ }
        }
    }

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

    /// <summary>
    /// <see cref="ILoggingBuilder"/> に SuperLightLogger 内蔵ファイルターゲットを追加する拡張メソッド群。
    /// </summary>
    public static class FileLoggerExtensions
    {
        /// <summary>
        /// SuperLightLogger 内蔵のファイルターゲットを <see cref="ILoggingBuilder"/> に登録する。
        /// NLog の <c>File Target</c> 相当の機能 (パステンプレート / レイアウト / 日付ローリング /
        /// サイズアーカイブ / 最大保持数 / ヘッダーフッター / 非同期書込) を提供する。
        /// </summary>
        /// <param name="builder">構成中の <see cref="ILoggingBuilder"/>。</param>
        /// <param name="configure"><see cref="FileTargetOptions"/> を構成するアクション。</param>
        public static ILoggingBuilder AddSuperLightFile(this ILoggingBuilder builder, Action<FileTargetOptions> configure)
        {
            if (builder == null) throw new ArgumentNullException(nameof(builder));
            if (configure == null) throw new ArgumentNullException(nameof(configure));
            var options = new FileTargetOptions();
            configure(options);
            // factory delegate で渡すことで DI コンテナがインスタンスをトラックし、
            // ServiceProvider 破棄時に FileLoggerProvider.Dispose() を呼んでくれる。
            // (instance 直接登録の AddSingleton は外部所有扱いで dispose されない)
            builder.Services.AddSingleton<ILoggerProvider>(_ => new FileLoggerProvider(options));
            return builder;
        }

        /// <summary>
        /// 既に構成済みの <see cref="FileTargetOptions"/> を <see cref="ILoggingBuilder"/> に登録する。
        /// </summary>
        public static ILoggingBuilder AddSuperLightFile(this ILoggingBuilder builder, FileTargetOptions options)
        {
            if (builder == null) throw new ArgumentNullException(nameof(builder));
            if (options == null) throw new ArgumentNullException(nameof(options));
            builder.Services.AddSingleton<ILoggerProvider>(_ => new FileLoggerProvider(options));
            return builder;
        }
    }
}
