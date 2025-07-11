using System;
using System.Threading;
using Microsoft.Maui.Controls;

namespace Flashnote;

public partial class UpdateProgressPage : ContentPage
{
    private CancellationTokenSource _cancellationTokenSource;
    
    public UpdateProgressPage()
    {
        InitializeComponent();
        _cancellationTokenSource = new CancellationTokenSource();
    }

    /// <summary>
    /// ダウンロード進捗を更新
    /// </summary>
    public void UpdateProgress(double progress, string status = null, string detail = null)
    {
        try
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                try
                {
                    // UIコンポーネントが存在するかチェック
                    if (ProgressBar == null || ProgressLabel == null || StatusLabel == null || DetailLabel == null)
                    {
                        System.Diagnostics.Debug.WriteLine("UIコンポーネントが初期化されていません");
                        return;
                    }

                    // 進捗バーの更新
                    ProgressBar.Progress = Math.Max(0, Math.Min(1, progress));
                    ProgressLabel.Text = $"{(int)(progress * 100)}%";
                    
                    // ステータスの更新
                    if (!string.IsNullOrEmpty(status))
                    {
                        StatusLabel.Text = status;
                    }
                    
                    // 詳細の更新
                    if (!string.IsNullOrEmpty(detail))
                    {
                        DetailLabel.Text = detail;
                    }
                    
                    System.Diagnostics.Debug.WriteLine($"Progress updated: {progress:P1} - {status} - {detail}");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"UI更新中にエラー: {ex.Message}");
                }
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"MainThread.BeginInvokeOnMainThread呼び出し中にエラー: {ex.Message}");
        }
    }

    /// <summary>
    /// ステータスを更新
    /// </summary>
    public void UpdateStatus(string status, string detail = null)
    {
        try
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                try
                {
                    if (StatusLabel != null)
                    {
                        StatusLabel.Text = status;
                    }
                    
                    if (!string.IsNullOrEmpty(detail) && DetailLabel != null)
                    {
                        DetailLabel.Text = detail;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"ステータス更新中にエラー: {ex.Message}");
                }
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"UpdateStatus呼び出し中にエラー: {ex.Message}");
        }
    }

    /// <summary>
    /// 完了状態を表示
    /// </summary>
    public void ShowComplete(bool success, string message = null)
    {
        try
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                try
                {
                    if (LoadingIndicator != null)
                    {
                        LoadingIndicator.IsRunning = false;
                        LoadingIndicator.IsVisible = false;
                    }
                    
                    if (TitleLabel != null)
                    {
                        TitleLabel.Text = success ? "✅ 完了" : "❌ エラー";
                    }
                    
                    if (StatusLabel != null)
                    {
                        StatusLabel.Text = message ?? (success ? "アップデートが完了しました" : "アップデートに失敗しました");
                    }
                    
                    if (ProgressBar != null && ProgressLabel != null)
                    {
                        ProgressBar.Progress = success ? 1.0 : 0;
                        ProgressLabel.Text = success ? "100%" : "0%";
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"ShowComplete中にエラー: {ex.Message}");
                }
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ShowComplete呼び出し中にエラー: {ex.Message}");
        }
    }

    /// <summary>
    /// エラー状態を表示
    /// </summary>
    public void ShowError(string errorMessage, string detail = null)
    {
        try
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                try
                {
                    if (LoadingIndicator != null)
                    {
                        LoadingIndicator.IsRunning = false;
                        LoadingIndicator.IsVisible = false;
                    }
                    
                    if (TitleLabel != null)
                    {
                        TitleLabel.Text = "❌ エラー";
                    }
                    
                    if (StatusLabel != null)
                    {
                        StatusLabel.Text = errorMessage;
                    }
                    
                    if (ProgressBar != null && ProgressLabel != null)
                    {
                        ProgressBar.Progress = 0;
                        ProgressLabel.Text = "0%";
                    }
                    
                    if (!string.IsNullOrEmpty(detail) && DetailLabel != null)
                    {
                        DetailLabel.Text = detail;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"ShowError中にエラー: {ex.Message}");
                }
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ShowError呼び出し中にエラー: {ex.Message}");
        }
    }

    /// <summary>
    /// キャンセルボタンの表示/非表示を切り替え
    /// </summary>
    public void SetCancelButtonVisible(bool visible)
    {
        try
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                try
                {
                    if (CancelButton != null)
                    {
                        CancelButton.IsVisible = visible;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"SetCancelButtonVisible中にエラー: {ex.Message}");
                }
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"SetCancelButtonVisible呼び出し中にエラー: {ex.Message}");
        }
    }

    /// <summary>
    /// キャンセルボタンのクリックイベント
    /// </summary>
    private void OnCancelClicked(object sender, EventArgs e)
    {
        _cancellationTokenSource?.Cancel();
    }

    /// <summary>
    /// キャンセレーショントークンを取得
    /// </summary>
    public CancellationToken GetCancellationToken()
    {
        return _cancellationTokenSource?.Token ?? CancellationToken.None;
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _cancellationTokenSource?.Cancel();
    }
} 