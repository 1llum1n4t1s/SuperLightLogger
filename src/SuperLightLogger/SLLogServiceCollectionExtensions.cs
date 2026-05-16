using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace SuperLightLogger
{
    /// <summary>
    /// DI統合のための拡張メソッド。
    /// </summary>
    /// <remarks>
    /// <para>
    /// クラス名が <c>SLLogServiceCollectionExtensions</c> と SuperLightLogger の略称プレフィックス付きなのは、
    /// ユーザーが同時に <c>using Microsoft.Extensions.DependencyInjection;</c> を書いていても
    /// MEL / AspNetCore / 各種サードパーティ DI 拡張ライブラリで多用される
    /// <c>ServiceCollectionExtensions</c> という一般名と FQN 参照や <c>using static</c> で
    /// 曖昧エラーを起こさないようにするため。
    /// (1.0.3 で <c>LoggingBuilderExtensions</c> の名前衝突を踏み、1.0.4 で <c>SLLogBuilderExtensions</c>
    ///  にリネームした経緯と同じ問題への対策。)
    /// </para>
    /// </remarks>
    public static class SLLogServiceCollectionExtensions
    {
        /// <summary>
        /// DIコンテナの<see cref="ILoggerFactory"/>をSuperLightLoggerに設定する。
        /// <c>var app = builder.Build();</c>の後に呼び出す。
        /// </summary>
        /// <param name="provider">構築済みのサービスプロバイダ。</param>
        /// <returns>同じサービスプロバイダ（チェーン呼び出し用）。</returns>
        /// <remarks>
        /// この経路で渡された <see cref="ILoggerFactory"/> は DI コンテナが所有するため、
        /// <see cref="LogManager.Shutdown"/> は呼ばないこと (DI コンテナが ServiceProvider 破棄時に
        /// 自動で Dispose する)。
        /// </remarks>
        /// <example>
        /// <code>
        /// var app = builder.Build();
        /// app.Services.UseSuperLightLogger();
        /// </code>
        /// </example>
        public static IServiceProvider UseSuperLightLogger(this IServiceProvider provider)
        {
            if (provider == null) throw new ArgumentNullException(nameof(provider));
            var factory = provider.GetRequiredService<ILoggerFactory>();
            // ownsFactory: false で渡すことで、LogManager.Shutdown() を誤って呼ばれても
            // DI コンテナが所有する factory を勝手に Dispose しないようにする。
            // (DI コンテナが ServiceProvider 破棄時に自動で Dispose する)
            LogManager.Configure(factory, ownsFactory: false);
            return provider;
        }
    }
}
