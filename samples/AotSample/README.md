# SuperLightLogger AOT サンプル

SuperLightLogger をネイティブAOTでビルドして実行するサンプル。

## ビルド方法

Visual Studio Developer Command Prompt または Developer PowerShell から実行する必要があります（C++ リンカが必要なため）。

```powershell
# Developer PowerShell for VS から実行
dotnet publish -c Release -r win-x64
```

成功すると以下にネイティブEXE（約2.6MB）が生成されます：

```
bin\Release\net10.0\win-x64\publish\AotSample.exe
```

## AOT利用時の注意点

`LogManager.GetCurrentClassLogger()` は `StackFrame` を内部で使用するため、
ネイティブAOTやトリミング環境では `IL2026` 警告が出ます。
代わりに以下のいずれかを使用してください：

```csharp
// 推奨: ジェネリック版（AOT安全）
var log = LogManager.GetLogger<MyClass>();

// または: typeof で型を指定
var log = LogManager.GetLogger(typeof(MyClass));

// または: 文字列で名前を指定
var log = LogManager.GetLogger("MyLogger");
```
