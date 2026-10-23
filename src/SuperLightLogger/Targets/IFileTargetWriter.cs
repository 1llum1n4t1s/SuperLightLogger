using System;

namespace SuperLightLogger
{
    /// <summary>
    /// 内部 File ターゲットのライターインターフェース (Sync / Async 共通)。
    /// </summary>
    internal interface IFileTargetWriter : IDisposable
    {
        /// <summary>1 ログイベントを書き出す。同期実装はディスクへ即書込み、非同期実装はキューに enqueue する。</summary>
        void Write(in LogEvent ev);

        /// <summary>内部書込みまたはアーカイブ処理で発生した例外の累計件数。</summary>
        long ErrorCount { get; }

        /// <summary>
        /// 内側ストレージへの flush をベストエフォートで実行する。
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>セマンティクスの差異</b>:
        /// </para>
        /// <list type="bullet">
        ///   <item><description>
        ///   同期実装 (<see cref="FileTargetWriter"/>) は <see cref="System.IO.FileStream.Flush()"/>
        ///   を呼んで「現時点で <see cref="Write"/> 済みの全データをディスクに永続化」する保証付き。
        ///   </description></item>
        ///   <item><description>
        ///   非同期実装 (<see cref="AsyncFileQueue"/>) は **キュー内の未書出イベントは drain しない**。
        ///   呼出時点で worker が内部ストレージに書き出し済みの内容についてのみ <see cref="Write"/>
        ///   先 (典型的には内側 <see cref="FileTargetWriter"/>) の <see cref="Flush"/> をトリガする
        ///   best-effort 動作。
        ///   </description></item>
        /// </list>
        /// <para>
        /// 終了時のキューの書出しには <see cref="IDisposable.Dispose"/> を使うこと。
        /// 呼出側は worker の終了を最大5秒待機する。残量の drain、inner の Flush / Dispose、
        /// キュー等の解放は worker だけが行い、待機がタイムアウトした場合も worker が継続する。
        /// タイムアウト時は Dispose の復帰までに全イベントが書き出される保証はない。
        /// </para>
        /// </remarks>
        void Flush();
    }
}
