param([Parameter(Mandatory)][string]$PackagePath, [Parameter(Mandatory)][string]$Version)
$ErrorActionPreference = 'Stop'
$archive = [System.IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $PackagePath).Path)
try {
    $entry = $archive.GetEntry('SuperLightLogger.nuspec')
    if (-not $entry) { throw 'nuspec がありません。' }
    $reader = [System.IO.StreamReader]::new($entry.Open())
    try { [xml]$spec = $reader.ReadToEnd() } finally { $reader.Dispose() }
    if ($spec.package.metadata.id -ne 'SuperLightLogger' -or $spec.package.metadata.version -ne $Version) { throw 'パッケージ識別子・版が一致しません。' }
    $required = @('README.md', 'icon.png')
    foreach ($tfm in @('netstandard2.0', 'net8.0', 'net10.0')) {
        $required += "lib/$tfm/SuperLightLogger.dll", "lib/$tfm/SuperLightLogger.xml"
    }
    foreach ($path in $required) {
        $item = $archive.GetEntry($path)
        if (-not $item -or $item.Length -eq 0) { throw "必須ファイルがありません: $path" }
        $stream = $item.Open()
        $sha = [System.Security.Cryptography.SHA256]::Create()
        try { $hash = [Convert]::ToHexString($sha.ComputeHash($stream)); Write-Output "$path SHA256=$hash" }
        finally { $sha.Dispose(); $stream.Dispose() }
    }
} finally { $archive.Dispose() }
Get-FileHash -LiteralPath $PackagePath -Algorithm SHA256
