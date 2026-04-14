using Microsoft.Extensions.Logging;
using SuperLightLogger;

// SuperLightLogger ネイティブAOT サンプル
// dotnet publish -c Release -r win-x64 で単一ネイティブEXEとして出力可能。

// このサンプルでは、コンソール出力 + 内蔵 File Target の両方を構成する。
// File Target は NLog 互換のレイアウト・日付ロール・サイズアーカイブ・最大保持数を備え、
// すべてリフレクション/動的コード生成を使わないため AOT 安全。
var logFile = Path.Combine(AppContext.BaseDirectory, "logs", "AotSample_${shortdate}.log");

LogManager.Configure(builder =>
{
    builder.AddConsole();
    builder.AddSuperLightFile(opt =>
    {
        opt.FileName = logFile;
        opt.ArchiveAboveSize = 1L * 1024 * 1024;     // 1 MB 超過でアーカイブ
        opt.MaxArchiveFiles = 10;                    // 古いものから削除
        opt.ArchiveNumbering = ArchiveNumbering.Sequence;
    });
    builder.SetMinimumLevel(LogLevel.Trace);
});

// AOT 安全な3つの取得方法
var logByType = LogManager.GetLogger(typeof(Program));
var logByGeneric = LogManager.GetLogger<Program>();   // ジェネリック版（推奨）
var logByName = LogManager.GetLogger("AotSample");

// 全レベルの動作確認
logByType.Trace("Trace 出力");
logByType.Debug("Debug 出力");
logByType.Info("Info 出力");
logByType.Warn("Warn 出力");
logByType.Error("Error 出力");
logByType.Fatal("Fatal 出力");

// Format系
logByGeneric.InfoFormat("値={0}, 値={1}", 42, "hello");

// 例外
try
{
    throw new InvalidOperationException("テスト例外");
}
catch (Exception ex)
{
    logByName.Error("例外発生", ex);
}

// 構造化ログ
logByType.InfoStructured("ユーザー {UserId} がログインしました", 12345);

LogManager.Shutdown();

Console.WriteLine("AOT sample completed!");
Console.WriteLine($"ログファイル: {Path.Combine(AppContext.BaseDirectory, "logs")}");

// 注意: GetCurrentClassLogger() は StackFrame ベースのため
// AOT/Trim 環境では IL2026 警告が出る。AOT で使う場合は GetLogger<T>() を推奨。
