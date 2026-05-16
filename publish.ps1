# SuperLightLogger NuGet パブリッシュスクリプト
# $apiKey を事前に設定するか、環境変数 NUGET_API_KEY を使用
# -expectedVersion を指定すると、その version の nupkg だけを公開し、重複はエラー扱い (CI 整合性検証用)
# 指定しない場合は従来動作 (artifacts/*.nupkg を全部 push、重複はスキップ)
param(
    [string]$expectedVersion = $null
)

Write-Host $PSScriptRoot

if (-not $apiKey)
{
    $apiKey = $env:NUGET_API_KEY
}

if (-not $apiKey)
{
    throw "NuGet API keyが設定されていません。`$apiKey または環境変数 NUGET_API_KEY を設定してください。"
}

$folder = "$PSScriptRoot\artifacts"

if ($expectedVersion)
{
    # CI 経路: 指定 version の nupkg だけを公開し、見つからなければ即エラー
    # --skip-duplicate も外す (バージョン更新忘れによる重複公開試行を確実にエラーへ昇格)
    $expectedNupkg = "SuperLightLogger.$expectedVersion.nupkg"
    $packages = Get-ChildItem -Path $folder -Filter $expectedNupkg -Recurse
    if (-not $packages)
    {
        throw "期待バージョンの nupkg が見つかりません: $expectedNupkg (Directory.Build.props の <Version> がブランチ名と一致しているか確認してください)"
    }
    $skipDuplicate = $false
}
else
{
    # ローカル経路: 従来動作 (artifacts/*.nupkg 全部 push、重複はスキップ)
    $packages = Get-ChildItem -Path $folder -Filter "*.nupkg" -Recurse | Sort-Object LastWriteTime
    $skipDuplicate = $true
}

if (-not $packages)
{
    Write-Error "パッケージが見つかりません: $folder"
    exit 1
}

$failed = 0
foreach ($pkg in $packages)
{
    Write-Host "Publishing: $($pkg.Name)"
    if ($skipDuplicate)
    {
        $result = dotnet nuget push "$($pkg.FullName)" --api-key $apiKey --source https://api.nuget.org/v3/index.json --skip-duplicate 2>&1
    }
    else
    {
        $result = dotnet nuget push "$($pkg.FullName)" --api-key $apiKey --source https://api.nuget.org/v3/index.json 2>&1
    }
    $exitCode = $LASTEXITCODE

    if ($exitCode -ne 0)
    {
        Write-Host "Error: $result"
        Write-Error "Failed to publish $($pkg.Name) (exit code: $exitCode)"
        $failed++
    }
    else
    {
        Write-Host "Successfully published $($pkg.Name)"
    }
}

if ($failed -gt 0)
{
    Write-Error "$failed 個のパッケージの公開に失敗しました"
    exit 1
}
else
{
    Write-Host "全パッケージの公開が完了しました！"
}
