using System;

namespace SuperLightLogger
{
    /// <summary>
    /// 内部 File ターゲットのライターインターフェース (Sync / Async 共通)。
    /// </summary>
    internal interface IFileTargetWriter : IDisposable
    {
        void Write(in LogEvent ev);
        void Flush();
    }
}
