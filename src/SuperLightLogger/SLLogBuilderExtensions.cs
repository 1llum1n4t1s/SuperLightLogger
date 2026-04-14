using System;
using Microsoft.Extensions.Logging;

namespace SuperLightLogger
{
    /// <summary>
    /// <see cref="ILoggingBuilder"/> に対する SuperLightLogger 流の拡張メソッド。
    /// </summary>
    /// <remarks>
    /// <para>
    /// クラス名が <c>SLLogBuilderExtensions</c> と SuperLightLogger の略称プレフィックス付きなのは、
    /// ユーザーが同時に <c>using Microsoft.Extensions.Logging;</c> を書いていても
    /// <c>Microsoft.Extensions.Logging.LoggingBuilderExtensions</c> と
    /// 名前衝突しないようにするため。
    /// (1.0.3 初期実装は <c>LoggingBuilderExtensions</c> で出そうとしたが、
    ///  MEL 側の同名ヘルパ型と衝突して FQN 参照が曖昧エラーになる
    ///  source break を引き起こすことが Codex レビューで判明し、
    ///  SLLog プレフィックスに変更した経緯がある。)
    /// </para>
    /// <para>
    /// このクラスの最大の目的は、ユーザーが <c>using Microsoft.Extensions.Logging;</c>
    /// を追記せずとも最小ログレベルの設定ができるようにすること。
    /// MEL を using すると <c>LogLevel</c> 型が名前空間に流れ込み、
    /// 既存コードが持つ同名型 (例: <c>Cube.LogLevel</c>) と衝突してしまう問題への対策である。
    /// </para>
    /// <para>
    /// 使用例:
    /// <code>
    /// using SuperLightLogger;
    ///
    /// LogManager.Configure(builder =&gt;
    /// {
    ///     builder.SetMinimumLevel("Info");            // MEL の using 不要
    ///     builder.AddSuperLightFile("logs/app.log");
    /// });
    /// </code>
    /// </para>
    /// </remarks>
    public static class SLLogBuilderExtensions
    {
        /// <summary>
        /// log4net / NLog 形式の文字列でミニマムログレベルを設定する。
        /// 受理値: Trace / Debug / Info / Warn / Error / Fatal / None (大文字小文字区別なし)。
        /// </summary>
        /// <param name="builder">構成中の <see cref="ILoggingBuilder"/>。</param>
        /// <param name="level">レベル名 (例: "Info")。</param>
        /// <returns>チェーン呼び出し用に同じ <paramref name="builder"/>。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> が null。</exception>
        /// <exception cref="ArgumentException"><paramref name="level"/> が未知。</exception>
        public static ILoggingBuilder SetMinimumLevel(this ILoggingBuilder builder, string level)
        {
            if (builder == null) throw new ArgumentNullException(nameof(builder));
            // FQN 経由で MEL 側の拡張メソッドを呼ぶ (拡張メソッド名が衝突するため)。
            Microsoft.Extensions.Logging.LoggingBuilderExtensions.SetMinimumLevel(
                builder, SLLogLevels.Parse(level));
            return builder;
        }
    }
}
