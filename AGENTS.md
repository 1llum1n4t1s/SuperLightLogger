# AGENTS.md

This file provides guidance to Claude Code and other coding agents working in this repository.

## プロジェクト概要

SuperLightLogger は **log4net / NLog の API をそのままに、内部だけ `Microsoft.Extensions.Logging` に差し替えるための薄いシム** です。`using log4net;` を `using SuperLightLogger;` に置き換えるだけで移行が完了することをコンセプトにしています。実装方針は徹底的に「軽量・AOT 安全・依存最小」。

NuGet 公開パッケージ (`PackageId=SuperLightLogger`, ライセンス: MIT)。

## ビルド / テストコマンド

`SuperLightLogger.csproj` は `netstandard2.0;net8.0;net10.0` の 3 ターゲット、テストプロジェクトは `net10.0` 単独 (xunit.v3) です。

```bash
# フルビルド (3 TFM すべて)
dotnet build -c Release

# 全テスト実行 (210 件)
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

アイコンを更新する場合だけ `pwsh -File icon/generate_icon.ps1` を手動実行し、生成物を commit します。通常のビルドからスクリプトは実行しません。全体設計の正本は [`DESIGN.md`](DESIGN.md) を参照してください。

## 設計の正本

コンポーネントの責務、境界、データフロー、設計判断は [`DESIGN.md`](DESIGN.md) を正本とします。実装を変更する前に該当箇所を確認し、設計上の契約が変わる場合は同じ変更内で更新してください。このファイルには作業規約、必須コマンド、検証手順、実装時に維持する制約だけを記載します。

## このリポジトリで作業する際の重要ルール

SuperLightLogger の直接参照元、更新対象ファイル、復元条件、検証コマンドは、リポジトリ直下の `vava.config.json` を正本とする。直接参照元を追加・削除したときは、同じ変更内で `consumerUpdates.targets` を同期する。

- **AOT/トリミングを壊さないこと**。`net8.0` / `net10.0` ターゲットは `IsAotCompatible=true` `IsTrimmable=true` `EnableTrimAnalyzer=true`。リフレクション (`Type.GetMethod`, `Activator.CreateInstance`, `Expression.Compile` 等) や動的コード生成の新規導入は禁止。代わりに静的な実装で組み、やむを得ず `StackFrame` 等を使う場合は `[RequiresUnreferencedCode]` を付ける (既存例: `LogManager.GetCurrentClassLogger`)。
- **netstandard2.0 互換性を維持すること**。新規 API を使う場合は `#if NET5_0_OR_GREATER` 等で分岐させる (既存例: `LogEvent` 生成時の `Environment.CurrentManagedThreadId` vs `Thread.CurrentThread.ManagedThreadId`)。`init` セッターやファイルスコープ namespace は使わず block 形式で統一する。
- **`_disposed` フラグは必ずロック内側でチェックすること**。`FileTargetWriter` / `AsyncFileQueue` で `Dispose` と `Write` の TOCTOU を回避するため、ロック外で見てはいけない。過去にこの修正で TOCTOU バグを潰した経緯がある。
- **`AddSuperLightFile` は factory delegate で登録すること**。`builder.Services.AddSingleton<ILoggerProvider>(_ => new FileLoggerProvider(options))` の形にする。インスタンスを直接渡すと DI コンテナが外部所有扱いで `Dispose` を呼ばずリークするため。`FileLoggerProvider` を直接 `new` する場合は呼び出し側が `Dispose` する責任を負う。
- **新規公開ヘルパ型は `SLLog` プレフィックスを付けること**。追加前に MEL / BCL の公開型名と照合し、FQN 参照や `using static` の曖昧化を防ぐ。利用側の `LogLevel` 衝突は文字列ベース API (`SetMinimumLevel("Info")` / `MinLevelName = "Warn"`) で回避し、`SuperLightLogger.LogLevel` のシム enum は追加しない。
- **テストは内部型に直接アクセスできる**。`InternalsVisibleTo SuperLightLogger.Tests` を csproj で宣言しているため、`LogEvent` や `IFileTargetWriter` も `using SuperLightLogger;` だけでテストから触れる。
- **コメントと XML doc は日本語で書くこと**。既存コードはすべて日本語コメントで統一されている。
- **依存パッケージを増やさないこと**。現状の依存は Microsoft 純正 3 つ (`Microsoft.Extensions.Logging`, `Microsoft.Extensions.Logging.Abstractions`, `Microsoft.Extensions.DependencyInjection.Abstractions`) のみに保つ。「Super Light」を名乗る根拠なので、サードパーティ追加は基本 NG。追加が必要と判断したら実装前に必要性と影響を報告する。

## テストファイル構成

```
tests/SuperLightLogger.Tests/
├── LogManagerTests.cs                          # シム層 (LogManager) — [Collection] 必須
├── LogTests.cs                                 # シム層 (ILog / Log)
├── LogExtensionsTests.cs                       # シム層 (InfoFormat / *Structured)
├── SLLogLevelsTests.cs                         # 文字列→LogLevel パーサ (Parse / TryParse)
├── SLLogBuilderExtensionsTests.cs              # SetMinimumLevel(string) 拡張 — [Collection] 必須
├── Helpers/
│   ├── FakeLogger.cs / FakeLoggerFactory.cs    # テスト用スタブ
│   └── LogManagerCollection.cs                 # LogManager 静的状態共有テストの xunit Collection 定義
└── Targets/
    ├── LayoutRendererTests.cs                  # ${...} トークン描画・padding
    ├── FileLoggerProviderTests.cs              # DI 登録・ProviderAlias・MinLevelName・AddSuperLightFile(string)
    ├── AsyncFileQueueTests.cs                  # 並行・ドレイン・discard
    ├── FileTargetWriterTests.cs                # アーカイブ全モード・保持数・回帰
    └── FileTargetWriterAdversarialTests.cs     # /stst 生成の嫌がらせテスト (29 件)
```

`FileTargetWriterAdversarialTests.cs` は `境界値 / 並行性 / リソース枯渇 / 状態遷移 / 型パンチ / 環境異常` の 6 カテゴリで「壊れ方が安全か」を検証する。新規修正がこの層を壊していないかは必ず確認すること。

**`LogManager` 静的状態を触るテストは必ず `[Collection(LogManagerCollection.Name)]` を付けること。** xunit.v3 は既定でクラスごと並列実行する。`LogManager._factory` は static field なので、別クラスのテストが `Configure` / `Shutdown` を並行で叩くとレースして偶発的に失敗する (過去に `Shutdown_DisposesFactory` が flaky 化した実績あり)。現在対象は `LogManagerTests` と `SLLogBuilderExtensionsTests` の 2 クラス。

## 実装時に維持する契約

- `BuildExceptionRenderer` の `tostring` は `default` と同じ経路で処理し、重複する switch ラベルを作らない。
- アーカイブ番号付けの `Date` モードでパステンプレートが `{#}` を含む場合は、`FindNextSequenceFromTemplate` で次番号を探索し、既存アーカイブを上書きしない。
- `FindSequencePlaceholder` は `#` のみ受理。`{0}` `{00}` 形式は `string.Format` プレースホルダと衝突するので意図的に非対応。
- 異種ファイル名 (例: 日付フォーマットの `20260414` のような数値) を巨大な連番として誤認しないよう、`MaxReasonableSequence = 99999` のガード閾値を維持する。
- **`ArchiveCurrent` / `ResolveArchivePath` / `ResolveRollingArchive` は `in LogEvent` を受け取る**。時間境界で Archive するときは `${logger}` 等の展開値を保持するため、元の `ev` から `Timestamp` だけ差し替えた合成 `LogEvent` を渡す。
- **動的 FileName (`app_${shortdate}.log` 等) + `MaxArchiveFiles`**: `WriteCore` のパス変更時は自然に生じた旧 FileName だけを掃除し、`IsDynamicArchiveCandidate` で同じ具象パスのサイズ／時間アーカイブを除外する。`ArchiveCurrent` 側は現在の具象 basename から生じたアーカイブだけを掃除し、2 つを別の保持枠として管理する。`Rolling` でも動的パス切替の旧ファイル掃除を実行し、具象パスのアーカイブだけを自前連番管理へ任せる。`TemplateFileNameToGlob` + `IsDynamicFileNameCandidate` で `app_audit.log` のような兄弟ファイルを候補から除外する。
- **`KeepFileOpen=false` + パス変更**: close 済みのファイルへ footer を書けるよう、`ReopenForFooter()` で一時的に開き直してから `CloseStream(writeFooter: true)` を実行する。`Dispose` 経路でも同じ契約を維持する。
