using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace SuperLightLogger
{
    /// <summary>
    /// DI統合のための拡張メソッド。
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// DIコンテナの<see cref="ILoggerFactory"/>をSuperLightLoggerに設定する。
        /// <c>var app = builder.Build();</c>の後に呼び出す。
        /// </summary>
        /// <param name="provider">構築済みのサービスプロバイダ。</param>
        /// <returns>同じサービスプロバイダ（チェーン呼び出し用）。</returns>
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
            LogManager.Configure(factory);
            return provider;
        }
    }
}
