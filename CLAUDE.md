# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## プロジェクト概要

SuperLightLogger は **log4net / NLog の API をそのままに、内部だけ `Microsoft.Extensions.Logging` に差し替えるための薄いシム** です。`using log4net;` を `using SuperLightLogger;` に置き換えるだけで移行が完了することをコンセプトにしています。実装方針は徹底的に「軽量・AOT 安全・依存最小」。

NuGet 公開パッケージ (`PackageId=SuperLightLogger`, ライセンス: MIT)。

## ビルド / テストコマンド

`SuperLightLogger.csproj` は `netstandard2.0;net8.0;net10.0` の 3 ターゲット、テストプロジェクトは `net10.0` 単独 (xunit.v3) です。

```bash
# フルビルド (3 TFM すべて)
dotnet build -c Release

# 全テスト実行 (140 件)
dotnet test tests/SuperLightLogger.Tests

# 単一テストクラスだけ実行
dotnet test tests/SuperLightLogger.Tests --filter "FullyQualifiedName~LayoutRendererTests"

# 単一テスト
dotnet test tests/SuperLightLogger.Tests --filter "DisplayName~SizeArchive_SequenceIncrements"

# 嫌がらせ系 (adversarial) テストだけ実行 — 6 カテゴリ 29 件
dotnet test tests/SuperLightLogger.Tests --filter "FullyQualifiedName~FileTargetWriterAdversarialTests"

# AOT サンプル ( PublishAot=true ) のネイティブ発行
dotnet publish samples/AotSample -c Release

# NuGet パッケージ生成 (artifacts/ に出力)
dotnet pack src/SuperLightLogger -c Release
```

`Directory.Build.targets` がビルド前に `icon/generate_icon.ps1` を呼んでアプリアイコン PNG を生成します。マルチターゲットでも 1 回しか走らないよう `TargetFramework=netstandard2.0` 時のみ実行する条件付き。Windows + PowerShell が前提。

## アーキテクチャ

リポジトリは **2 層** に分かれています。

### 1. log4net 互換シム層 (`src/SuperLightLogger/*.cs`)

| ファイル | 役割 |
|---|---|
| `LogManager.cs` | 静的ファクトリ。`Configure(Action<ILoggingBuilder>)` か `Configure(ILoggerFactory)` で初期化。`GetLogger<T>()` は AOT 安全、`GetCurrentClassLogger()` は `StackFrame` 経由で `RequiresUnreferencedCode` 警告付き。未初期化時は `NullLoggerFactory` にフォールバックして初回だけ警告。 |
| `ILog.cs` / `Log.cs` | log4net `ILog` 互換インターフェイスと、`ILogger` をラップする実装。 |
| `LogExtensions.cs` | `InfoFormat` / `DebugFormat` などの log4net 風 API に加え、`InfoStructured` 等の M.E.L. 名前付きテンプレート版も提供 (構造化ログへの段階移行用)。 |
| `ServiceCollectionExtensions.cs` | `app.Services.UseSuperLightLogger()` で DI コンテナの `ILoggerFactory` を `LogManager` に橋渡しする 1 行ヘルパー。 |

### 2. 内蔵 File Target サブシステム (`src/SuperLightLogger/Targets/`)

NLog の `File Target` 相当を **追加 NuGet 依存ゼロ・AOT 安全** で実装。Microsoft.Extensions.Logging の `ILoggerProvider` として登録される唯一の内蔵シンク (Console / Serilog 等は M.E.L. の標準プロバイダに委譲)。

| ファイル | 公開性 | 役割 |
|---|---|---|
| `FileTargetOptions.cs` | public | 設定オブジェクト。`FileName` / `Layout` / `ArchiveAboveSize` / `ArchiveEvery` / `ArchiveNumbering` / `MaxArchiveFiles` / `Async*` などをすべて公開。`ArchiveEvery` `ArchiveNumbering` enum もここ。 |
| `FileLoggerProvider.cs` | public | `[ProviderAlias("SuperLightFile")]` 付き `ILoggerProvider`。`FileLoggerExtensions.AddSuperLightFile()` で `ILoggingBuilder` に登録。同ファイル内に internal な `FileLogger` (ILogger 実装) も同居。 |
| `LayoutRenderer.cs` | internal | NLog 互換 `${...}` テンプレートエンジン。**コンストラクタで 1 回パース** し `Action<LogEvent, StringBuilder>[]` に変換、描画時はこの配列を回すだけ。リフレクション・動的コード生成は一切なし。`${onexception:...}` の入れ子もサポート。 |
| `FileTargetWriter.cs` | internal | 同期ファイルライター。1 インスタンス 1 ロック (`_lock`)。アーカイブ解決 (`Sequence` / `Rolling` / `Date` / `DateAndSequence`) と保持数管理を自前で実装。`KeepFileOpen=true/false` 両モード対応。 |
| `AsyncFileQueue.cs` | internal | `IFileTargetWriter` をラップして `BlockingCollection<LogEvent>` + バックグラウンドスレッドで書き出す。`netstandard2.0` でも動くように `Channel<T>` ではなく `BlockingCollection` を採用。`Dispose` 時はワーカーを止めてから残量を **`TryTake` で**ドレインする (`GetConsumingEnumerable` を使うとワーカーと並行 take してログ順序が壊れるため)。 |
| `LogEvent.cs` | internal | `readonly struct LogEvent` (Timestamp, Level, Logger, Message, Exception, ThreadId, **ThreadName**)。`ThreadName` は Async モードでバックグラウンドスレッドの名前が紛れ込まないよう、生成時 (呼び出し元スレッド) でキャプチャしておく必要がある。`ForPath(DateTime)` でパステンプレート用の最小フィールド版を生成。 |
| `IFileTargetWriter.cs` | internal | Sync / Async 共通の `Write(in LogEvent)` / `Flush()` / `IDisposable`。 |

呼び出しフロー:

```
ILogger.Log → FileLogger.Log → IFileTargetWriter.Write
                                 ├─ (sync)  FileTargetWriter.Write → lock → WriteCore → 必要ならアーカイブ
                                 └─ (async) AsyncFileQueue.Write → BlockingCollection → ワーカースレッドが FileTargetWriter.Write
```

`FileLoggerProvider` は `Async=true` のときだけ `AsyncFileQueue` で `FileTargetWriter` をラップして保持。

## このリポジトリで作業する際の重要ルール

- **AOT/トリミングを壊さないこと**。`net8.0` / `net10.0` ターゲットは `IsAotCompatible=true` `IsTrimmable=true` `EnableTrimAnalyzer=true`。リフレクション (`Type.GetMethod`, `Activator.CreateInstance`, `Expression.Compile` 等) や動的コード生成を新規導入してはいけない。やむを得ず `StackFrame` 等を使う場合は `[RequiresUnreferencedCode]` を付ける (既存例: `LogManager.GetCurrentClassLogger`)。
- **netstandard2.0 互換性を維持すること**。新規 API を使う場合は `#if NET5_0_OR_GREATER` 等で分岐させる (既存例: `LogEvent` 生成時の `Environment.CurrentManagedThreadId` vs `Thread.CurrentThread.ManagedThreadId`)。`init` セッターやファイルスコープ namespace は使っていない (block 形式統一)。
- **`_disposed` フラグは必ずロック内側でチェックすること**。`FileTargetWriter` / `AsyncFileQueue` で `Dispose` と `Write` の TOCTOU を回避するため、外側で見ては駄目。過去にこの修正で TOCTOU バグを潰した経緯がある。
- **`AddSuperLightFile` は factory delegate で登録すること**。`builder.Services.AddSingleton<ILoggerProvider>(_ => new FileLoggerProvider(options))` の形にしないと DI コンテナが外部所有扱いで `Dispose` を呼ばずリークする。`FileLoggerProvider` を直接 `new` する場合は呼び出し側が `Dispose` する責任。
- **テストは内部型に直接アクセスできる**。`InternalsVisibleTo SuperLightLogger.Tests` を csproj で宣言しているため、`LogEvent` や `IFileTargetWriter` も `using SuperLightLogger;` だけでテストから触れる。
- **コメントと XML doc は日本語で書くこと**。既存コードはすべて日本語コメントで統一されている。
- **依存パッケージを増やさないこと**。現状の依存は Microsoft 純正 3 つ (`Microsoft.Extensions.Logging`, `Microsoft.Extensions.Logging.Abstractions`, `Microsoft.Extensions.DependencyInjection.Abstractions`) のみ。「Super Light」を名乗る根拠なので、サードパーティ追加は基本 NG。

## テストファイル構成

```
tests/SuperLightLogger.Tests/
├── LogManagerTests.cs                          # シム層 (LogManager)
├── LogTests.cs                                 # シム層 (ILog / Log)
├── LogExtensionsTests.cs                       # シム層 (InfoFormat / *Structured)
├── Helpers/FakeLogger.cs, FakeLoggerFactory.cs # テスト用スタブ
└── Targets/
    ├── LayoutRendererTests.cs                  # ${...} トークン描画・padding
    ├── FileLoggerProviderTests.cs              # DI 登録・ProviderAlias
    ├── AsyncFileQueueTests.cs                  # 並行・ドレイン・discard
    ├── FileTargetWriterTests.cs                # アーカイブ全モード・保持数・回帰
    └── FileTargetWriterAdversarialTests.cs     # /stst 生成の嫌がらせテスト (29 件)
```

`FileTargetWriterAdversarialTests.cs` は `境界値 / 並行性 / リソース枯渇 / 状態遷移 / 型パンチ / 環境異常` の 6 カテゴリで「壊れ方が安全か」を検証する。新規修正がこの層を壊していないかは必ず確認すること。

## 参考: 過去の落とし穴

- `BuildExceptionRenderer` の `case "tostring":` と `default:` を別ラベルにすると重複扱い。`default:` に統合済み。
- アーカイブ番号付けの `Date` モードでパステンプレートが `{#}` プレースホルダを含む場合、ハードコードで `0` を渡すと既存アーカイブを上書きする。`FindNextSequenceFromTemplate` で次番号を探索する実装に直してある。
- `FindSequencePlaceholder` は `#` のみ受理。`{0}` `{00}` 形式は `string.Format` プレースホルダと衝突するので意図的に非対応。
- 異種ファイル名 (例: 日付フォーマットの `20260414` のような数値) を巨大な連番として誤認しないため、`MaxReasonableSequence = 99999` のガード閾値あり。
- **`ArchiveCurrent` / `ResolveArchivePath` / `ResolveRollingArchive` は `in LogEvent` を受け取る**。旧シグネチャ (`DateTime now`) は `LogEvent.ForPath(now)` で Logger/Message を空にしてしまうため、`${logger}` トークンを含む `ArchiveFileName` が壊れていた。時間境界で Archive するときは `ev` から `Timestamp` だけ差し替えた **合成 LogEvent** を作って渡すこと。
- **動的 FileName (`app_${shortdate}.log` 等) + `MaxArchiveFiles`** は `WriteCore` のパス変更分岐で明示的に `CleanupOldArchives` を呼ぶ。`ListArchives` は `TemplateFileNameToGlob` + `IsDynamicFileNameCandidate` (ミドルに数字を最低 1 文字含むことを要求) でフィルタし、`app_audit.log` のような兄弟ファイルを誤って削除しないようにしている。
- **`KeepFileOpen=false` + パス変更**: footer は通常 `CloseStream(writeFooter:true)` が書くが、close 済みだと書けない。`ReopenForFooter()` で一瞬開き直してから close する必要がある。`Dispose` 経路でも同じ。
