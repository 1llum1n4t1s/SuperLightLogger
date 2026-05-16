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
        /// 「キューも含めて完全に書き出し完了させてからディスクに永続化したい」用途では
        /// <see cref="IDisposable.Dispose"/> を使うこと (Dispose は worker.Join → TryTake で
        /// 残量を drain → inner.Flush → inner.Dispose を順に行う)。
        /// </para>
        /// </remarks>
        void Flush();
    }
}
