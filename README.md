# SuperLightLogger

> **log4net や NLog を `using` を1行書き換えるだけで卒業できる、超軽量な互換シム。**

`using log4net;` を `using SuperLightLogger;` に置き換えるだけで、
コードベースは一切触らずに **C# 標準の `Microsoft.Extensions.Logging`** へ移行できます。

---

## こんな悩み、ありませんか？

- log4net や NLog で書かれた **長年のコードベース** を抱えていて、`Microsoft.Extensions.Logging` への乗り換えを何年も先送りにしている
- 「全コードを `ILogger<T>` に書き換えるなんて、工数もリスクも見合わない」と諦めている
- ASP.NET Core / Generic Host / OpenTelemetry / Serilog などの **モダンなエコシステム** を使いたいのに、ロギング基盤だけが足を引っ張っている
- `log4net.config` や `NLog.config` の XML を書くたびに、コードでスッキリ書きたいと思っている
- 新規プロジェクトでは `ILogger<T>` を使っているが、**旧プロジェクトとAPIが二重化** していて統一したい
- ネイティブ AOT 対応のアプリを作りたいが、既存のロギングライブラリの AOT 互換性が不安

---

## SuperLightLogger なら、移行が「数分」で終わります

| やること | 所要時間 |
|---|---|
| NuGet 参照を `log4net` → `SuperLightLogger` に差し替え | 10秒 |
| プロジェクト全体の `using log4net;` を一括置換 | 30秒 |
| `Program.cs` で `LogManager.Configure(...)` を1回だけ呼ぶ | 1分 |

**それだけで、ログ基盤の中身は丸ごと `Microsoft.Extensions.Logging` に置き換わります。**

既存の `log.Info(...)` も `log.DebugFormat(...)` も `log.Error(message, ex)` も、
**コードを1行も書き換えずにそのまま動きます。**

---

## 乗り換えると何が嬉しいの？

### 1. C# 標準ロギングのエコシステムに直結

`Microsoft.Extensions.Logging` 互換になるので、以下のシンクが**設定だけ**で使えるようになります：

- `AddConsole()` / `AddDebug()` / `AddEventLog()` / `AddEventSourceLogger()`
- **Serilog** (`AddSerilog()`)
- **NLog** (Microsoft.Extensions.Logging プロバイダ経由)
- **OpenTelemetry** (`AddOpenTelemetry()`)
- **Application Insights** (`AddApplicationInsights()`)
- Datadog / New Relic / Seq / Loki などサードパーティのプロバイダ全般

### 2. ASP.NET Core / Generic Host / DI とシームレスに統合

DI コンテナで構築済みの `ILoggerFactory` を、たった1行で `LogManager` に橋渡しできます：

```csharp
var app = builder.Build();
app.Services.UseSuperLightLogger();   // これだけ
```

### 3. 構造化ログへ「段階的に」移行できる

旧コードと新コードを **同じプロジェクトで共存** できるので、書き換えを一気にやる必要がありません：

```csharp
// 旧コード（log4net 互換）— そのまま動く
log.InfoFormat("ユーザー{0}がログインしました", userId);

// 新コード（M.E.Logging 式の構造化テンプレート）— 余裕ができた箇所から徐々に
log.InfoStructured("ユーザー {UserId} がログインしました", userId);
```

`{UserId}` の値は構造化ログとして扱われ、Seq や Datadog などで検索・集計できます。

### 4. ネイティブ AOT 対応

`net8.0` / `net10.0` ターゲットで `IsAotCompatible=true` を有効化済み。
**`PublishAot=true` でビルドしたアプリにそのまま組み込めます。**
2.6MB の単一ネイティブ EXE 内で動作することを実機検証済みです。

### 5. NLog 互換のファイルターゲットを内蔵

NLog の `File Target` 相当を **追加依存ゼロ** で同梱しています。
パステンプレート (`${shortdate}` 等)・サイズ/日付ベースのアーカイブ・最大保持数・
ヘッダー/フッター・非同期書込みをすべてサポートし、しかも **Native AOT 安全**。

```csharp
LogManager.Configure(builder =>
{
    builder.AddSuperLightFile(opt =>
    {
        opt.FileName = "logs/app_${shortdate}.log";
        opt.ArchiveAboveSize = 1L * 1024 * 1024;   // 1 MB でローテート
        opt.MaxArchiveFiles = 10;                  // 10世代で打ち切り
    });
});
```

詳細は後述の [内蔵 File Target](#内蔵-file-target) セクションを参照。

### 6. 依存もコードも、本当に「Super Light」

- 依存パッケージは Microsoft 純正の **3つだけ**
- 実装は数百行のシンプルなシム — 隠された魔法はゼロ
- 内蔵 File Target 以外のシンク (Console / Serilog / Datadog / OpenTelemetry 等) は標準 M.E.L にすべて委譲

---

## インストール

```bash
dotnet add package SuperLightLogger
```

## クイックスタート

### 1. 起動時に1回だけ初期化

```csharp
using Microsoft.Extensions.Logging;
using SuperLightLogger;

LogManager.Configure(builder =>
{
    builder.AddConsole();
    builder.SetMinimumLevel(LogLevel.Debug);
});
```

> 💡 **1.0.4+: `using Microsoft.Extensions.Logging;` を書きたくない場合**
>
> 既存コードに自作の `LogLevel` 型 (例: `Cube.LogLevel`) がある場合、MEL を using すると
> 名前衝突します。その場合は **文字列ベース API** を使えば `SuperLightLogger` 名前空間だけで
> 設定完結できます:
>
> ```csharp
> using SuperLightLogger;
>
> LogManager.Configure(builder =>
> {
>     builder.SetMinimumLevel("Debug");           // 文字列オーバーロード
>     builder.AddSuperLightFile("logs/app.log");  // ファイル名だけのショートカット
> });
> ```

### 2. あとは log4net と全く同じ書き方

```csharp
using SuperLightLogger;

public class OrderService
{
    private static readonly ILog log = LogManager.GetLogger(typeof(OrderService));

    public void PlaceOrder(int orderId)
    {
        log.Info("注文処理を開始します");
        log.DebugFormat("OrderId={0}", orderId);

        try
        {
            // ...
        }
        catch (Exception ex)
        {
            log.Error("注文処理に失敗しました", ex);
        }
    }
}
```

---

## log4net からの移行 — Before / After

```csharp
// ───── Before (log4net) ─────
using log4net;

public class MyService
{
    private static readonly ILog log = LogManager.GetLogger(typeof(MyService));
    // ...
}
```

```csharp
// ───── After (SuperLightLogger) ─────
using SuperLightLogger;

public class MyService
{
    private static readonly ILog log = LogManager.GetLogger(typeof(MyService));
    // ↑ 1文字も変わっていない！
}
```

**変更点は `using` の1行だけ。** クラス本体・ロガー取得・ログ呼び出し、すべて手つかずでOK。

XML 設定ファイル (`log4net.config`) は不要になります。
代わりに `Program.cs` で `LogManager.Configure(...)` を呼んで、コードでロガーを構成してください。

---

## NLog 風の書き方も対応

```csharp
private static readonly ILog log = LogManager.GetCurrentClassLogger();
```

> **AOT環境では非推奨**: `GetCurrentClassLogger()` は内部で `StackFrame` を使うため、
> ネイティブAOT / トリミング時に `IL2026` 警告が出ます。AOT で使う場合は
> `LogManager.GetLogger<MyClass>()` を使ってください（こちらも一行で書けます）。

---

## ASP.NET Core / Generic Host との統合

```csharp
var builder = WebApplication.CreateBuilder(args);

// 普段通り ILoggerFactory を構成
builder.Logging.AddConsole();
builder.Logging.AddDebug();

var app = builder.Build();

// DI コンテナの ILoggerFactory を SuperLightLogger に橋渡し
app.Services.UseSuperLightLogger();

app.Run();
```

これで `appsettings.json` の `"Logging"` セクションも有効になり、
SuperLightLogger 越しのログ出力が Microsoft 標準のフィルタ・プロバイダ機構に乗ります。

---

## 構造化ログへの段階的移行

log4net の `InfoFormat("{0}", x)` から M.E.Logging 式の名前付きテンプレートへ、
**ファイル単位・関数単位で少しずつ** 移行できます：

```csharp
// Step 1: そのまま動かす（log4net式）
log.InfoFormat("注文 {0} がユーザー {1} により完了", orderId, userId);

// Step 2: 構造化ログ化（名前付きプレースホルダー）
log.InfoStructured("注文 {OrderId} がユーザー {UserId} により完了", orderId, userId);
```

Step 2 にしておくと、Seq / Datadog / Application Insights などで
`OrderId = 12345` のような構造化クエリができるようになります。

---

## 内蔵 File Target

log4net や NLog から移行する際にほぼ必須となる「ローカルファイルへのログ出力」を、
**追加 NuGet パッケージなし**・**Native AOT 安全** で同梱しています。
NLog の `File Target` をそのまま意識して設計されており、NLog 経験者なら設定の見た目だけで動きが想像できます。

### 最小構成

```csharp
using SuperLightLogger;

LogManager.Configure(builder =>
{
    // ファイル名だけ指定するワンライナー (パステンプレート使用可)
    builder.AddSuperLightFile("logs/app_${shortdate}.log");
});
```

これだけで `logs/app_2026-04-15.log` が日付ごとに自動生成されます。
出力形式は NLog 互換のレイアウトテンプレートでカスタマイズできます。

細かい設定をしたい場合は `Action<FileTargetOptions>` オーバーロードを使います:

```csharp
LogManager.Configure(builder =>
{
    builder.AddSuperLightFile(opt =>
    {
        opt.FileName = "logs/app_${shortdate}.log";
        opt.Layout   = "${longdate} [${level}] ${logger} - ${message}";
    });
});
```

### NLog 相当のフル設定例

```csharp
LogManager.Configure(builder =>
{
    builder.AddSuperLightFile(opt =>
    {
        // パステンプレート
        opt.FileName = "logs/MyApp_${date:format=yyyyMMdd}.log";

        // 1行のレイアウト (デフォルト値とほぼ同等)
        opt.Layout =
            @"${date:format=yyyy-MM-dd HH\:mm\:ss.ffff} [${level:uppercase=true}] " +
            @"[${threadid}] ${message}${onexception:${newline}${exception:format=tostring}}";

        // アーカイブ (サイズ + 最大保持数)
        opt.ArchiveAboveSize = 1L * 1024 * 1024;            // 1 MB 超過でローテート
        opt.MaxArchiveFiles  = 10;                          // 10世代で打ち切り
        opt.ArchiveNumbering = ArchiveNumbering.Sequence;   // .1.log, .2.log, ...

        // 非同期書込み (バックグラウンドスレッドで吐き出す)
        opt.Async              = true;
        opt.AsyncBufferSize    = 10000;
        opt.AsyncFlushInterval = TimeSpan.FromSeconds(1);

        // 個別ターゲットの最低レベルも文字列で設定可能 (log4net 互換表記)
        opt.MinLevelName = "Trace";
    });

    // ↓ Microsoft.Extensions.Logging の `LogLevel` enum に依存せず
    //   文字列で最小レベルを設定できる (using Microsoft.Extensions.Logging; 不要)。
    //   Cube.LogLevel 等の自作 enum と名前衝突するのを避けたい人向け。
    builder.SetMinimumLevel("Trace");
});
```

> 💡 **`LogLevel` 名前衝突を避けたい人へ**
>
> 既存コードに `Cube.LogLevel` のような自作 enum がある場合、
> `using Microsoft.Extensions.Logging;` を足すと `LogLevel` が衝突してしまいます。
> SuperLightLogger は **MEL を using せずに済む文字列ベースの API** を提供しています:
>
> ```csharp
> using SuperLightLogger;   // これだけで OK
>
> LogManager.Configure(b =>
> {
>     b.SetMinimumLevel("Info");              // ← 文字列オーバーロード
>     b.AddSuperLightFile("logs/app.log");    // ← ファイル名だけ指定の最短形
> });
> ```
>
> 受理される値 (大文字小文字を区別しません):
> `Trace` / `Debug` / `Info` (= `Information`) /
> `Warn` (= `Warning`) / `Error` / `Fatal` (= `Critical`) / `None` (= `Off`)
>
> `FileTargetOptions.MinLevelName` プロパティも同じ文字列を受け付けます。

### 対応するレイアウトレンダラ

`${...}` 構文で NLog と同じ感覚で記述できます。すべてコンストラクタ時に
コンパイル済みデリゲート配列にパース済みのため、出力時はリフレクションを一切使いません。

| レンダラ | 説明 |
|---|---|
| `${longdate}` / `${shortdate}` / `${time}` | 日付/時刻のショートカット |
| `${date:format=yyyy-MM-dd HH\:mm\:ss.ffff}` | カスタムフォーマット |
| `${level:uppercase=true:padding=-5}` | レベル (大小文字・パディング指定可) |
| `${logger}` / `${message}` | カテゴリ名 / メッセージ本文 |
| `${exception:format=tostring}` (他に `message`, `type`, `stacktrace`) | 例外情報 (`format` はいずれか1つ) |
| `${onexception:${newline}${exception}}` | 例外ありのときだけ出力 (ネスト可) |
| `${threadid}` / `${threadname}` | スレッド情報 |
| `${processid}` / `${processname}` / `${machinename}` | プロセス/マシン情報 |
| `${basedir}` / `${tempdir}` / `${newline}` | 環境関連 |

### アーカイブ番号付け方式

| `ArchiveNumbering` | 例 | 用途 |
|---|---|---|
| `Sequence` (既定) | `app.1.log`, `app.2.log`, ... | NLog 既定。古い番号を残す |
| `Rolling` | `app.0.log` が常に最新 | tail コマンドと相性が良い |
| `Date` | `app.20260414.log` | 1日1ファイル + 日付ベースの保持 |
| `DateAndSequence` | `app.20260414.1.log` | 日付 + 同日内シーケンス |

### 時間ベースのアーカイブ

サイズではなく時間境界 (毎時/毎日/毎週) でローテートしたい場合：

```csharp
opt.ArchiveEvery = ArchiveEvery.Day;     // 日付が変わった瞬間にアーカイブ
opt.ArchiveAboveSize = 0;                // サイズベースは無効化
```

`ArchiveEvery` には `Year` / `Month` / `Day` / `Hour` / `Minute` / 各曜日が指定できます。

### アーカイブパスのカスタマイズ (`ArchiveFileName`)

アクティブなログファイルとは **別のディレクトリ** や **別の命名規則** でアーカイブしたい場合、
`ArchiveFileName` にレイアウトトークン込みのテンプレートを指定できます：

```csharp
opt.FileName        = "logs/app.log";
opt.ArchiveFileName = "logs/archive/app.{#}.${logger}.log";
opt.ArchiveNumbering = ArchiveNumbering.Sequence;
opt.MaxArchiveFiles  = 30;
```

- `{#}` はシーケンス番号 (`Sequence` / `Rolling` / `DateAndSequence` 時)
- `${logger}` / `${level}` / `${shortdate}` などのレイアウトトークンは **実際のログイベントから描画** されるため、
  「エラー発生時だけ ${level}=Error のファイルに退避」といった運用も可能
- 省略した場合は `FileName` から自動導出 (`app.log` → `app.1.log`)

### オーバーヘッド

- **同期モード (デフォルト)**: 1 ログイベントあたり数十マイクロ秒程度。高頻度書込みでもスループットは安定。
- **非同期モード (`Async=true`)**: 呼び出し側はキューに詰めるだけ (1〜数マイクロ秒)。追加依存なしで `netstandard2.0` でも動作。
- **AOT バイナリへの影響**: 内部はリフレクションも動的生成も使わないため、AOT 公開時の警告ゼロで取り込めます (`samples/AotSample` で実機検証済み)。

---

## ネイティブ AOT / トリミング対応

`net8.0` / `net10.0` ターゲットでは以下を有効化済みです：

- `IsAotCompatible=true`
- `IsTrimmable=true`
- `EnableTrimAnalyzer=true`

`dotnet publish -p:PublishAot=true` でビルドするアプリに、何の追加設定もなく組み込めます。

```csharp
// AOT 安全なロガー取得
var log = LogManager.GetLogger<MyClass>();

// または typeof 指定
var log = LogManager.GetLogger(typeof(MyClass));
```

実際のネイティブ AOT ビルドサンプルは [`samples/AotSample`](samples/AotSample) を参照してください。
2.6MB の単一ネイティブ EXE として全機能が動作することを検証済みです。

---

## 障害モード契約 / トラブルシューティング

SuperLightLogger は **「ロガーがアプリを殺さない」** を最優先にして、以下の障害をすべて
サイレントに飲み込み、stderr (`Console.Error`) にカテゴリ別 1 回だけ警告を出します。

| 障害 | 挙動 | 通知経路 |
|---|---|---|
| ディスクフル / 書込み権限なし | 該当ログイベントは drop | `Console.Error` (`_writeErrorEmitted` で 1 回限り) |
| アーカイブローテーション失敗 (ファイル削除 permission denied 等) | `MaxArchiveFiles` 設定が事実上効かなくなる | `Console.Error` (`_archiveErrorEmitted` で 1 回限り) |
| `AsyncFileQueue` キュー満杯 (`AsyncDiscardOnFull = true` 時) | 該当ログイベントは無言で drop | 通知なし |
| `AsyncFileQueue` バックグラウンドワーカーの例外 | ワーカー再起動なし、以降のログ drop | `Console.Error` (1 回) |
| `LogManager.Configure` 未呼び出し | `NullLoggerFactory` にフォールバックして全ログ drop | `Console.Error` (1 回限り、リセット契機なし) |
| `${longdate}` 等の DST 切替時間帯 | 同じ時間帯が二度現れる / 1 時間スキップ | 通知なし (`DateTime.Now` ベース) |

### 🚨 stderr が消失する環境では追加設定が必須

`Console.Error` はデフォルトで以下の環境で消失/captureされにくいため、リダイレクト設定が
必要です:

| 環境 | 対応 |
|---|---|
| **Windows Service** | `sc.exe` の stderr リダイレクト or `StandardErrorEncoding` 設定 |
| **IIS Hosted ASP.NET Core** | `web.config` の `stdoutLogEnabled="true"` |
| **Docker container** | `docker logs <container>` で確認、もしくは sidecar で stderr 収集 |
| **systemd サービス** | `journalctl -u <service>` で確認、`StandardError=journal` 推奨 |
| **Native AOT (Windows コンソールホストなし)** | 起動時に `AllocConsole()` 等で stderr 出力先を確保 |

警告は **カテゴリ別「初回 1 回」のみ** 出力されるため (スパム抑止のため)、本番運用では
別途 `Application Insights` / `OpenTelemetry` の `ILoggerProvider` を **並列登録** して
SuperLightLogger 自身の障害を外部から監視することを推奨します。

### ⏰ DST / UTC ログタイムスタンプの注意

`FileLogger.Log` および `LayoutRenderer` の `${longdate}` / `${shortdate}` / `${time}` / `${date}` は
**ローカル時刻 (`DateTime.Now`)** で生成されます。これは log4net / NLog のデフォルト挙動と
合わせるためですが、以下のケースで意図しない挙動になる場合があります:

- **DST 切替時間帯**: `ArchiveEvery.Hour` 等の時間境界アーカイブが、巻き戻り時間帯で
  「同じ 1 時間が二度現れる」状態になり、同じ時間帯のログが 2 つのファイルに分裂する可能性
- **マルチリージョン分散システム**: US-East と Tokyo のサーバーで `${longdate}` の値が
  9 時間ずれて記録され、Elastic/Kibana 等で時系列集約しづらくなる

UTC 運用が必要な場合は、現状はワークアラウンドとして `FileLogger` を fork して
`DateTime.UtcNow` を使う形にする必要があります。`${longdate:utc=true}` トークンパラメータの
ネイティブサポートは将来バージョンで検討予定。

### ⚠️ ロガー名にユーザー入力を渡さない (パストラバーサル防御)

`FileName` / `ArchiveFileName` テンプレートに `${logger}` を使う構成 (例:
`"logs/${logger}.log"`) では、`${logger}` 内のパス区切り (`/` `\` `:`) と
`Path.GetInvalidFileNameChars()` がライブラリ内部で `_` に置換されます。これにより
`LogManager.GetLogger("../../etc/passwd")` のような攻撃で任意パスに書き込まれることを防止
しています。

ただし、**ユーザー入力をそのままロガー名にする実装** (例: HTTP request の `request.Path` を
`GetLogger(...)` に渡す) は引き続き設計上の anti-pattern です。可能な限り
`LogManager.GetLogger<T>()` または静的な定数文字列でロガー名を指定してください。

---

## レベルマッピング

| SuperLightLogger | Microsoft.Extensions.Logging | log4net | NLog |
|---|---|---|---|
| `Trace()` | `Trace` | — | `Trace` |
| `Debug()` | `Debug` | `Debug` | `Debug` |
| `Info()` | `Information` | `Info` | `Info` |
| `Warn()` | `Warning` | `Warn` | `Warn` |
| `Error()` | `Error` | `Error` | `Error` |
| `Fatal()` | `Critical` | `Fatal` | `Fatal` |

log4net の 5レベル、NLog の 6レベル、どちらの感覚でもシームレスに使えます。

---

## 対応フレームワーク

| ターゲット | 対象ランタイム |
|---|---|
| `netstandard2.0` | .NET Framework 4.6.1+, .NET Core 2.0+, Mono, Unity |
| `net8.0` | .NET 8 LTS |
| `net10.0` | .NET 10 |

レガシーな .NET Framework 4.6.1 のプロジェクトから最新の .NET 10 / AOT アプリまで、
**同じ NuGet パッケージひとつ** でカバーできます。

---

## なぜ "Super Light" なのか

- 独自の設定ファイル形式は **持ちません**（コードで設定する）
- リフレクションや動的コード生成は **使いません**（AOT 完全対応）
- 巨大な依存ツリーは **持ちません**（Microsoft 純正 3パッケージのみ）
- 内蔵シンクは log4net/NLog からの移行で必須となる **File Target だけ** に絞り込み（残りは M.E.L プロバイダに委譲）

> シムは小さいほど、信頼できる。

---

## 変更履歴

### 1.0.8 (現行)
- **📦 依存パッケージ更新**
  - `Microsoft.Extensions.Logging` / `Microsoft.Extensions.Logging.Abstractions` / `Microsoft.Extensions.DependencyInjection.Abstractions` を **10.0.9** に更新
  - 開発依存も最新化: `Microsoft.NET.Test.Sdk` を 18.6.0、`AotSample` の `Microsoft.Extensions.Logging.Console` を 10.0.9 に統一
- **📝 ドキュメント微修正**
  - `FileTargetWriter.TemplateFileNameToGlob` の XML doc を「Win32 glob」からクロスプラットフォームな検索パターン (`Directory.GetFiles` のパターン) 表現に修正 (挙動変更なし)

### 1.0.7
- **🛡️ サプライチェーン強化**
  - GitHub Actions の SHA pin (`actions/checkout` / `actions/setup-dotnet`) と `permissions: contents: read` 明示
  - `publish.yml` に「ブランチ名 `release/<X.Y.Z>` と `Directory.Build.props` の `<Version>` が一致しているか検証する step」を追加 — バージョン更新忘れによる NuGet 番号永久消費事故を防止
  - `publish.ps1` に `-expectedVersion` パラメータを追加し、CI では指定バージョンの nupkg だけを `--skip-duplicate` なしで push
  - ビルド時の PowerShell スクリプト自動実行 (icon 生成) を廃止し、任意コード実行サーフェスを縮小
- **🏷️ 公開拡張クラスを `SLLog` プレフィックス化**
  - `ServiceCollectionExtensions` → `SLLogServiceCollectionExtensions`
  - `FileLoggerExtensions` → `SLLogFileTargetExtensions`
  - 他社 DI / ファイルロガーライブラリで多用される一般名との衝突を回避 (1.0.3 → 1.0.4 で `LoggingBuilderExtensions` をリネームした方針の継続)。**呼び出し側コードが `provider.UseSuperLightLogger()` / `builder.AddSuperLightFile(...)` の拡張メソッド呼出形式なら無影響。FQN 参照していた場合は新クラス名へ書き換えが必要**
  - `FileLogger` (internal) と `SLLogFileTargetExtensions` (public) を `FileLoggerProvider.cs` から別ファイルに分割
- **🔐 `LogManager` の所有モデル分離 (重大バグ修正)**
  - `Configure(ILoggerFactory factory, bool ownsFactory)` 新オーバーロード追加 — DI コンテナから受け取った factory を `Shutdown()` が誤って `Dispose` してしまう事故を防ぐ
  - `UseSuperLightLogger` は内部で `ownsFactory: false` を渡して DI 所有 factory を保護
  - `Configure` 二度呼び時、旧ライブラリ所有 factory を上書き前に `Dispose` してリーク解決
  - 既存 `Configure(ILoggerFactory)` シグネチャは `ownsFactory: true` (旧挙動互換) のまま維持
- **⚠️ パストラバーサル防御**
  - `LayoutRenderer` に `sanitizeForFilePath` モード追加 — `FileName` / `ArchiveFileName` で展開される `${logger}` / `${threadname}` / `${message}` のパス区切り (`/` `\` `:`) と `Path.GetInvalidFileNameChars()` を `_` に置換
  - `LogManager.GetLogger("../../etc/passwd")` のような攻撃文字列での任意パス書込みを防止
- **🔧 シム層の細かい修正**
  - `LogStructured` の ILog 第三者実装フォールバック経路で `string.Format` の `FormatException` を吸収 (アプリクラッシュ防止)
  - `LogManager.Reset` に `[EditorBrowsable(Never)]` を付与 (テスト専用 API の誤用窓口を縮小)
  - `FileTargetWriter` の `Console.Error.WriteLine` を `_lock` 外に出した — stderr リダイレクト先パイプ詰まりで他スレッドが全 block するのを回避
  - `LogExtensions.cs` の役割記述 (CLAUDE.md) を実装と整合させた
- **📊 Day-2 Ops 観測点 API (新規)**
  - `FileTargetStatistics` クラスと `FileLoggerProvider.GetStatistics()` メソッドを公開
  - Async モードの discard 件数 / キュー深さ / worker エラー数を本番運用で監視可能に
  - 依存追加ゼロ、Super Light 命題は維持
- **📖 ドキュメント拡充**
  - README に「障害モード契約 / トラブルシューティング」section 追加 (silent failure 一覧、stderr リダイレクト要求、DST/UTC 注意、`${logger}` サニタイズ挙動を明文化)
  - `IFileTargetWriter.Flush()` の Sync/Async セマンティクス差異と `FileTargetOptions` の mutability ポリシーを XML doc で明記
- **🧹 その他**
  - `.gitignore` にシークレット系パターン (`.env` / `secrets.json` / `*.pfx` / `*.snk` 等) を予防的に追加
  - AotSample の `Microsoft.Extensions.Logging.Console` を 10.0.8 に統一 (本体と version 揃え)
  - 本体依存 `Microsoft.Extensions.*` を 10.0.6 → **10.0.8** に patch 更新 (互換性破壊なし)
  - テスト SDK `Microsoft.NET.Test.Sdk` を 18.4.0 → 18.5.1 に patch 更新
- **✅ テスト**: 既存 182 件 + 新規回帰テスト 11 件 = **193 件すべて pass**

### 1.0.6
- **ホットパスのパフォーマンス最適化**
  - `*Format` 1/2/3 引数オーバーロードが毎回確保していた `object[]` 配列を除去 — `string.Format(string, object)` 等の非 params 版に直接委譲
  - `LayoutRenderer` の `${level}` 描画を事前計算ルックアップテーブル化 — 毎ログ行の `ToUpperInvariant()` / `PadLeft()` アロケーションを除去
  - `FileTargetWriter.TemplateFileNameToGlob` の連続 `*` 畳み込みを 1 パス化
- **依存パッケージを `Microsoft.Extensions.*` 10.0.6 に更新**

### 1.0.4
- **`LogLevel` 名前衝突を回避する文字列ベース API を追加**
  - `ILoggingBuilder.SetMinimumLevel("Info")` — `using Microsoft.Extensions.Logging;` を追記せず `SuperLightLogger` 名前空間だけで最小レベルを設定可能
  - `FileTargetOptions.MinLevelName` プロパティ — ファイルターゲット個別の最小レベルも文字列で設定可能
  - `SLLogLevels.Parse(string)` / `SLLogLevels.TryParse(...)` パブリックヘルパ
  - 新規公開型 (`SLLogBuilderExtensions` / `SLLogLevels`) は MEL 側の同名ヘルパ型との衝突を避けるため SuperLightLogger の略称 `SLLog` プレフィックス付き
  - 既存コードに自作 `LogLevel` 型 (例: `Cube.LogLevel`) がある場合の名前衝突フリー化
- **`AddSuperLightFile(string fileName)` ショートカット** — ファイル名だけ指定する最短形オーバーロード
- 受理するレベル名: `Trace` / `Debug` / `Info` (= `Information`) / `Warn` (= `Warning`) / `Error` / `Fatal` (= `Critical`) / `None` (= `Off`) — 大文字小文字区別なし

### 1.0.2
- **NLog 互換の内蔵 File Target サブシステムを追加** (`AddSuperLightFile`)
  - `${...}` レイアウトテンプレート (`longdate` / `level` / `message` / `exception` / `onexception` / `threadid` 等)
  - パステンプレート (`logs/app_${shortdate}.log` のような動的パス)
  - サイズ/日付/曜日ベースのアーカイブ (`ArchiveAboveSize` / `ArchiveEvery`)
  - アーカイブ番号付け方式 4 種 (`Sequence` / `Rolling` / `Date` / `DateAndSequence`)
  - アーカイブパスのカスタマイズ (`ArchiveFileName` — レイアウトトークン対応)
  - 最大保持数 (`MaxArchiveFiles`) と古いファイルの自動削除
  - ヘッダー / フッター / カスタムエンコーディング
  - 非同期書込み (`Async=true`)
- **ネイティブ AOT / トリミング対応**
  - `net8.0` / `net10.0` で `IsAotCompatible=true` `IsTrimmable=true` `EnableTrimAnalyzer=true`
  - `LogManager.GetLogger<T>()` が AOT 安全
  - `GetCurrentClassLogger()` は `[RequiresUnreferencedCode]` 付きで AOT 環境での誤用を警告化
  - `samples/AotSample` で 2.6MB の単一ネイティブ EXE 動作を実機検証
- 追加 NuGet 依存ゼロ、`netstandard2.0` でもそのまま動作

### 1.0.0
- log4net 互換 API シムの初版リリース
- `Microsoft.Extensions.Logging` を内部に持つ薄いラッパー
- `LogManager.Configure()` / `GetLogger<T>()` / `ILog` インターフェイス
- log4net 風 `*Format` API と M.E.L. 式 `*Structured` API の両対応
- `UseSuperLightLogger()` による DI コンテナ統合

---

## ライセンス

MIT License
