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

### 1.0.4 (現行)
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
