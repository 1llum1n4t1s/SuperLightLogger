# SuperLightLogger 設計

## 目的と境界

SuperLightLogger は、log4net / NLog に近い呼出し API を保ちながら、ログ処理を `Microsoft.Extensions.Logging` (MEL) へ橋渡しする薄い互換シムです。内蔵機能はファイル出力に限定し、Console や外部ログ基盤は MEL の既存プロバイダへ委譲します。

対応 TFM は `netstandard2.0`、`net8.0`、`net10.0` です。`net8.0` と `net10.0` は AOT / trimming 互換をビルド時に検査します。本体の依存は `Microsoft.Extensions.Logging`、`Microsoft.Extensions.Logging.Abstractions`、`Microsoft.Extensions.DependencyInjection.Abstractions` の3パッケージだけです。

## 主要コンポーネント

| コンポーネント | 責務と境界 |
|---|---|
| `ILog` / `Log` / `LogExtensions` | log4net 互換の6レベル API、書式付きログ、MEL の名前付きテンプレートへ移行する構造化ログ API を提供する。実際の配送先は `ILogger` に委譲する。 |
| `LogManager` | `ILoggerFactory` の設定・所有権・ロガー生成を管理する。未設定時は `NullLoggerFactory` へフォールバックする。 |
| `SLLogServiceCollectionExtensions` / `SLLogBuilderExtensions` / `SLLogLevels` | DI 統合と、MEL の `LogLevel` 型を利用側へ露出させない文字列ベース設定を提供する。 |
| `SLLogFileTargetExtensions` / `FileLoggerProvider` / `FileLogger` | File Target を MEL の `ILoggerProvider` として登録し、カテゴリ別ロガーから共通 writer へ `LogEvent` を配送する。 |
| `LayoutRenderer` / `LogEvent` | レイアウトを構築時に一度だけ解析し、イベントを文字列へ描画する。スレッド情報は呼出元で捕捉する。パス用トークンはファイル名として無害化する。 |
| `FileTargetWriter` | 同期書込み、ストリーム管理、サイズ・時間境界のアーカイブ、連番解決、保持数管理を1インスタンス1ロックで直列化する。 |
| `AsyncFileQueue` | `BlockingCollection<LogEvent>` と専用スレッドで writer を非同期化する。キュー満杯時の待機／破棄と終了時のドレインを担当する。 |
| `FileTargetStatistics` | discard 件数、キュー深さ、内部書込み・アーカイブエラー数をベストエフォートのスナップショットとして公開する。 |

## データフロー

```text
ILog 呼出し
  → Log が MEL の ILogger へ変換
  → FileLogger.Log が LogEvent を生成
  → IFileTargetWriter.Write
      ├─ Sync: FileTargetWriter → LayoutRenderer → ファイル／アーカイブ
      └─ Async: AsyncFileQueue → worker → FileTargetWriter → ファイル／アーカイブ
```

`LogManager.Configure(Action<ILoggingBuilder>)` は factory を生成してライブラリ所有とします。DI の `UseSuperLightLogger()` はコンテナ所有の factory を `ownsFactory: false` で接続します。`Shutdown()` が破棄するのはライブラリ所有の factory だけです。

## 重要な不変条件

- AOT / trimming を維持するため、レイアウト処理と通常のロガー取得にリフレクションや動的コード生成を使わない。`StackFrame` を使う `GetCurrentClassLogger()` だけは制約を属性と文書で明示する。
- `netstandard2.0` で利用できない API は TFM 条件分岐で隔離する。非同期キューは互換性のある `BlockingCollection<T>` を使う。
- `FileTargetWriter` と `AsyncFileQueue` の disposed 状態は必ず対応するロック内で判定し、`Write` / `Dispose` の TOCTOU を作らない。
- Async 終了時の残量ドレイン、inner writer、キュー、停止トークンの破棄は worker だけが所有する。Join タイムアウト時も呼出側から触れない。
- ログ障害はアプリへ伝播させず、該当イベントを破棄して stderr へ抑制付きで通知する。見逃しを補うため、累計エラーを `GetStatistics()` から観測できるようにする。
- `${logger}` などパスに展開する値は区切り文字、無効文字、`.` / `..`、Windows 予約デバイス名を無害化する。
- アーカイブ保持数の掃除は現在のテンプレートから生成され得る候補だけを対象とし、兄弟ファイルを巻き込まない。動的 `FileName` の自然なパス切替で生じる旧ファイルと、各具象パスのサイズ／時間アーカイブは別の保持枠として扱う。
- `AddSuperLightFile` は DI が破棄する factory delegate 登録を使う。`FileLoggerProvider` を直接生成した利用者は自ら破棄する。
- `LogManager` の static 状態を扱うテストは同じ xUnit collection に所属させ、クラス間並列実行を防ぐ。

## 採用した設計判断

| 判断 | 理由とトレードオフ |
|---|---|
| MEL を配送基盤として再利用 | 既存エコシステムへ接続でき、シムの責務と依存を小さく保てる。一方、MEL が表現しない細かな log4net / NLog 挙動は完全互換にしない。 |
| レイアウトを構築時に静的 delegate 列へ変換 | ログごとの解析と動的コード生成を避け、AOT 安全性とホットパス性能を両立する。 |
| 例外を飲み込み統計で観測 | ロガー障害によるアプリ停止を防げる一方、利用者が stderr または統計を監視しなければログ欠落を見逃し得る。 |
| 文字列ベースのレベル API | 利用側の `LogLevel` 型名衝突を避けられる。誤記はコンパイル時ではなく構成時に検出される。 |
| File Target を追加依存なしで内蔵 | 移行時に必要なローカル出力を単一パッケージで提供できる。一方、高度な外部シンクは MEL プロバイダへ委譲する。 |

## 検証とリリース

通常の検証コマンドと変更時の制約は [`AGENTS.md`](AGENTS.md) を正本とします。`release/<version>` の push で GitHub Actions が restore、Release build、test、pack、ブランチ名とのバージョン整合確認を行い、NuGet Trusted Publishing で公開します。
