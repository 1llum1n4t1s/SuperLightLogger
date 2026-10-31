# SuperLightLogger

SuperLightLogger は、log4net 風の `ILog` API と NLog 風のロガー取得・ファイル設定を、
`Microsoft.Extensions.Logging` (MEL) へ橋渡しする軽量シムです。

- `Info` / `DebugFormat` / `Error(message, exception)` などの既存呼出しを維持しながら移行できます。
- 名前付きテンプレートによる構造化ログ、DI 統合、内蔵 File Target を提供します。
- File Target はレイアウト、サイズ／時間アーカイブ、保持数、非同期書込みに対応します。
- Console や外部ログ基盤への出力には、対応する MEL プロバイダを追加して構成します。

log4net / NLog の完全互換ではありません。対応する呼出しは `using` の変更で移行できますが、
パッケージ参照の差替えと起動時の構成が必要です。既存の XML 設定は読み込みません。
NLog 固有の `Logger` 型や未提供の API を使う箇所は書換えが必要です。

## インストール

```bash
dotnet add package SuperLightLogger
```

## クイックスタート

### 1. 起動時に1回だけ初期化

Console 出力を使う場合は `dotnet add package Microsoft.Extensions.Logging.Console` も実行してください。
ASP.NET Core など、ホストがこのプロバイダを提供する環境では追加不要です。

```csharp
using Microsoft.Extensions.Logging;
using SuperLightLogger;

LogManager.Configure(builder =>
{
    builder.AddConsole();
    builder.SetMinimumLevel(LogLevel.Debug);
});
```

> 💡 **`LogLevel` の名前衝突を避ける場合**
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

### 2. log4net 風の API で出力

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

単独アプリで `LogManager.Configure(builder => ...)` を使う場合は、終了時に
`LogManager.Shutdown()` を呼び、ファイルと非同期キューを終了処理してください。
DI 統合ではホストによるコンテナの破棄に委ねます。

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

この例ではクラス本体・ロガー取得・ログ呼出しを維持できます。

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

`FileName` に `${shortdate}` / `${date}` などの動的トークンを使う場合、自然なパス切替で生じる旧ファイルと、同じ具象パスから作られるサイズ／時間アーカイブには `MaxArchiveFiles` が別々の保持枠として適用されます。高流量日のサイズローテートが過去日のログを保持枠から押し出すことはありません。

`SetMinimumLevel(string)` と `FileTargetOptions.MinLevelName` は、大文字小文字を区別せず
`Trace` / `Debug` / `Info` (= `Information`) / `Warn` (= `Warning`) /
`Error` / `Fatal` (= `Critical`) / `None` (= `Off`) を受け付けます。

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
opt.ArchiveEvery = ArchiveEvery.Day;     // 日付境界を越えた後の最初の書込みでアーカイブ
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

- `{#}` はシーケンス番号。`Date` モードでも指定すると既存ファイルを避けて次番号を使います。`{0}` / `{00}` は非対応です。
- `${logger}` / `${level}` / `${shortdate}` などのレイアウトトークンは **実際のログイベントから描画** されるため、
  「エラー発生時だけ ${level}=Error のファイルに退避」といった運用も可能
- 省略した場合は `FileName` から自動導出 (`app.log` → `app.1.log`)

### オーバーヘッド

- **同期モード (デフォルト)**: 呼び出し元スレッドでファイル書込みと必要なフラッシュを完了します。
- **非同期モード (`Async=true`)**: 呼び出し側は bounded queue へ追加し、専用スレッドが書き込みます。満杯時に待機するか破棄するかは `AsyncDiscardOnFull` で選べます。
- **AOT**: File Target の通常経路はリフレクションと動的コード生成を使わず、追加依存なしで `netstandard2.0` でも動作します。

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

---

## 障害モード契約 / トラブルシューティング

SuperLightLogger は **「ロガーがアプリを殺さない」** ことを優先し、主な障害をアプリへ
伝播させず、次のように処理します。

| 障害 | 挙動 | 通知経路 |
|---|---|---|
| ディスクフル / 書込み権限なし | 該当ログイベントを破棄し、累計エラー数を加算 | `Console.Error` (writer ごとに初回 1 回) |
| アーカイブローテーション失敗 (ファイル削除 permission denied 等) | その回のアーカイブまたは保持掃除を中止し、書込み処理は継続 | `Console.Error` (writer ごとに初回 1 回) |
| `AsyncFileQueue` キュー満杯 (`AsyncDiscardOnFull = true` 時) | 該当ログイベントは無言で drop | 通知なし |
| `AsyncFileQueue` バックグラウンドワーカーの例外 | 累計エラー数を加算し、同じワーカーで処理を継続 | `Console.Error` (発生ごと) |
| `LogManager.Configure` 未呼び出し | `NullLoggerFactory` にフォールバックして全ログを破棄 | `Console.Error` (未構成状態ごとに初回 1 回) |
| `${longdate}` 等の DST 切替時間帯 | 同じ時間帯が二度現れる / 1 時間スキップ | 通知なし (`DateTime.Now` ベース) |

### 🚨 stderr を収集しない環境では監視を追加

Windows Service や GUI アプリなど標準エラー出力を収集しないホストでは、writer の警告を
確認できません。ホストまたはプロセス管理基盤で stderr を収集するか、直接生成して保持した
`FileLoggerProvider` の `GetStatistics()` を定期的に取得してください。

| 統計 | 意味 |
|---|---|
| `WorkerErrorCount` | 同期 writer、アーカイブ処理、Async worker で発生した累計エラー数 |
| `DiscardedLogEventCount` | `AsyncDiscardOnFull=true` でキュー満杯により破棄した累計イベント数 |
| `QueueDepth` | Async queue の取得時点での近似深さ (同期モードは `-1`) |

統計はベストエフォートのスナップショットです。writer の反復警告は抑制されるため、
本番では stderr と統計の両方を監視してください。

### ⏰ DST / UTC ログタイムスタンプの注意

`FileLogger.Log` および `LayoutRenderer` の `${longdate}` / `${shortdate}` / `${time}` / `${date}` は
**ローカル時刻 (`DateTime.Now`)** で生成されます。これは log4net / NLog のデフォルト挙動と
合わせるためですが、以下のケースで意図しない挙動になる場合があります:

- **DST 切替時間帯**: `ArchiveEvery.Hour` 等の時間境界アーカイブが、巻き戻り時間帯で
  「同じ 1 時間が二度現れる」状態になり、同じ時間帯のログが 2 つのファイルに分裂する可能性
- **マルチリージョン分散システム**: US-East と Tokyo のサーバーで `${longdate}` の値が
  異なるローカル時刻で記録され、時系列集約しづらくなる

UTC 変換用の設定や `${longdate:utc=true}` は提供していません。UTC 運用が必要な場合は、
UTC 出力を提供する別の MEL プロバイダを構成してください。

### ⚠️ ロガー名にユーザー入力を渡さない (パストラバーサル防御)

`FileName` / `ArchiveFileName` テンプレートに `${logger}` を使う構成 (例:
`"logs/${logger}.log"`) では、`${logger}` 内のパス区切り (`/` `\` `:`) と
`Path.GetInvalidFileNameChars()` がライブラリ内部で `_` に置換されます。これにより
`LogManager.GetLogger("../../etc/passwd")` のような攻撃で任意パスに書き込まれることを防止
しています。

加えて、`.` / `..` は `_` に置換し、Windows の予約デバイス名
(`CON` / `PRN` / `AUX` / `NUL` / `COM1`〜`COM9` / `LPT1`〜`LPT9`) には先頭へ `_` を付けます。

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
- 通常のロガー取得と File Target はリフレクションや動的コード生成を使いません（`GetCurrentClassLogger()` だけは `StackFrame` を使うため AOT / trimming 時に警告）
- 巨大な依存ツリーは **持ちません**（Microsoft 純正 3パッケージのみ）
- 内蔵シンクは log4net/NLog からの移行で必須となる **File Target だけ** に絞り込み（残りは M.E.L プロバイダに委譲）

> シムは小さいほど、信頼できる。

---

## 開発者向け資料

内部構造と設計判断は [DESIGN.md](DESIGN.md)、ビルド・テストと変更時の制約は
[AGENTS.md](AGENTS.md) を参照してください。

## 変更履歴

版ごとの変更点は [CHANGELOG.md](https://github.com/1llum1n4t1s/SuperLightLogger/blob/main/CHANGELOG.md) を参照してください。

---

## ライセンス

MIT License
