# AnkiPlus MAUI - バージョン管理スクリプト

param(
    [ValidateSet("Major", "Minor", "Patch")]
    [string]$Type = "Patch",
    [switch]$DryRun = $false,
    [switch]$AutoRelease = $false
)

# カラー出力関数
function Write-ColorOutput {
    param(
        [string]$Message,
        [string]$Color = "White"
    )
    Write-Host $Message -ForegroundColor $Color
}

# 現在のバージョンを取得
function Get-CurrentVersion {
    $csprojPath = "Flashnote_MAUI.csproj"
    if (-not (Test-Path $csprojPath)) {
        Write-ColorOutput "❌ Flashnote_MAUI.csproj が見つかりません。" "Red"
        exit 1
    }
    
    $content = Get-Content $csprojPath -Raw
    if ($content -match '<ApplicationDisplayVersion>(.*?)</ApplicationDisplayVersion>') {
        return $matches[1]
    }
    return "1.0.0"
}

# 次のバージョンを計算
function Get-NextVersion {
    param(
        [string]$CurrentVersion,
        [string]$Type
    )
    
    $parts = $CurrentVersion.Split('.')
    $major = [int]$parts[0]
    $minor = [int]$parts[1]
    $patch = [int]$parts[2]
    
    switch ($Type) {
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

# メイン処理
Write-ColorOutput "🔢 AnkiPlus MAUI バージョン管理" "Green"
Write-ColorOutput "================================" "Green"

$currentVersion = Get-CurrentVersion
$nextVersion = Get-NextVersion -CurrentVersion $currentVersion -Type $Type

Write-ColorOutput "📋 バージョン情報:" "Blue"
Write-ColorOutput "   現在のバージョン: $currentVersion" "Yellow"
Write-ColorOutput "   新しいバージョン: $nextVersion ($Type アップデート)" "Cyan"

if ($DryRun) {
    Write-ColorOutput "`n🔍 ドライランモード - 実際の変更は行いません" "Yellow"
    Write-ColorOutput "実行される処理:" "Blue"
    Write-ColorOutput "   1. .csprojファイルのバージョン更新" "White"
    Write-ColorOutput "   2. Gitコミット作成" "White"
    if ($AutoRelease) {
        Write-ColorOutput "   3. Gitタグ作成" "White"
        Write-ColorOutput "   4. GitHubプッシュ" "White"
        Write-ColorOutput "   5. GitHub Actions実行" "White"
    }
    exit 0
}

# 確認
if (-not $AutoRelease) {
    $confirm = Read-Host "`n続行しますか？ (y/N)"
    if ($confirm -ne "y" -and $confirm -ne "Y") {
        Write-ColorOutput "❌ 処理をキャンセルしました。" "Red"
        exit 0
    }
}

# バージョン更新実行
Write-ColorOutput "`n🚀 バージョン更新を実行します..." "Green"

if ($AutoRelease) {
    # 自動リリースモード
    Write-ColorOutput "🎯 自動リリースモードで実行中..." "Cyan"
    & .\build-release.ps1 -IncrementVersion -VersionType $Type -CreateTag -PushToGitHub
} else {
    # バージョン更新のみ
    Write-ColorOutput "📝 バージョン更新のみを実行中..." "Cyan"
    & .\build-release.ps1 -IncrementVersion -VersionType $Type
}

if ($LASTEXITCODE -eq 0) {
    Write-ColorOutput "`n✅ バージョン更新が完了しました！" "Green"
    if ($AutoRelease) {
        Write-ColorOutput "🚀 GitHub Actionsが自動リリースを作成します。" "Cyan"
        Write-Host "📖 リリース状況: https://github.com/winmac924/Flashnote_MAUI/releases" -ForegroundColor Yellow
        Write-Host "⚙️ Actions状況: https://github.com/winmac924/Flashnote_MAUI/actions" -ForegroundColor Yellow
    }
} else {
    Write-ColorOutput "❌ バージョン更新中にエラーが発生しました。" "Red"
    exit 1
}

Write-ColorOutput "`n📚 使用例:" "Blue"
Write-ColorOutput "   .\version-bump.ps1 -Type Patch              # パッチバージョンアップ" "White"
Write-ColorOutput "   .\version-bump.ps1 -Type Minor              # マイナーバージョンアップ" "White"
Write-ColorOutput "   .\version-bump.ps1 -Type Major              # メジャーバージョンアップ" "White"
Write-ColorOutput "   .\version-bump.ps1 -Type Patch -AutoRelease # パッチアップ + 自動リリース" "White"
Write-ColorOutput "   .\version-bump.ps1 -Type Minor -DryRun      # ドライラン（確認のみ）" "White" 