using System.IO;
using System.Text.Json;
using System.Windows;
using Azure.Core;
using Azure.Identity;
using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using Microsoft.CognitiveServices.Speech.Translation;

namespace Trans;

/// <summary>
/// Azure AI Speech を使って英語音声を日本語へリアルタイム翻訳するメイン画面です。
/// </summary>
public partial class MainWindow : Window
{
    private const string SourceLanguage = "en-US";
    private const string TargetLanguage = "ja";
    private const string LocalSettingsFileName = "local.settings.json";
    private const string SpeechRegionEnvironmentVariable = "TRANS_SPEECH_REGION";
    private const string SpeechResourceIdEnvironmentVariable = "TRANS_SPEECH_RESOURCE_ID";

    private TranslationRecognizer? _recognizer;
    private bool _isTranslating;

    /// <summary>
    /// 画面を初期化し、外部設定から接続情報を読み込みます。
    /// </summary>
    public MainWindow()
    {
        InitializeComponent();
        LoadConfiguration();
    }

    /// <summary>
    /// 音声翻訳を開始します。
    /// </summary>
    /// <param name="sender">イベント送信元のコントロールです。</param>
    /// <param name="e">クリックイベントの引数です。</param>
    private async void StartButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isTranslating) return;

        var region = RegionTextBox.Text.Trim();
        var resourceId = ResourceIdTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(region) || string.IsNullOrWhiteSpace(resourceId)
            || resourceId.Contains("<subscription-id>"))
        {
            UpdateStatus("⚠ リージョンとリソースIDを正しく設定してください");
            return;
        }

        StartButton.IsEnabled = false;

        try
        {
            UpdateStatus("🔑 DefaultAzureCredential で認証トークンを取得中...");

            var credential = new DefaultAzureCredential();
            var tokenRequest = new TokenRequestContext(["https://cognitiveservices.azure.com/.default"]);
            var accessToken = await credential.GetTokenAsync(tokenRequest);

            // Speech SDK の AAD 認証では、resource ID を含む専用形式のトークン文字列を使う。
            var authorizationToken = $"aad#{resourceId}#{accessToken.Token}";

            var config = SpeechTranslationConfig.FromAuthorizationToken(authorizationToken, region);
            config.SpeechRecognitionLanguage = SourceLanguage;
            config.AddTargetLanguage(TargetLanguage);

            var audioConfig = AudioConfig.FromDefaultMicrophoneInput();
            _recognizer = new TranslationRecognizer(config, audioConfig);

            _recognizer.Recognizing += OnRecognizing;
            _recognizer.Recognized += OnRecognized;
            _recognizer.Canceled += OnCanceled;
            _recognizer.SessionStarted += OnSessionStarted;
            _recognizer.SessionStopped += OnSessionStopped;

            await _recognizer.StartContinuousRecognitionAsync();

            _isTranslating = true;
            StopButton.IsEnabled = true;
            RegionTextBox.IsEnabled = false;
            ResourceIdTextBox.IsEnabled = false;
        }
        catch (Exception ex)
        {
            UpdateStatus($"❌ エラー: {ex.Message}");
            StartButton.IsEnabled = true;
        }
    }

    /// <summary>
    /// 実行中の音声翻訳を停止します。
    /// </summary>
    /// <param name="sender">イベント送信元のコントロールです。</param>
    /// <param name="e">クリックイベントの引数です。</param>
    private async void StopButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_isTranslating || _recognizer is null) return;

        StopButton.IsEnabled = false;
        UpdateStatus("⏳ 翻訳を停止中...");

        try
        {
            await _recognizer.StopContinuousRecognitionAsync();
        }
        catch (Exception ex)
        {
            UpdateStatus($"❌ 停止エラー: {ex.Message}");
        }
        finally
        {
            DisposeRecognizer();
            _isTranslating = false;
            StartButton.IsEnabled = true;
            RegionTextBox.IsEnabled = true;
            ResourceIdTextBox.IsEnabled = true;
            CurrentRecognitionText.Text = "";
            CurrentTranslationText.Text = "";
            UpdateStatus("🟡 停止しました");
        }
    }

    /// <summary>
    /// 認識結果と翻訳結果の履歴表示をクリアします。
    /// </summary>
    /// <param name="sender">イベント送信元のコントロールです。</param>
    /// <param name="e">クリックイベントの引数です。</param>
    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        RecognizedTextBox.Clear();
        TranslatedTextBox.Clear();
    }

    /// <summary>
    /// 翻訳セッションの開始時にステータス表示を更新します。
    /// </summary>
    /// <param name="sender">イベント送信元です。</param>
    /// <param name="e">セッション開始イベントの引数です。</param>
    private void OnSessionStarted(object? sender, SessionEventArgs e)
    {
        Dispatcher.Invoke(() =>
            UpdateStatus("🟢 翻訳中 — マイクに向かって英語を話してください 🎤"));
    }

    /// <summary>
    /// 認識途中のテキストと暫定翻訳をリアルタイム表示へ反映します。
    /// </summary>
    /// <param name="sender">イベント送信元です。</param>
    /// <param name="e">翻訳認識イベントの引数です。</param>
    private void OnRecognizing(object? sender, TranslationRecognitionEventArgs e)
    {
        if (e.Result.Reason != ResultReason.TranslatingSpeech) return;

        e.Result.Translations.TryGetValue(TargetLanguage, out var japanese);

        Dispatcher.Invoke(() =>
        {
            CurrentRecognitionText.Text = e.Result.Text;
            CurrentTranslationText.Text = japanese ?? "";
        });
    }

    /// <summary>
    /// 認識確定後の原文と翻訳文を履歴へ追加します。
    /// </summary>
    /// <param name="sender">イベント送信元です。</param>
    /// <param name="e">翻訳認識イベントの引数です。</param>
    private void OnRecognized(object? sender, TranslationRecognitionEventArgs e)
    {
        switch (e.Result.Reason)
        {
            case ResultReason.TranslatedSpeech:
                e.Result.Translations.TryGetValue(TargetLanguage, out var japanese);
                Dispatcher.Invoke(() =>
                {
                    if (!string.IsNullOrWhiteSpace(e.Result.Text))
                    {
                        RecognizedTextBox.AppendText(e.Result.Text + Environment.NewLine);
                        RecognizedTextBox.ScrollToEnd();

                        TranslatedTextBox.AppendText((japanese ?? "") + Environment.NewLine);
                        TranslatedTextBox.ScrollToEnd();
                    }

                    CurrentRecognitionText.Text = "";
                    CurrentTranslationText.Text = "";
                });
                break;

            case ResultReason.RecognizedSpeech:
                Dispatcher.Invoke(() =>
                {
                    RecognizedTextBox.AppendText(e.Result.Text + " (翻訳なし)" + Environment.NewLine);
                    RecognizedTextBox.ScrollToEnd();
                    CurrentRecognitionText.Text = "";
                    CurrentTranslationText.Text = "";
                });
                break;

            case ResultReason.NoMatch:
                Dispatcher.Invoke(() =>
                    UpdateStatus("🟡 音声を認識できませんでした — もう一度お試しください"));
                break;
        }
    }

    /// <summary>
    /// Speech SDK からのキャンセルやエラーを受け取り、画面状態を復旧します。
    /// </summary>
    /// <param name="sender">イベント送信元です。</param>
    /// <param name="e">キャンセルイベントの引数です。</param>
    private void OnCanceled(object? sender, TranslationRecognitionCanceledEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            if (e.Reason == CancellationReason.Error)
            {
                UpdateStatus($"❌ エラー: {e.ErrorCode} — {e.ErrorDetails}");
            }
            else
            {
                UpdateStatus("🟡 翻訳がキャンセルされました");
            }

            _isTranslating = false;
            StartButton.IsEnabled = true;
            StopButton.IsEnabled = false;
            RegionTextBox.IsEnabled = true;
            ResourceIdTextBox.IsEnabled = true;
        });
    }

    /// <summary>
    /// セッション終了時に UI の状態を停止状態へ戻します。
    /// </summary>
    /// <param name="sender">イベント送信元です。</param>
    /// <param name="e">セッション終了イベントの引数です。</param>
    private void OnSessionStopped(object? sender, SessionEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            if (_isTranslating)
            {
                UpdateStatus("🟡 セッションが終了しました");
                _isTranslating = false;
                StartButton.IsEnabled = true;
                StopButton.IsEnabled = false;
                RegionTextBox.IsEnabled = true;
                ResourceIdTextBox.IsEnabled = true;
            }
        });
    }

    /// <summary>
    /// ウィンドウ終了時に認識セッションを安全に停止して破棄します。
    /// </summary>
    /// <param name="sender">イベント送信元のウィンドウです。</param>
    /// <param name="e">終了イベントの引数です。</param>
    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_recognizer is not null)
        {
            _recognizer.StopContinuousRecognitionAsync().Wait(TimeSpan.FromSeconds(5));
            DisposeRecognizer();
        }
    }

    /// <summary>
    /// 認識オブジェクトのイベント購読を解除し、リソースを解放します。
    /// </summary>
    private void DisposeRecognizer()
    {
        if (_recognizer is null) return;

        _recognizer.Recognizing -= OnRecognizing;
        _recognizer.Recognized -= OnRecognized;
        _recognizer.Canceled -= OnCanceled;
        _recognizer.SessionStarted -= OnSessionStarted;
        _recognizer.SessionStopped -= OnSessionStopped;
        _recognizer.Dispose();
        _recognizer = null;
    }

    /// <summary>
    /// 画面上のステータス表示を更新します。
    /// </summary>
    /// <param name="message">表示するメッセージです。</param>
    private void UpdateStatus(string message)
    {
        StatusText.Text = message;
    }

    /// <summary>
    /// 設定ファイルまたは環境変数から接続情報を読み込み、UI 初期値へ反映します。
    /// </summary>
    private void LoadConfiguration()
    {
        // ローカル実行を優先しつつ、公開リポジトリに機密値を残さないため外部設定から読み込む。
        var settings = LoadLocalSettings();

        RegionTextBox.Text = FirstNonEmpty(
            settings.Region,
            Environment.GetEnvironmentVariable(SpeechRegionEnvironmentVariable),
            "westus2");

        ResourceIdTextBox.Text = FirstNonEmpty(
            settings.ResourceId,
            Environment.GetEnvironmentVariable(SpeechResourceIdEnvironmentVariable),
            "<resource-id>");

        if (settings.Exists)
        {
            UpdateStatus("🟡 local.settings.json を読み込みました");
            return;
        }

        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(SpeechRegionEnvironmentVariable))
            || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(SpeechResourceIdEnvironmentVariable)))
        {
            UpdateStatus("🟡 環境変数から設定を読み込みました");
            return;
        }

        UpdateStatus("🟡 local.settings.json または環境変数を設定してから翻訳を開始してください");
    }

    /// <summary>
    /// 出力先に配置された local.settings.json を読み込みます。
    /// </summary>
    /// <returns>設定ファイルの存在有無と読み取れた値です。</returns>
    private static LocalSettings LoadLocalSettings()
    {
        // dotnet run 時も参照できるよう、出力先へコピーされた local.settings.json を読む。
        var settingsPath = Path.Combine(AppContext.BaseDirectory, LocalSettingsFileName);
        if (!File.Exists(settingsPath))
        {
            return new LocalSettings(false, null, null);
        }

        try
        {
            using var stream = File.OpenRead(settingsPath);
            using var document = JsonDocument.Parse(stream);

            if (!document.RootElement.TryGetProperty("Speech", out var speechElement))
            {
                return new LocalSettings(true, null, null);
            }

            var region = speechElement.TryGetProperty("Region", out var regionElement)
                ? regionElement.GetString()
                : null;

            var resourceId = speechElement.TryGetProperty("ResourceId", out var resourceIdElement)
                ? resourceIdElement.GetString()
                : null;

            return new LocalSettings(true, region, resourceId);
        }
        catch
        {
            return new LocalSettings(true, null, null);
        }
    }

    /// <summary>
    /// 候補の中から最初に空でない文字列を返します。
    /// </summary>
    /// <param name="values">優先順に評価する文字列群です。</param>
    /// <returns>最初に見つかった有効な文字列、なければ空文字列です。</returns>
    private static string FirstNonEmpty(params string?[] values)
    {
        // 設定ファイル、環境変数、既定値の順で最初に使える値を採用する。
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return string.Empty;
    }

    private readonly record struct LocalSettings(bool Exists, string? Region, string? ResourceId);
}
