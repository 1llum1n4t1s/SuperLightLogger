using System;
using System.ComponentModel;
using System.Diagnostics;
#if NET6_0_OR_GREATER
using System.Diagnostics.CodeAnalysis;
#endif
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SuperLightLogger
{
    /// <summary>
    /// log4net互換の静的ログマネージャー。
    /// <see cref="ILoggerFactory"/>を内部で保持し、<see cref="ILog"/>インスタンスを生成する。
    /// </summary>
    /// <remarks>
    /// <para>
    /// このクラスはファクトリの「所有モデル」を 2 種類サポートする:
    /// </para>
    /// <list type="bullet">
    ///   <item><description>
    ///   <b>ライブラリ所有</b> (<see cref="Configure(Action{ILoggingBuilder})"/> 経路、
    ///   または <see cref="Configure(ILoggerFactory)"/> 単独経路) — <see cref="Shutdown"/> 時に
    ///   <see cref="IDisposable.Dispose"/> を呼んでファクトリを破棄する。
    ///   </description></item>
    ///   <item><description>
    ///   <b>DI コンテナ所有</b> (<see cref="Configure(ILoggerFactory, bool)"/> を
    ///   <c>ownsFactory: false</c> で呼んだ場合、典型的には
    ///   <see cref="SLLogServiceCollectionExtensions.UseSuperLightLogger"/> 経由) —
    ///   <see cref="Shutdown"/> はファクトリを破棄せず、DI コンテナ側の破棄に委ねる。
    ///   </description></item>
    /// </list>
    /// <para>
    /// この区別を設けない初期実装では、DI 経由で渡された factory を <see cref="Shutdown"/> で
    /// 勝手に破棄してしまい、既に払い出した <see cref="ILogger"/> を握る全コンポーネントが
    /// <see cref="ObjectDisposedException"/> 連鎖する事故があった。
    /// </para>
    /// </remarks>
    public static class LogManager
    {
        private static volatile ILoggerFactory? _factory;
        // _factory がライブラリ所有なら true、DI 所有 / NullLoggerFactory なら false。
        // _factory と必ず一緒に _lock 内で更新する。
        private static bool _ownsFactory;
        private static readonly object _lock = new object();
        private static bool _warningEmitted;

        /// <summary>
        /// 使用する<see cref="ILoggerFactory"/>を設定する (ライブラリ所有として扱う)。
        /// アプリケーション起動時に1回呼び出す。
        /// </summary>
        /// <remarks>
        /// このオーバーロードは旧 API 互換のため <see cref="Shutdown"/> 時に
        /// <paramref name="factory"/> を <see cref="IDisposable.Dispose"/> する。
        /// DI コンテナから取得した factory を渡す場合は
        /// <see cref="Configure(ILoggerFactory, bool)"/> を <c>ownsFactory: false</c> で呼ぶか、
        /// <see cref="SLLogServiceCollectionExtensions.UseSuperLightLogger"/> 経由で構成すること。
        /// </remarks>
        /// <param name="factory">使用する ILoggerFactory。</param>
        public static void Configure(ILoggerFactory factory)
            => Configure(factory, ownsFactory: true);

        /// <summary>
        /// 使用する<see cref="ILoggerFactory"/>と所有モデルを明示して設定する。
        /// </summary>
        /// <param name="factory">使用する ILoggerFactory。</param>
        /// <param name="ownsFactory">
        /// <c>true</c> ならライブラリ所有 (<see cref="Shutdown"/> 時に Dispose する)。
        /// <c>false</c> なら外部 (典型的には DI コンテナ) 所有 (<see cref="Shutdown"/> は触らない)。
        /// </param>
        public static void Configure(ILoggerFactory factory, bool ownsFactory)
        {
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            ILoggerFactory? previous;
            bool previousOwned;
            lock (_lock)
            {
                previous = _factory;
                previousOwned = _ownsFactory;
                _factory = factory;
                _ownsFactory = ownsFactory;
                _warningEmitted = false;
            }
            // 上書き前に保持していた旧 factory がライブラリ所有ならここで Dispose してリークを防ぐ。
            // DI 所有 or NullLoggerFactory (= previousOwned == false) は触らない (所有者が別途破棄する)。
            // lock 外で Dispose するのは、Dispose 内が他ロックを取得した場合のデッドロック回避。
            if (previous != null && previousOwned && !object.ReferenceEquals(previous, factory))
            {
                try { previous.Dispose(); } catch { /* ignored: 旧 factory の破棄失敗は伝播させない */ }
            }
        }

        /// <summary>
        /// ビルダーパターンで<see cref="ILoggerFactory"/>を構成する。
        /// </summary>
        /// <remarks>
        /// 内部で <see cref="LoggerFactory.Create"/> によりライブラリ所有 factory を生成するため、
        /// <see cref="Shutdown"/> 時に Dispose される。
        /// </remarks>
        /// <param name="configure">ILoggingBuilderの構成アクション。</param>
        public static void Configure(Action<ILoggingBuilder> configure)
        {
            if (configure == null) throw new ArgumentNullException(nameof(configure));
            var factory = LoggerFactory.Create(configure);
            Configure(factory, ownsFactory: true);
        }

        /// <summary>
        /// ログシステムをシャットダウンする。
        /// </summary>
        /// <remarks>
        /// ライブラリ所有モード (<see cref="Configure(ILoggerFactory)"/> または
        /// <see cref="Configure(Action{ILoggingBuilder})"/> 経路) で構成された場合のみ、
        /// 保持中の <see cref="ILoggerFactory"/> を <see cref="IDisposable.Dispose"/> する。
        /// DI コンテナ所有モード (<see cref="SLLogServiceCollectionExtensions.UseSuperLightLogger"/>
        /// 経路) では factory には触れず、DI コンテナ側の破棄に委ねる (= そもそも呼ぶ必要なし)。
        /// </remarks>
        public static void Shutdown()
        {
            ILoggerFactory? toDispose;
            lock (_lock)
            {
                toDispose = _ownsFactory ? _factory : null;
                _factory = null;
                _ownsFactory = false;
                _warningEmitted = false;
            }
            if (toDispose != null)
            {
                try { toDispose.Dispose(); } catch { /* ignored */ }
            }
        }

        /// <summary>
        /// ファクトリを破棄せずにリセットする（テスト専用 API）。
        /// </summary>
        /// <remarks>
        /// 本来は internal にすべき API だが、過去バージョンで public 公開された後方互換のため
        /// public のまま残している。テスト以外で呼ぶ用途はない (本番コードで呼ぶと
        /// FileLoggerProvider 等が握る FileStream がリークする)。IntelliSense では非表示扱い。
        /// log4net の <c>LogManager.ResetConfiguration()</c> とは別物なので注意。
        /// </remarks>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static void Reset()
        {
            lock (_lock)
            {
                _factory = null;
                _ownsFactory = false;
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
        /// 型パラメータでロガーを取得する（ネイティブAOT/トリミング安全）。
        /// </summary>
        /// <typeparam name="T">ロガー名に使用する型。</typeparam>
        /// <returns>ILogインスタンス。</returns>
        public static ILog GetLogger<T>() => GetLogger(typeof(T));

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
        /// <remarks>
        /// 内部で <see cref="StackFrame"/> によるリフレクションを利用するため、
        /// ネイティブAOTやトリミング環境では型情報が削られて正しい名前を取得できない場合がある。
        /// AOT環境では <see cref="GetLogger{T}"/> または <see cref="GetLogger(Type)"/> の使用を推奨。
        /// </remarks>
        /// <returns>ILogインスタンス。</returns>
#if NET6_0_OR_GREATER
        [RequiresUnreferencedCode("StackFrame.GetMethod() を使用するため、トリミング時に型情報が失われる可能性があります。AOT/Trim 環境では GetLogger<T>() を使用してください。")]
#endif
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
                // NullLoggerFactory は BCL のシングルトンなので所有しない (Dispose してはいけない)
                _ownsFactory = false;
                return _factory;
            }
        }
    }
}
