using System;
using Microsoft.Extensions.Logging;

namespace SuperLightLogger
{
    /// <summary>
    /// log4net / NLog 互換のログレベル表記を
    /// <see cref="Microsoft.Extensions.Logging.LogLevel"/> に変換するヘルパ。
    /// </summary>
    /// <remarks>
    /// <para>
    /// クラス名が <c>SLLogLevels</c> と SuperLightLogger の略称プレフィックス付きなのは、
    /// ユーザーが同時に <c>using Microsoft.Extensions.Logging;</c> を書いていても
    /// 名前衝突が起きないようにするため (過去に <c>LoggingBuilderExtensions</c> が
    /// MEL 側の同名ヘルパ型と衝突して source break を起こした経緯がある)。
    /// </para>
    /// <para>
    /// このライブラリは既存コードベースに存在しがちな自作 <c>LogLevel</c> 型
    /// (例: <c>Cube.LogLevel</c>) との名前衝突を避けるため、
    /// <c>SuperLightLogger.LogLevel</c> というシム enum を新設しない方針である。
    /// 代わりに文字列 ("Info", "Warn" など) を受け付けるエントリポイントを提供し、
    /// ユーザーが <c>using Microsoft.Extensions.Logging;</c> を追記せずとも
    /// レベル設定できるようにしている。
    /// </para>
    /// <para>
    /// 受理する名前 (大文字小文字を区別しない、前後空白は trim):
    /// <list type="bullet">
    ///   <item><description><c>Trace</c></description></item>
    ///   <item><description><c>Debug</c></description></item>
    ///   <item><description><c>Info</c> / <c>Information</c></description></item>
    ///   <item><description><c>Warn</c> / <c>Warning</c></description></item>
    ///   <item><description><c>Error</c></description></item>
    ///   <item><description><c>Fatal</c> / <c>Critical</c></description></item>
    ///   <item><description><c>None</c> / <c>Off</c></description></item>
    /// </list>
    /// </para>
    /// </remarks>
    public static class SLLogLevels
    {
        /// <summary>
        /// log4net / NLog 形式のレベル名を
        /// <see cref="Microsoft.Extensions.Logging.LogLevel"/> に変換する。
        /// </summary>
        /// <param name="level">レベル名 (Trace / Debug / Info / Warn / Error / Fatal / None)。</param>
        /// <exception cref="ArgumentNullException"><paramref name="level"/> が null。</exception>
        /// <exception cref="ArgumentException">未知のレベル名。</exception>
        public static LogLevel Parse(string level)
        {
            if (level == null) throw new ArgumentNullException(nameof(level));
            if (TryParse(level, out var result)) return result;
            throw new ArgumentException(
                "Unknown log level '" + level + "'. " +
                "Expected one of: Trace, Debug, Info, Warn, Error, Fatal, None.",
                nameof(level));
        }

        /// <summary>
        /// log4net / NLog 形式のレベル名のパースを試みる。
        /// </summary>
        /// <param name="level">レベル名。null / 空白 / 未知値はすべて false を返す。</param>
        /// <param name="result">成功時のパース結果。失敗時は <see cref="LogLevel.None"/>。</param>
        public static bool TryParse(string? level, out LogLevel result)
        {
            if (string.IsNullOrWhiteSpace(level))
            {
                result = LogLevel.None;
                return false;
            }

            // ToLowerInvariant でアロケートするが、設定は起動時の1回だけなのでコスト無視。
            switch (level!.Trim().ToLowerInvariant())
            {
                case "trace":
                    result = LogLevel.Trace; return true;
                case "debug":
                    result = LogLevel.Debug; return true;
                case "info":
                case "information":
                    result = LogLevel.Information; return true;
                case "warn":
                case "warning":
                    result = LogLevel.Warning; return true;
                case "error":
                    result = LogLevel.Error; return true;
                case "fatal":
                case "critical":
                    result = LogLevel.Critical; return true;
                case "none":
                case "off":
                    result = LogLevel.None; return true;
                default:
                    result = LogLevel.None;
                    return false;
            }
        }
    }
}
