# 🔐 APIキー・接続文字列のセキュリティ設定

## 📋 概要

このプロジェクトでは、Firebase APIキーとAzure Storage接続文字列を安全に管理するため、設定ファイル方式を採用しています。

## 🔧 初期設定手順

### 1. 設定ファイルのコピー

```bash
# テンプレートファイルから実際の設定ファイルを作成
copy appsettings.example.json appsettings.json
```

### 2. APIキーの設定

`appsettings.json`ファイルを開き、以下の値を実際の値に置き換えてください：

```json
{
  "Firebase": {
    "ApiKey": "YOUR_FIREBASE_API_KEY_HERE",
    "AuthDomain": "YOUR_PROJECT.firebaseapp.com"
  },
  "AzureStorage": {
    "ConnectionString": "YOUR_AZURE_STORAGE_CONNECTION_STRING_HERE"
  }
}
```

## 🛡️ セキュリティ対策

### ✅ 実装済み対策

1. **設定ファイルの分離**
   - APIキーはコードから分離
   - `appsettings.json`は`.gitignore`に追加済み

2. **テンプレートファイル**
   - `appsettings.example.json`で設定項目を明示
   - 実際の値は含まれていない

3. **依存性注入**
   - `ConfigurationService`でAPIキーを管理
   - アプリケーション全体で安全にアクセス

### 🚨 注意事項

1. **appsettings.jsonは絶対にGitHubにアップロードしないでください**
2. **APIキーを他の人と共有しないでください**
3. **定期的にAPIキーをローテーションしてください**

## 🔄 開発チーム用

### 新しい開発者の参加時

1. `appsettings.example.json`を参考に`appsettings.json`を作成
2. 管理者からAPIキーと接続文字列を安全な方法で受け取る
3. 設定ファイルを作成してアプリをテスト

### APIキーの管理

- **Firebase**: Firebase Consoleで管理
- **Azure Storage**: Azure Portalで管理
- **本番環境**: 別途本番用のキーを使用

## 🚀 リリース時の注意

1. **GitHub Actions**: 環境変数やSecretsを使用
2. **配布版**: 設定ファイルは自動的に含まれる
3. **ユーザー**: 設定ファイルの内容は見えない

## 📞 トラブルシューティング

### よくあるエラー

#### ❌ `設定ファイルが見つからない`
- `appsettings.json`ファイルが存在するか確認
- ファイルがプロジェクトに含まれているか確認

#### ❌ `Firebase APIキーが設定されていません`
- `appsettings.json`内のAPIキーが正しいか確認
- Firebase Consoleでプロジェクト設定を確認

#### ❌ `Azure Storage接続文字列が設定されていません`
- Azure Portalで接続文字列を再確認
- アクセスキーが有効か確認

## 🔗 関連ファイル

- `appsettings.example.json` - 設定テンプレート
- `Services/ConfigurationService.cs` - 設定読み込みサービス
- `App.xaml.cs` - アプリケーション初期化
- `.gitignore` - セキュリティ設定 