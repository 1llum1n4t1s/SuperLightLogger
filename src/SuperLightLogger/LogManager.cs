using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SuperLightLogger
{
    /// <summary>
    /// log4net互換の静的ログマネージャー。
    /// <see cref="ILoggerFactory"/>を内部で保持し、<see cref="ILog"/>インスタンスを生成する。
    /// </summary>
    public static class LogManager
    {
        private static volatile ILoggerFactory? _factory;
        private static readonly object _lock = new object();
        private static bool _warningEmitted;

        /// <summary>
        /// 使用する<see cref="ILoggerFactory"/>を設定する。
        /// アプリケーション起動時に1回呼び出す。
        /// </summary>
        /// <param name="factory">使用するILoggerFactory。</param>
        public static void Configure(ILoggerFactory factory)
        {
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            lock (_lock)
            {
                _factory = factory;
                _warningEmitted = false;
            }
        }

        /// <summary>
        /// ビルダーパターンで<see cref="ILoggerFactory"/>を構成する。
        /// </summary>
        /// <param name="configure">ILoggingBuilderの構成アクション。</param>
        public static void Configure(Action<ILoggingBuilder> configure)
        {
            if (configure == null) throw new ArgumentNullException(nameof(configure));
            var factory = LoggerFactory.Create(configure);
            Configure(factory);
        }

        /// <summary>
        /// ログシステムをシャットダウンし、<see cref="ILoggerFactory"/>を破棄する。
        /// </summary>
        public static void Shutdown()
        {
            lock (_lock)
            {
                _factory?.Dispose();
                _factory = null;
                _warningEmitted = false;
            }
        }

        /// <summary>
        /// ファクトリを破棄せずにリセットする（テスト用）。
        /// </summary>
        public static void Reset()
        {
            lock (_lock)
            {
                _factory = null;
                _warningEmitted = false;
            }
        }

        /// <summary>
        /// 指定した型のロガーを取得する（log4net互換）。
        /// </summary>
        /// <param name="type">ロガー名に使用する型。</param>
        /// <returns>ILogインスタンス。</returns>
        public static ILog GetLogger(Type type)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));
            return GetLogger(type.FullName ?? type.Name);
        }

        /// <summary>
        /// 指定した名前のロガーを取得する。
        /// </summary>
        /// <param name="name">ロガー名。</param>
        /// <returns>ILogインスタンス。</returns>
        public static ILog GetLogger(string name)
        {
            if (name == null) throw new ArgumentNullException(nameof(name));
            var factory = EnsureFactory();
            var logger = factory.CreateLogger(name);
            return new Log(logger);
        }

        /// <summary>
        /// 呼び出し元クラスの名前でロガーを取得する（NLog互換）。
        /// </summary>
        /// <returns>ILogインスタンス。</returns>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static ILog GetCurrentClassLogger()
        {
            var frame = new StackFrame(1, false);
            var callingType = frame.GetMethod()?.DeclaringType;
            return GetLogger(callingType?.FullName ?? callingType?.Name ?? "Unknown");
        }

        private static ILoggerFactory EnsureFactory()
        {
            var f = _factory;
            if (f != null) return f;

            lock (_lock)
            {
                if (_factory != null) return _factory;
                if (!_warningEmitted)
                {
                    Console.Error.WriteLine(
                        "[SuperLightLogger] WARNING: LogManager.Configure() が呼び出されていません。" +
                        "NullLoggerFactory を使用します。ログは出力されません。");
                    _warningEmitted = true;
                }
                _factory = NullLoggerFactory.Instance;
                return _factory;
            }
        }
    }
}
