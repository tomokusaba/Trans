# Trans

Trans は、Azure AI Speech のリアルタイム音声翻訳を使って、英語音声を日本語へ翻訳する WPF アプリケーションです。マイクから入力された英語を逐次認識し、その場で日本語訳を表示します。

このアプリは Azure AD 認証を前提にしており、ソースコードに API キーは保持しません。設定値は `local.settings.json` または環境変数から読み込みます。

## 動作環境

このプロジェクトを実行するには、Windows 上で .NET 9 SDK が利用できることと、マイク入力が使えることが必要です。加えて、Azure Speech リソースが作成済みであり、そのリソースに対して実行ユーザーへ適切な RBAC ロールが割り当てられている必要があります。

NuGet パッケージとしては `Microsoft.CognitiveServices.Speech` と `Azure.Identity` を使用しています。

## 認証の前提

認証には `DefaultAzureCredential` を使用します。ローカル開発では、通常は `az login` 済みの Azure CLI 資格情報が使われます。

対象の Azure Speech リソースに対して、実行ユーザーには少なくとも `Cognitive Services User` ロールが必要です。ロールがない場合は、翻訳開始時に 401 エラーになります。

## 設定方法

設定は次の優先順位で読み込まれます。

1. `local.settings.json`
2. 環境変数 `TRANS_SPEECH_REGION`
3. 環境変数 `TRANS_SPEECH_RESOURCE_ID`

ローカルで最も扱いやすい方法は、`local.settings.json.example` を参考に `local.settings.json` を用意することです。

設定ファイルの形式は次のとおりです。

```json
{
  "Speech": {
    "Region": "westus2",
    "ResourceId": "<your-speech-resource-id>"
  }
}
```

`ResourceId` には Azure Speech リソースの完全なリソース ID を指定します。形式は次のようになります。

```text
/subscriptions/<subscription-id>/resourceGroups/<resource-group>/providers/Microsoft.CognitiveServices/accounts/<speech-resource-name>
```

`local.settings.json` は `.gitignore` に含まれているため、GitHub に push されません。共有したい場合は `local.settings.json.example` だけを更新してください。

## 実行手順

最初に Azure へサインインします。

```powershell
az login
```

続いて、このディレクトリでアプリを起動します。

```powershell
dotnet run
```

起動後にリージョンとリソース ID が読み込まれていれば、そのまま「翻訳開始」を押して利用できます。設定が不足している場合は、画面のステータス欄に案内が表示されます。

## ビルド

ビルドは次のコマンドで実行できます。

```powershell
dotnet build
```

## セキュリティ上の注意

このリポジトリには API キーや接続文字列を含めない想定です。Azure のリソース ID やローカル設定ファイルも、公開リポジトリへそのまま含めない運用を前提にしています。

公開前には、少なくとも次の点を確認してください。

1. `local.settings.json` が追跡対象に入っていないこと。
2. 実在する Azure リソース ID をソースコードやドキュメント本文に直接書いていないこと。
3. `bin` と `obj` を含む生成物をコミットしていないこと。

## ファイル構成の要点

`MainWindow.xaml` には UI 定義があります。`MainWindow.xaml.cs` には設定読み込み、Azure AD 認証、Speech Translation の開始と停止、認識結果の更新処理があります。`local.settings.json.example` は公開用のサンプル設定です。