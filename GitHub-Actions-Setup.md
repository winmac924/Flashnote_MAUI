# GitHub Actions 設定手順書

## 🔧 GitHubリポジトリでの設定

### 1. Actions の権限設定

1. GitHubリポジトリページで **Settings** タブをクリック
2. 左メニューから **Actions** → **General** を選択
3. **Workflow permissions** セクションで以下を設定：
   - ✅ **Read and write permissions** を選択
   - ✅ **Allow GitHub Actions to create and approve pull requests** にチェック

### 2. Actions の有効化

1. リポジトリの **Actions** タブをクリック
2. 「**I understand my workflows, go ahead and enable them**」をクリック

## 🚀 修正内容

### 修正された問題：

1. **権限エラー (403)** → `permissions:` セクションを追加
2. **ファイルパスエラー** → 複数パスパターンでの検索機能を追加
3. **デバッグ機能** → ファイル一覧表示ステップを追加
4. **Action バージョン** → `softprops/action-gh-release@v2` に更新

## 🔍 トラブルシューティング

### エラーが続く場合の確認手順：

1. **Actions タブ**でワークフローの詳細ログを確認
2. **Debug: List Files** ステップでファイル構造を確認
3. **Find Executable Files** ステップでパス検索結果を確認

### よくあるエラーと対処法：

#### ❌ `Pattern does not match any files`
- **原因**: EXEファイルのパスが見つからない
- **対処**: Debug ステップでファイル構造を確認し、パスを修正

#### ❌ `GitHub release failed with status: 403`
- **原因**: GitHub Actionsの権限不足
- **対処**: リポジトリ設定で権限を「Read and write permissions」に変更

#### ❌ `No EXE files found`
- **原因**: ビルドが失敗してEXEファイルが生成されていない
- **対処**: ビルドログを確認し、依存関係やプロジェクト設定を確認

## 📋 次回リリース手順

1. コードを修正・コミット
2. PowerShellでリリース実行：
   ```powershell
   .\build-release.ps1 -Version "1.0.1" -CreateTag -PushToGitHub
   ```
3. GitHub Actionsの実行結果を確認
4. リリースページで成果物を確認

## 🔗 参考リンク

- [GitHub Actions Permissions](https://docs.github.com/en/actions/security-guides/automatic-token-authentication#permissions-for-the-github_token)
- [softprops/action-gh-release](https://github.com/softprops/action-gh-release)
- [.NET Publish Options](https://docs.microsoft.com/en-us/dotnet/core/tools/dotnet-publish) 