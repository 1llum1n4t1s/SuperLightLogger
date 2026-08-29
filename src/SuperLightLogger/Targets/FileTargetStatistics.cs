namespace SuperLightLogger
{
    /// <summary>
    /// <see cref="FileLoggerProvider"/> の実行時統計情報。
    /// Day-2 Ops 観点で Async モードの health-check / モニタリング用に
    /// <see cref="FileLoggerProvider.GetStatistics"/> から取得する。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 値はすべてベストエフォートのスナップショットで、取得時点での近似値。
    /// 厳密な強整合性は保証しない (`BlockingCollection.Count` 等は内部的に race の余地がある)。
    /// </para>
    /// <para>
    /// 同期モード (`Async=false`) では Async 関連の値は常に 0 / -1 になる。
    /// <see cref="IsAsyncMode"/> で構成を確認できる。
    /// </para>
    /// </remarks>
    public sealed class FileTargetStatistics
    {
        /// <summary>Async 構成 (<see cref="FileTargetOptions.Async"/>=true) で動作しているかどうか。</summary>
        public bool IsAsyncMode { get; }

        /// <summary>
        /// Async モードでキューが満杯のため drop されたログイベントの累計件数
        /// (<see cref="FileTargetOptions.AsyncDiscardOnFull"/>=true 時のみカウント)。
        /// </summary>
        public long DiscardedLogEventCount { get; }

        /// <summary>
        /// 同期ライターまたは Async モードのバックグラウンドワーカーで
        /// 内部書込み・アーカイブ中に例外が発生した累計回数。
        /// 通常は 0 のはず。0 でない場合はディスクフル / permission denied / I/O hardware 障害等の
        /// 兆候として調査推奨。
        /// </summary>
        public long WorkerErrorCount { get; }

        /// <summary>
        /// Async モードのバックグラウンドワーカーが処理待ちのキュー深さ (取得時点の近似)。
        /// 同期モードでは -1。
        /// </summary>
        public int QueueDepth { get; }

        internal FileTargetStatistics(bool isAsyncMode, long discardedLogEventCount, long workerErrorCount, int queueDepth)
        {
            IsAsyncMode = isAsyncMode;
            DiscardedLogEventCount = discardedLogEventCount;
            WorkerErrorCount = workerErrorCount;
            QueueDepth = queueDepth;
        }
    }
}
