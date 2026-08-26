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
        private readonly TimeSpan _shutdownTimeout;
        private readonly bool _discardOnFull;
        private readonly object _lifecycleLock = new object();
        private readonly CancellationTokenSource _stopTokenSource = new CancellationTokenSource();
        private bool _stopRequested;
        private int _activeOperations;
        // Day-2 Ops 観測点 (FileLoggerProvider.GetStatistics 経由で公開)。
        // Interlocked で更新する原子カウンタ。
        private long _discardedCount;
        private long _workerErrorCount;

        /// <summary>キャパオーバーで drop されたログイベントの累計件数 (DiscardOnFull=true 時のみ加算)。</summary>
        internal long DiscardedCount => Interlocked.Read(ref _discardedCount);

        /// <summary>ワーカーで発生した例外の累計回数。</summary>
        internal long WorkerErrorCount => Interlocked.Read(ref _workerErrorCount);

        /// <summary>キューの現在深さ (BlockingCollection.Count、race の余地あり近似値)。</summary>
        internal int QueueDepth
        {
            get
            {
                try { return _queue.Count; }
                catch (ObjectDisposedException) { return 0; }
            }
        }

        /// <summary>Dispose と競合中の Write / Flush を含む実行中操作数。</summary>
        internal int ActiveOperationCount
        {
            get
            {
                lock (_lifecycleLock) return _activeOperations;
            }
        }

        public AsyncFileQueue(
            IFileTargetWriter inner,
            int bufferSize,
            TimeSpan flushInterval,
            bool discardOnFull,
            TimeSpan? shutdownTimeout = null)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            if (bufferSize <= 0) bufferSize = 10000;
            _queue = new BlockingCollection<LogEvent>(bufferSize);
            _flushInterval = flushInterval > TimeSpan.Zero ? flushInterval : TimeSpan.FromSeconds(1);
            _shutdownTimeout = shutdownTimeout.HasValue && shutdownTimeout.Value > TimeSpan.Zero
                ? shutdownTimeout.Value
                : TimeSpan.FromSeconds(5);
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
            if (!TryEnterOperation()) return;
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
                    _queue.Add(copy, _stopTokenSource.Token);
                }
            }
            catch (InvalidOperationException)
            {
                // CompleteAdding / Dispose 後の Add → 無視
                // ObjectDisposedException も InvalidOperationException の派生型なのでここで吸収される。
            }
            catch (OperationCanceledException)
            {
                // Dispose が待機中の Add を停止 → 無視
            }
            finally
            {
                ExitOperation();
            }
        }

        public void Flush()
        {
            if (!TryEnterOperation()) return;
            try { _inner.Flush(); }
            finally { ExitOperation(); }
        }

        private void WorkerLoop()
        {
            try
            {
                DateTime lastFlush = DateTime.UtcNow;
                while (true)
                {
                    try
                    {
                        if (_queue.TryTake(out var ev, _flushInterval))
                        {
                            _inner.Write(in ev);
                        }
                        else if (_queue.IsCompleted)
                        {
                            break;
                        }

                        if (DateTime.UtcNow - lastFlush >= _flushInterval)
                        {
                            try { _inner.Flush(); } catch { /* ignored */ }
                            lastFlush = DateTime.UtcNow;
                        }
                    }
                    catch (InvalidOperationException)
                    {
                        // CompleteAdding / Dispose 後の TryTake → 終了
                        break;
                    }
                    catch (Exception ex)
                    {
                        Interlocked.Increment(ref _workerErrorCount);
                        try
                        {
                            Console.Error.WriteLine($"[SuperLightLogger.FileTarget.Async] ワーカーエラー: {ex.GetType().Name}: {ex.Message}");
                        }
                        catch { /* ignored: stderr 自体が壊れていたら何もできない */ }
                    }
                }
            }
            finally
            {
                // 残量の drain と資源破棄は worker だけが所有する。
                // Dispose の Join がタイムアウトしても inner / queue と競合させない。
                try { _inner.Flush(); } catch { /* ignored */ }
                try { _inner.Dispose(); } catch { /* ignored */ }
                try { _queue.Dispose(); } catch { /* ignored */ }
                try { _stopTokenSource.Dispose(); } catch { /* ignored */ }
            }
        }

        public void Dispose()
        {
            lock (_lifecycleLock)
            {
                if (_stopRequested) return;
                _stopRequested = true;

                // 満杯キューで待機中の Write を先に解除してから CompleteAdding する。
                try { _stopTokenSource.Cancel(); } catch { /* ignored */ }
                try { _queue.CompleteAdding(); } catch { /* ignored */ }

                while (_activeOperations > 0)
                {
                    Monitor.Wait(_lifecycleLock);
                }
            }

            // worker が時間内に終われば、finally で drain / Flush / Dispose まで完了済み。
            // タイムアウト時は worker に所有権を残して戻り、呼出しスレッドから inner / queue へ触れない。
            try { _worker.Join(_shutdownTimeout); } catch { /* ignored */ }
        }

        private bool TryEnterOperation()
        {
            lock (_lifecycleLock)
            {
                if (_stopRequested) return false;
                _activeOperations++;
                return true;
            }
        }

        private void ExitOperation()
        {
            lock (_lifecycleLock)
            {
                _activeOperations--;
                if (_stopRequested && _activeOperations == 0)
                {
                    Monitor.PulseAll(_lifecycleLock);
                }
            }
        }
    }
}
