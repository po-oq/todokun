<#
.SYNOPSIS
  todokun の Windows 向け単一ファイル exe を作る（要件 3 章・計画 7-1）。

.DESCRIPTION
  self-contained・単一ファイル（ネイティブ DLL も exe に含め、実行時に展開）。出力は既定で <repo>/artifacts/win-x64。
  できた todokun.exe を任意のフォルダに置き、そのフォルダを PATH に通すと、どこからでも 「todokun」 で起動できる。
  データは exe と同じフォルダの data\、キャッシュは cache\ に作られる（README 参照）。mac 向けの配布物は作らない。

.EXAMPLE
  pwsh scripts/publish.ps1
  pwsh scripts/publish.ps1 -Output C:\tools\todokun
#>
param(
    [string]$Output = (Join-Path $PSScriptRoot '..\artifacts\win-x64'),
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot '..\src\Todokun\Todokun.csproj'

dotnet publish $project `
    -c $Configuration `
    -r win-x64 `
    --self-contained `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $Output

if ($LASTEXITCODE -ne 0) { throw "dotnet publish に失敗しました（終了コード $LASTEXITCODE）" }

$exe = Join-Path $Output 'todokun.exe'
Write-Host "できました: $exe"
Write-Host "このフォルダを PATH に通すと、どこからでも 「todokun」 で起動できます。"
Write-Host "データ: $(Join-Path $Output 'data')（exe と同じ場所）"
