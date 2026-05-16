using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace SuperLightLogger
{
    /// <summary>
    /// <see cref="ILoggingBuilder"/> に SuperLightLogger 内蔵ファイルターゲットを追加する拡張メソッド群。
    /// </summary>
    /// <remarks>
    /// <para>
    /// クラス名が <c>SLLogFileTargetExtensions</c> と SuperLightLogger の略称プレフィックス付きなのは、
    /// Serilog / NLog / 他社ファイルロガー系ライブラリで多用される
    /// <c>FileLoggerExtensions</c> という一般名と FQN 参照や拡張メソッド解決で衝突しないようにするため。
    /// (1.0.3 → 1.0.4 で <c>LoggingBuilderExtensions</c> → <c>SLLogBuilderExtensions</c>
    ///  にリネームしたのと同じ問題への対策。)
    /// </para>
    /// </remarks>
    public static class SLLogFileTargetExtensions
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

        /// <summary>
        /// ファイル名だけ指定してファイルターゲットを登録する最速ショートカット。
        /// 他オプションはデフォルト (Layout / Encoding / KeepFileOpen など) のまま。
        /// </summary>
        /// <param name="builder">構成中の <see cref="ILoggingBuilder"/>。</param>
        /// <param name="fileName">出力先ファイル名 (NLog レイアウトテンプレート使用可)。</param>
        public static ILoggingBuilder AddSuperLightFile(this ILoggingBuilder builder, string fileName)
        {
            if (builder == null) throw new ArgumentNullException(nameof(builder));
            if (fileName == null) throw new ArgumentNullException(nameof(fileName));
            return builder.AddSuperLightFile(opts => opts.FileName = fileName);
        }
    }
}
