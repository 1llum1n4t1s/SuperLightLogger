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

### 5. 依存もコードも、本当に「Super Light」

- 依存パッケージは `Microsoft.Extensions.Logging.Abstractions` ほか **3つだけ**
- 実装は数百行のシンプルなシム — 隠された魔法はゼロ
- ライブラリ自体に独自設定ファイル/独自プロバイダは持たない（標準 M.E.L にすべて委譲）

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

- 独自の設定ファイル形式は **持ちません**（標準 M.E.L に全委譲）
- 独自の出力プロバイダは **持ちません**（コンソール・ファイル・クラウドはすべて M.E.L プロバイダを使う）
- リフレクションや動的コード生成は **使いません**（AOT 完全対応）
- 巨大な依存ツリーは **持ちません**（Microsoft 純正 3パッケージのみ）

> シムは小さいほど、信頼できる。

---

## ライセンス

MIT License
