# SuperLightLogger

log4net互換APIで呼び出せる `Microsoft.Extensions.Logging` ラッパーライブラリ。

`using log4net;` を `using SuperLightLogger;` に書き換えるだけで、C#標準のログ基盤に移行できます。

## インストール

```
dotnet add package SuperLightLogger
```

## 対応フレームワーク

- .NET Standard 2.0 (.NET Framework 4.6.1+)
- .NET 8.0
- .NET 10.0

ネイティブAOT / トリミング対応（`net8.0` / `net10.0` のみ。`netstandard2.0` は対象外）。

## クイックスタート

### 1. 初期設定（Program.cs等で1回だけ）

```csharp
using Microsoft.Extensions.Logging;
using SuperLightLogger;

// コンソール出力の場合
LogManager.Configure(builder =>
{
    builder.AddConsole();
    builder.SetMinimumLevel(LogLevel.Debug);
});
```

### 2. ログ出力（log4netと同じ書き方）

```csharp
using SuperLightLogger;

public class MyService
{
    private static readonly ILog log = LogManager.GetLogger(typeof(MyService));

    public void DoWork()
    {
        log.Info("処理を開始します");
        log.DebugFormat("パラメータ: {0}", someValue);

        try
        {
            // ...
        }
        catch (Exception ex)
        {
            log.Error("処理に失敗しました", ex);
        }
    }
}
```

## log4netからの移行

変更が必要なのは基本的に2箇所だけ：

1. **NuGet参照**: `log4net` → `SuperLightLogger`
2. **using文**: `using log4net;` → `using SuperLightLogger;`
3. **初期設定**: XML設定ファイルの代わりに `LogManager.Configure()` を呼ぶ

既存のコードはそのまま動きます：

```csharp
// Before (log4net)
using log4net;
private static readonly ILog log = LogManager.GetLogger(typeof(MyClass));

// After (SuperLightLogger) - 同じAPI！
using SuperLightLogger;
private static readonly ILog log = LogManager.GetLogger(typeof(MyClass));
```

## NLog風の書き方もOK

```csharp
private static readonly ILog log = LogManager.GetCurrentClassLogger();
```

> **AOT環境では非推奨**: `GetCurrentClassLogger()` は内部で `StackFrame` を使うため、
> ネイティブAOT/トリミング時に `IL2026` 警告が出ます。AOTで使う場合は
> `LogManager.GetLogger<MyClass>()` を使ってください。

## ASP.NET Core / Generic Host との統合

```csharp
var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

// DIコンテナのILoggerFactoryを自動設定
app.Services.UseSuperLightLogger();
```

## 構造化ログへの段階的移行

log4netの `InfoFormat("{0}", x)` から M.E.Logging式のテンプレートへ段階的に移行できます：

```csharp
// 旧: log4net式（そのまま動く）
log.InfoFormat("ユーザー{0}がログインしました", userId);

// 新: 構造化ログ（名前付きプレースホルダー）
log.InfoStructured("ユーザー {UserId} がログインしました", userId);
```

## ネイティブAOT / トリミング対応

`net8.0` / `net10.0` ターゲットでは `IsAotCompatible=true` を有効化済みです。
ライブラリ全体がトリミング解析を通過しているため、`PublishAot=true` でビルドした
アプリケーションから安全に利用できます。

ただし `LogManager.GetCurrentClassLogger()` は内部で `StackFrame` を使用するため、
AOT/トリミング環境では型情報が削られて正しいロガー名を取得できない可能性があります。
代わりに以下の **AOT安全な API** を使用してください：

```csharp
// 推奨: ジェネリック版（AOT安全）
private static readonly ILog log = LogManager.GetLogger<MyClass>();

// または: typeof で型を指定
private static readonly ILog log = LogManager.GetLogger(typeof(MyClass));
```

実際のネイティブAOTビルドサンプルは [`samples/AotSample`](samples/AotSample) を参照してください。

## レベルマッピング

| SuperLightLogger | M.E.Logging | log4net | NLog |
|-----------------|-------------|---------|------|
| `Trace()` | Trace | - | Trace |
| `Debug()` | Debug | Debug | Debug |
| `Info()` | Information | Info | Info |
| `Warn()` | Warning | Warn | Warn |
| `Error()` | Error | Error | Error |
| `Fatal()` | Critical | Fatal | Fatal |

## ライセンス

MIT License
