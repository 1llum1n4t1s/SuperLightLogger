using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SuperLightLogger;
using Xunit;

namespace SuperLightLogger.Tests.Targets;

/// <summary>
/// <see cref="AsyncFileQueue"/> および <see cref="FileTargetOptions.Async"/> の動作テスト。
/// </summary>
public class AsyncFileQueueTests : IDisposable
{
    private readonly string _tempDir;

    public AsyncFileQueueTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "SuperLightLoggerAsyncTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }
        catch
        {
            /* ignored */
        }
    }

    /// <summary>
    /// テスト用のインメモリライター。書き込まれた LogEvent をリストに積む。
    /// </summary>
    private sealed class CapturingWriter : IFileTargetWriter
    {
        public List<string> Messages { get; } = new();
        public int FlushCount;
        private readonly object _lock = new();

        public void Write(in LogEvent ev)
        {
            lock (_lock) Messages.Add(ev.Message);
        }

        public void Flush() => Interlocked.Increment(ref FlushCount);

        public void Dispose() { }
    }

    private static LogEvent MakeEvent(string msg)
        => new LogEvent(DateTime.Now, LogLevel.Information, "Cat", msg, null, 1, null);

    // ───────────── キュー → インナーに転送 ─────────────

    [Fact]
    public void Write_ForwardsToInner_AfterDispose()
    {
        var inner = new CapturingWriter();
        var queue = new AsyncFileQueue(inner, bufferSize: 100, flushInterval: TimeSpan.FromMilliseconds(50), discardOnFull: false);

        queue.Write(MakeEvent("a"));
        queue.Write(MakeEvent("b"));
        queue.Write(MakeEvent("c"));

        queue.Dispose(); // ドレインまで待つ

        Assert.Equal(new[] { "a", "b", "c" }, inner.Messages);
    }

    [Fact]
    public void Dispose_DrainsRemainingItems()
    {
        var inner = new CapturingWriter();
        var queue = new AsyncFileQueue(inner, bufferSize: 1000, flushInterval: TimeSpan.FromSeconds(10), discardOnFull: false);

        for (int i = 0; i < 200; i++)
            queue.Write(MakeEvent("m" + i));

        queue.Dispose();

        Assert.Equal(200, inner.Messages.Count);
        Assert.Equal("m0", inner.Messages[0]);
        Assert.Equal("m199", inner.Messages[199]);
    }

    [Fact]
    public void DiscardOnFull_DropsExcessWithoutBlocking()
    {
        var inner = new BlockingWriter();
        var queue = new AsyncFileQueue(inner, bufferSize: 4, flushInterval: TimeSpan.FromMilliseconds(50), discardOnFull: true);

        // インナーが詰まっている状態でキュー容量を超えて書いても、
        // ブロックせず Write は即返るはず (= 100ms 以内に完了)
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < 1000; i++)
            queue.Write(MakeEvent("x"));
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 1000, $"Discard モードでブロックされた: {sw.ElapsedMilliseconds}ms");

        inner.Release();
        queue.Dispose();
    }

    [Fact]
    public void WriteAfterDispose_DoesNotThrow()
    {
        var inner = new CapturingWriter();
        var queue = new AsyncFileQueue(inner, 100, TimeSpan.FromMilliseconds(50), discardOnFull: false);

        queue.Dispose();
        queue.Write(MakeEvent("after")); // 例外を投げない
    }

    [Fact]
    public void DoubleDispose_DoesNotThrow()
    {
        var inner = new CapturingWriter();
        var queue = new AsyncFileQueue(inner, 100, TimeSpan.FromMilliseconds(50), discardOnFull: false);
        queue.Write(MakeEvent("x"));

        queue.Dispose();
        queue.Dispose();
    }

    [Fact]
    public void Flush_DelegatesToInner()
    {
        var inner = new CapturingWriter();
        using var queue = new AsyncFileQueue(inner, 100, TimeSpan.FromMilliseconds(50), discardOnFull: false);

        queue.Flush();
        Assert.True(inner.FlushCount >= 1);
    }

    // ───────────── 統合: Async = true でファイル出力 ─────────────

    [Fact]
    public void AsyncMode_EndToEnd_AllLinesWritten()
    {
        var path = Path.Combine(_tempDir, "async.log");
        var options = new FileTargetOptions
        {
            FileName = path,
            Layout = "${message}",
            ArchiveAboveSize = 0,
            Async = true,
            AsyncBufferSize = 1000,
            AsyncFlushInterval = TimeSpan.FromMilliseconds(50),
        };

        using (var provider = new FileLoggerProvider(options))
        {
            var logger = provider.CreateLogger("Async");
            for (int i = 0; i < 100; i++)
                logger.LogInformation("line{N}", i);
            // Dispose 内で全件ドレインされる
        }

        var lines = System.IO.File.ReadAllLines(path);
        Assert.Equal(100, lines.Length);
        Assert.Equal("line0", lines[0]);
        Assert.Equal("line99", lines[99]);
    }

    /// <summary>
    /// Write を呼ばれると Release されるまで block する内部ライター。
    /// DiscardOnFull のテスト用。
    /// </summary>
    private sealed class BlockingWriter : IFileTargetWriter
    {
        private readonly ManualResetEventSlim _gate = new(initialState: false);

        public void Write(in LogEvent ev)
        {
            _gate.Wait(TimeSpan.FromSeconds(10));
        }

        public void Flush() { }

        public void Release() => _gate.Set();

        public void Dispose() => _gate.Dispose();
    }
}
