# AnkiPlus MAUI - Windows リリースビルドスクリプト (GitHub対応)

param(
    [string]$Version = "1.1.0",
    [string]$Configuration = "Release",
    [switch]$CreateTag = $false,
    [switch]$PushToGitHub = $false,
    [switch]$IncrementVersion = $false,
    [ValidateSet("Major", "Minor", "Patch")]
    [string]$VersionType = "Patch"
)

# バージョン自動インクリメント機能
function Get-NextVersion {
    param(
        [string]$CurrentVersion,
        [string]$VersionType
    )
    
    $versionParts = $CurrentVersion.Split('.')
    $major = [int]$versionParts[0]
    $minor = [int]$versionParts[1]
    $patch = [int]$versionParts[2]
    
    switch ($VersionType) {
        "Major" { 
            $major++
            $minor = 0
            $patch = 0
        }
        "Minor" { 
            $minor++
            $patch = 0
        }
        "Patch" { 
            $patch++
        }
    }
    
    return "$major.$minor.$patch"
}

# 現在のバージョンを.csprojから取得
function Get-CurrentVersionFromCsproj {
    param([string]$CsprojPath)
    
    $content = Get-Content $CsprojPath -Raw
    if ($content -match '<ApplicationDisplayVersion>(.*?)</ApplicationDisplayVersion>') {
        return $matches[1]
    }
    return "1.0.0"
}

# .csprojファイルのバージョンを更新
function Update-CsprojVersion {
    param(
        [string]$CsprojPath,
        [string]$Version
    )
    
    Write-Host "📝 .csprojファイルのバージョンを更新しています..." -ForegroundColor Blue
    
    $content = Get-Content $CsprojPath -Raw
    $versionNumber = [int]($Version.Replace('.', ''))
    $assemblyVersion = "$Version.0"
    
    # 各バージョン項目を更新
    $content = $content -replace '<ApplicationDisplayVersion>.*?</ApplicationDisplayVersion>', "<ApplicationDisplayVersion>$Version</ApplicationDisplayVersion>"
    $content = $content -replace '<ApplicationVersion>.*?</ApplicationVersion>', "<ApplicationVersion>$versionNumber</ApplicationVersion>"
    $content = $content -replace '<VersionPrefix>.*?</VersionPrefix>', "<VersionPrefix>$Version</VersionPrefix>"
    $content = $content -replace '<AssemblyVersion>.*?</AssemblyVersion>', "<AssemblyVersion>$assemblyVersion</AssemblyVersion>"
    $content = $content -replace '<FileVersion>.*?</FileVersion>', "<FileVersion>$assemblyVersion</FileVersion>"
    
    Set-Content $CsprojPath $content -Encoding UTF8
    
    Write-Host "✅ バージョンを $Version に更新しました" -ForegroundColor Green
    Write-Host "   - ApplicationDisplayVersion: $Version" -ForegroundColor Yellow
    Write-Host "   - ApplicationVersion: $versionNumber" -ForegroundColor Yellow
    Write-Host "   - VersionPrefix: $Version" -ForegroundColor Yellow
    Write-Host "   - AssemblyVersion: $assemblyVersion" -ForegroundColor Yellow
    Write-Host "   - FileVersion: $assemblyVersion" -ForegroundColor Yellow
}

Write-Host "🚀 AnkiPlus MAUI リリースビルドを開始します..." -ForegroundColor Green

$csprojPath = "AnkiPlus_MAUI.csproj"

# バージョン自動インクリメント
if ($IncrementVersion) {
    $currentVersion = Get-CurrentVersionFromCsproj -CsprojPath $csprojPath
    $Version = Get-NextVersion -CurrentVersion $currentVersion -VersionType $VersionType
    Write-Host "🔢 バージョンを自動インクリメント: $currentVersion → $Version ($VersionType)" -ForegroundColor Cyan
}

Write-Host "📋 ビルド設定:" -ForegroundColor Blue
Write-Host "   バージョン: $Version" -ForegroundColor Yellow
Write-Host "   構成: $Configuration" -ForegroundColor Yellow
Write-Host "   タグ作成: $CreateTag" -ForegroundColor Yellow
Write-Host "   GitHubプッシュ: $PushToGitHub" -ForegroundColor Yellow

# ビルド前のクリーンアップ
Write-Host "🧹 プロジェクトをクリーンアップしています..." -ForegroundColor Blue
dotnet clean -c $Configuration

# バージョン情報の更新
Update-CsprojVersion -CsprojPath $csprojPath -Version $Version

# Windows用ビルド（自己完結型EXE）
Write-Host "🏗️ Windows用実行ファイルをビルドしています..." -ForegroundColor Blue
dotnet publish -f net9.0-windows10.0.19041.0 -c $Configuration -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true

# ビルド結果の確認
$outputPath = "bin\$Configuration\net9.0-windows10.0.19041.0\win-x64\publish\"
if (Test-Path $outputPath) {
    Write-Host "✅ ビルドが正常に完了しました！" -ForegroundColor Green
    Write-Host "📁 出力フォルダ: $outputPath" -ForegroundColor Yellow
    
    # EXEファイルの検索
    $exeFiles = Get-ChildItem -Path $outputPath -Filter "*.exe" -Recurse
    if ($exeFiles.Count -gt 0) {
        Write-Host "📦 生成された実行ファイル:" -ForegroundColor Green
        foreach ($file in $exeFiles) {
            $fileSize = [math]::Round($file.Length / 1MB, 2)
            Write-Host "   $($file.FullName) ($fileSize MB)" -ForegroundColor Yellow
        }
    } else {
        Write-Host "⚠️ 実行ファイルが見つかりませんでした。" -ForegroundColor Yellow
    }
} else {
    Write-Host "❌ エラー: ビルドが失敗しました。" -ForegroundColor Red
    exit 1
}

Write-Host "🎉 リリースビルドが完了しました！" -ForegroundColor Green

# オプション: 署名の確認
Write-Host "`n🔐 署名の確認:" -ForegroundColor Blue
Write-Host "実行ファイルには有効な証明書で署名することを推奨します！" -ForegroundColor Yellow
Write-Host "署名されていない場合、初回実行時にWindows Defenderの警告が表示される可能性があります。" -ForegroundColor Yellow

# GitHub関連の処理
if ($CreateTag -or $PushToGitHub) {
    Write-Host "`n🐙 GitHub処理:" -ForegroundColor Blue
    
    # Gitの状態確認
    $gitStatus = git status --porcelain
    if ($gitStatus) {
        Write-Host "⚠️ コミットされていない変更があります:" -ForegroundColor Yellow
        Write-Host $gitStatus -ForegroundColor Yellow
        
        $continue = Read-Host "続行しますか？ (y/N)"
        if ($continue -ne "y" -and $continue -ne "Y") {
            Write-Host "❌ 処理を中断しました。" -ForegroundColor Red
            exit 1
        }
    }
    
    if ($CreateTag) {
        Write-Host "🏷️ Gitタグを作成しています..." -ForegroundColor Blue
        $tagName = "v$Version"
        
        # タグの存在確認
        $existingTag = git tag -l $tagName
        if ($existingTag) {
            Write-Host "⚠️ タグ '$tagName' は既に存在します。" -ForegroundColor Yellow
            $overwrite = Read-Host "タグを上書きしますか？ (y/N)"
            if ($overwrite -eq "y" -or $overwrite -eq "Y") {
                git tag -d $tagName
                git push origin --delete $tagName 2>$null
                Write-Host "🗑️ 既存のタグを削除しました。" -ForegroundColor Yellow
            } else {
                Write-Host "⏭️ タグ作成をスキップしました。" -ForegroundColor Yellow
                $CreateTag = $false
            }
        }
        
        if ($CreateTag) {
            git tag -a $tagName -m "Release version $Version - $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
            Write-Host "✅ タグ '$tagName' を作成しました。" -ForegroundColor Green
        }
    }
    
    if ($PushToGitHub) {
        Write-Host "📤 GitHubにプッシュしています..." -ForegroundColor Blue
        
        # 変更をコミット（もしあれば）
        if ($gitStatus) {
            Write-Host "💾 変更をコミットしています..." -ForegroundColor Blue
            git add .
            git commit -m "🚀 Release version $Version

- バージョンを $Version に更新
- リリースビルド準備完了
- 自動生成コミット"
        }
        
        # masterブランチにプッシュ
        git push origin main
        Write-Host "✅ コードをGitHubにプッシュしました。" -ForegroundColor Green
        
        # タグもプッシュ
        if ($CreateTag) {
            git push origin $tagName
            Write-Host "✅ タグ '$tagName' をGitHubにプッシュしました。" -ForegroundColor Green
            Write-Host "🚀 GitHub Actionsが自動的にリリースを作成します！" -ForegroundColor Green
        }
    }
}

Write-Host "`n🎊 すべての処理が完了しました！" -ForegroundColor Green

if ($CreateTag -and $PushToGitHub) {
    Write-Host "📖 GitHubリリースページを確認してください:" -ForegroundColor Yellow
    Write-Host "   https://github.com/winmac924/AnkiPlus_MAUI/releases" -ForegroundColor Cyan
    Write-Host "⏰ GitHub Actionsの処理完了まで数分お待ちください。" -ForegroundColor Yellow
}

# 使用例の表示
Write-Host "`n📚 使用例:" -ForegroundColor Blue
Write-Host "   .\build-release.ps1 -Version `"1.2.0`" -CreateTag -PushToGitHub" -ForegroundColor Cyan
Write-Host "   .\build-release.ps1 -IncrementVersion -VersionType Patch -CreateTag -PushToGitHub" -ForegroundColor Cyan
Write-Host "   .\build-release.ps1 -IncrementVersion -VersionType Minor -CreateTag -PushToGitHub" -ForegroundColor Cyan 