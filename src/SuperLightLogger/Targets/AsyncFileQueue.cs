using System;
using System.Collections.Concurrent;
using System.Threading;

namespace SuperLightLogger
{
    /// <summary>
    /// 内部ライターをバックグラウンドスレッドでラップする非同期キュー。
    /// </summary>
    /// <remarks>
    /// netstandard2.0 でも追加依存なしで動かすため <see cref="BlockingCollection{T}"/> を使用する。
    /// </remarks>
    internal sealed class AsyncFileQueue : IFileTargetWriter
    {
        private readonly IFileTargetWriter _inner;
        private readonly BlockingCollection<LogEvent> _queue;
        private readonly Thread _worker;
        private readonly TimeSpan _flushInterval;
        private readonly bool _discardOnFull;
        private volatile bool _stopRequested;
        // Day-2 Ops 観測点 (FileLoggerProvider.GetStatistics 経由で公開)。
        // Interlocked で更新する原子カウンタ。
        private long _discardedCount;
        private long _workerErrorCount;

        /// <summary>キャパオーバーで drop されたログイベントの累計件数 (DiscardOnFull=true 時のみ加算)。</summary>
        internal long DiscardedCount => Interlocked.Read(ref _discardedCount);

        /// <summary>ワーカーで発生した例外の累計回数。</summary>
        internal long WorkerErrorCount => Interlocked.Read(ref _workerErrorCount);

        /// <summary>キューの現在深さ (BlockingCollection.Count、race の余地あり近似値)。</summary>
        internal int QueueDepth => _queue.Count;

        public AsyncFileQueue(IFileTargetWriter inner, int bufferSize, TimeSpan flushInterval, bool discardOnFull)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            if (bufferSize <= 0) bufferSize = 10000;
            _queue = new BlockingCollection<LogEvent>(bufferSize);
            _flushInterval = flushInterval > TimeSpan.Zero ? flushInterval : TimeSpan.FromSeconds(1);
            _discardOnFull = discardOnFull;
            _worker = new Thread(WorkerLoop)
            {
                IsBackground = true,
                Name = "SuperLightLogger.FileTarget.Async",
            };
            _worker.Start();
        }

        public void Write(in LogEvent ev)
        {
            if (_stopRequested) return;
            // BlockingCollection.TryAdd / Add は値型を copy で受け取る
            LogEvent copy = ev;
            try
            {
                if (_discardOnFull)
                {
                    if (!_queue.TryAdd(copy))
                    {
                        // キャパオーバーで drop された件数を Day-2 Ops 観測点としてカウント
                        Interlocked.Increment(ref _discardedCount);
                    }
                }
                else
                {
                    _queue.Add(copy);
                }
            }
            catch (InvalidOperationException)
            {
                // CompleteAdding 後の Add → 無視
            }
        }

        public void Flush()
        {
            _inner.Flush();
        }

        private void WorkerLoop()
        {
            DateTime lastFlush = DateTime.UtcNow;
            while (!_queue.IsCompleted)
            {
                try
                {
                    if (_queue.TryTake(out var ev, _flushInterval))
                    {
                        _inner.Write(in ev);
                    }

                    if (DateTime.UtcNow - lastFlush >= _flushInterval)
                    {
                        try { _inner.Flush(); } catch { /* ignored */ }
                        lastFlush = DateTime.UtcNow;
                    }
                }
                catch (InvalidOperationException)
                {
                    // CompleteAdding 後の TryTake → 終了
                    break;
                }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref _workerErrorCount);
                    Console.Error.WriteLine($"[SuperLightLogger.FileTarget.Async] ワーカーエラー: {ex.GetType().Name}: {ex.Message}");
                }
            }
        }

        public void Dispose()
        {
            if (_stopRequested) return;
            _stopRequested = true;
            try { _queue.CompleteAdding(); } catch { /* ignored */ }

            // 残りをドレイン (ワーカーが終わるまで待つ)
            try { _worker.Join(TimeSpan.FromSeconds(5)); } catch { /* ignored */ }

            // ワーカーが Join タイムアウトで生き残っている可能性に備え、
            // GetConsumingEnumerable ではなく TryTake で「あれば取る」方式にする。
            // GetConsumingEnumerable を使うと、ワーカーがまだ動いていた場合に
            // 同じ BlockingCollection から並行 take してログ順序が壊れる。
            while (_queue.TryTake(out var ev))
            {
                try { _inner.Write(in ev); } catch { /* ignored */ }
            }

            try { _inner.Flush(); } catch { /* ignored */ }
            try { _inner.Dispose(); } catch { /* ignored */ }
            try { _queue.Dispose(); } catch { /* ignored */ }
        }
    }
}
