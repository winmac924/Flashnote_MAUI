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
        MainThread.BeginInvokeOnMainThread(() =>
        {
            StatusLabel.Text = status;
            
            if (!string.IsNullOrEmpty(detail))
            {
                DetailLabel.Text = detail;
            }
        });
    }

    /// <summary>
    /// 完了状態を表示
    /// </summary>
    public void ShowComplete(bool success, string message = null)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            LoadingIndicator.IsRunning = false;
            LoadingIndicator.IsVisible = false;
            
            if (success)
            {
                TitleLabel.Text = "✅ 完了";
                StatusLabel.Text = message ?? "アップデートが完了しました";
                ProgressBar.Progress = 1.0;
                ProgressLabel.Text = "100%";
            }
            else
            {
                TitleLabel.Text = "❌ エラー";
                StatusLabel.Text = message ?? "アップデートに失敗しました";
                ProgressBar.Progress = 0;
                ProgressLabel.Text = "0%";
            }
        });
    }

    /// <summary>
    /// エラー状態を表示
    /// </summary>
    public void ShowError(string errorMessage, string detail = null)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            LoadingIndicator.IsRunning = false;
            LoadingIndicator.IsVisible = false;
            
            TitleLabel.Text = "❌ エラー";
            StatusLabel.Text = errorMessage;
            ProgressBar.Progress = 0;
            ProgressLabel.Text = "0%";
            
            if (!string.IsNullOrEmpty(detail))
            {
                DetailLabel.Text = detail;
            }
        });
    }

    /// <summary>
    /// キャンセルボタンの表示/非表示を切り替え
    /// </summary>
    public void SetCancelButtonVisible(bool visible)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            CancelButton.IsVisible = visible;
        });
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