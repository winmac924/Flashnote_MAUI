# Flashnote MAUI - 差分更新ファイル生成スクリプト

param(
    [Parameter(Mandatory=$true)]
    [string]$CurrentVersion,
    
    [Parameter(Mandatory=$true)]
    [string]$TargetVersion,
    
    [string]$Configuration = "Release"
)

Write-Host "🚀 Flashnote MAUI 差分更新ファイル生成を開始します..." -ForegroundColor Green

Write-Host "📋 差分更新設定:" -ForegroundColor Blue
Write-Host "   現在のバージョン: $CurrentVersion" -ForegroundColor Yellow
Write-Host "   ターゲットバージョン: $TargetVersion" -ForegroundColor Yellow
Write-Host "   構成: $Configuration" -ForegroundColor Yellow

# 必要なディレクトリを作成
$diffDir = "differential-updates"
$currentExeDir = "$diffDir\current"
$targetExeDir = "$diffDir\target"
$outputDir = "$diffDir\output"

if (!(Test-Path $diffDir)) { New-Item -ItemType Directory -Path $diffDir | Out-Null }
if (!(Test-Path $currentExeDir)) { New-Item -ItemType Directory -Path $currentExeDir | Out-Null }
if (!(Test-Path $targetExeDir)) { New-Item -ItemType Directory -Path $targetExeDir | Out-Null }
if (!(Test-Path $outputDir)) { New-Item -ItemType Directory -Path $outputDir | Out-Null }

# 1. 現在のバージョンのEXEファイルを取得
Write-Host "📥 現在のバージョンのEXEファイルを取得中..." -ForegroundColor Blue
$currentExePath = "bin\$Configuration\net9.0-windows10.0.19041.0\win-x64\publish\Flashnote_MAUI.exe"

if (!(Test-Path $currentExePath))
{
    Write-Host "❌ 現在のバージョンのEXEファイルが見つかりません: $currentExePath" -ForegroundColor Red
    Write-Host "   先に build-release.ps1 を実行してEXEファイルを生成してください。" -ForegroundColor Yellow
    exit 1
}

Copy-Item $currentExePath "$currentExeDir\Flashnote_MAUI.exe"

# 2. ターゲットバージョンのEXEファイルを生成
Write-Host "🏗️ ターゲットバージョンのEXEファイルを生成中..." -ForegroundColor Blue

# .csprojファイルのバージョンを更新
$csprojPath = "Flashnote_MAUI.csproj"
$content = Get-Content $csprojPath -Raw
$versionNumber = [int]($TargetVersion.Replace('.', ''))

$content = $content -replace '<ApplicationDisplayVersion>.*?</ApplicationDisplayVersion>', "<ApplicationDisplayVersion>$TargetVersion</ApplicationDisplayVersion>"
$content = $content -replace '<ApplicationVersion>.*?</ApplicationVersion>', "<ApplicationVersion>$versionNumber</ApplicationVersion>"
$content = $content -replace '<VersionPrefix>.*?</VersionPrefix>', "<VersionPrefix>$TargetVersion</VersionPrefix>"
$content = $content -replace '<AssemblyVersion>.*?</AssemblyVersion>', "<AssemblyVersion>$TargetVersion.0</AssemblyVersion>"
$content = $content -replace '<FileVersion>.*?</FileVersion>', "<FileVersion>$TargetVersion.0</FileVersion>"

Set-Content $csprojPath $content -Encoding UTF8

# ターゲットバージョンをビルド
dotnet publish -f net9.0-windows10.0.19041.0 -c $Configuration -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true

$targetExePath = "bin\$Configuration\net9.0-windows10.0.19041.0\win-x64\publish\Flashnote_MAUI.exe"
if (!(Test-Path $targetExePath))
{
    Write-Host "❌ ターゲットバージョンのEXEファイルの生成に失敗しました" -ForegroundColor Red
    exit 1
}

Copy-Item $targetExePath "$targetExeDir\Flashnote_MAUI.exe"

# 3. 差分ファイルを生成
Write-Host "🔍 差分ファイルを生成中..." -ForegroundColor Blue

$currentExeBytes = [System.IO.File]::ReadAllBytes("$currentExeDir\Flashnote_MAUI.exe")
$targetExeBytes = [System.IO.File]::ReadAllBytes("$targetExeDir\Flashnote_MAUI.exe")

$diffInfo = GenerateBinaryDiff $currentExeBytes $targetExeBytes $CurrentVersion $TargetVersion

# 4. 差分ファイルをZIPにパッケージ
Write-Host "📦 差分ファイルをパッケージ中..." -ForegroundColor Blue

$diffFileName = "Flashnote_MAUI_${CurrentVersion}_to_${TargetVersion}.diff"
$diffFilePath = "$outputDir\$diffFileName"

# 差分情報をJSONファイルとして保存
$diffInfoJson = $diffInfo | ConvertTo-Json -Depth 10
$tempDir = "$outputDir\temp"
if (Test-Path $tempDir) { Remove-Item $tempDir -Recurse -Force }
New-Item -ItemType Directory -Path $tempDir | Out-Null

Set-Content "$tempDir\diff_info.json" $diffInfoJson -Encoding UTF8

# 新しいデータファイルをコピー
foreach ($chunk in $diffInfo.Chunks)
{
    if ($chunk.Type -eq "insert" -or $chunk.Type -eq "replace")
    {
        $chunkData = $targetExeBytes[$chunk.Offset..($chunk.Offset + $chunk.Length - 1)]
        [System.IO.File]::WriteAllBytes("$tempDir\$($chunk.FileName)", $chunkData)
    }
}

# ZIPファイルを作成
Compress-Archive -Path "$tempDir\*" -DestinationPath $diffFilePath -Force

# 一時ファイルをクリーンアップ
Remove-Item $tempDir -Recurse -Force

# 5. 差分ファイルのサイズを確認
$diffFileSize = (Get-Item $diffFilePath).Length
$currentExeSize = (Get-Item "$currentExeDir\Flashnote_MAUI.exe").Length
$targetExeSize = (Get-Item "$targetExeDir\Flashnote_MAUI.exe").Length

$compressionRatio = [math]::Round((1 - $diffFileSize / $targetExeSize) * 100, 2)

Write-Host "✅ 差分更新ファイルの生成が完了しました！" -ForegroundColor Green
Write-Host "📁 出力ファイル: $diffFilePath" -ForegroundColor Yellow
Write-Host "📊 ファイルサイズ情報:" -ForegroundColor Blue
Write-Host "   現在のバージョン: $([math]::Round($currentExeSize / 1MB, 2)) MB" -ForegroundColor Yellow
Write-Host "   ターゲットバージョン: $([math]::Round($targetExeSize / 1MB, 2)) MB" -ForegroundColor Yellow
Write-Host "   差分ファイル: $([math]::Round($diffFileSize / 1MB, 2)) MB" -ForegroundColor Yellow
Write-Host "   圧縮率: $compressionRatio%" -ForegroundColor Cyan

Write-Host "🎉 差分更新ファイルの生成が完了しました！" -ForegroundColor Green

# 差分生成関数
function GenerateBinaryDiff($currentBytes, $targetBytes, $currentVersion, $targetVersion)
{
    Write-Host "   バイナリ差分を分析中..." -ForegroundColor Blue
    
    $diffInfo = @{
        CurrentVersion = $currentVersion
        TargetVersion = $targetVersion
        Chunks = @()
    }
    
    $currentLength = $currentBytes.Length
    $targetLength = $targetBytes.Length
    
    # 簡単な差分アルゴリズム（実際の実装ではより高度なアルゴリズムを使用）
    $i = 0
    $j = 0
    $chunkIndex = 0
    
    while ($i -lt $currentLength -and $j -lt $targetLength)
    {
        if ($currentBytes[$i] -eq $targetBytes[$j])
        {
            # 同じバイトの場合、コピー
            $copyStart = $i
            while ($i -lt $currentLength -and $j -lt $targetLength -and $currentBytes[$i] -eq $targetBytes[$j])
            {
                $i++
                $j++
            }
            
            $copyLength = $i - $copyStart
            if ($copyLength -gt 0)
            {
                $diffInfo.Chunks += @{
                    Type = "copy"
                    Offset = $copyStart
                    Length = $copyLength
                    FileName = $null
                }
            }
        }
        else
        {
            # 異なるバイトの場合、新しいデータを挿入
            $insertStart = $j
            while ($j -lt $targetLength -and ($i -ge $currentLength -or $currentBytes[$i] -ne $targetBytes[$j]))
            {
                $j++
                if ($i -lt $currentLength) { $i++ }
            }
            
            $insertLength = $j - $insertStart
            if ($insertLength -gt 0)
            {
                $diffInfo.Chunks += @{
                    Type = "insert"
                    Offset = $insertStart
                    Length = $insertLength
                    FileName = "chunk_$chunkIndex.bin"
                }
                $chunkIndex++
            }
        }
    }
    
    # 残りのデータを処理
    if ($j -lt $targetLength)
    {
        $remainingLength = $targetLength - $j
        $diffInfo.Chunks += @{
            Type = "insert"
            Offset = $j
            Length = $remainingLength
            FileName = "chunk_$chunkIndex.bin"
        }
    }
    
    Write-Host "   差分チャンク数: $($diffInfo.Chunks.Count)" -ForegroundColor Green
    return $diffInfo
} 