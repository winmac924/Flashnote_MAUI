using Flashnote.Models;
using Flashnote.Services;
using Microsoft.Maui.Animations;
using System.Collections.ObjectModel;

namespace Flashnote.Views
{
    public partial class HelpOverlay : ContentView
    {
        private HelpData _currentHelpData;
        private int _currentStepIndex = 0;
        private ObservableCollection<HelpStep> _visibleSteps;
        private bool _isAnimating = false;
        private AnimationService _animationService;
        private bool _isVideoPlaying = false;

        public event EventHandler HelpClosed;

        public HelpOverlay()
        {
            InitializeComponent();
            _visibleSteps = new ObservableCollection<HelpStep>();
            
            // 背景タップで閉じる
            var tapGesture = new TapGestureRecognizer();
            tapGesture.Tapped += (s, e) => CloseHelp();
            BackgroundOverlay.GestureRecognizers.Add(tapGesture);
        }

        public async Task ShowHelp(HelpType helpType)
        {
            _currentHelpData = HelpService.GetHelpData(helpType);
            if (_currentHelpData == null) return;

            _currentStepIndex = 0;
            UpdateHelpContent();
            await ShowHelpAnimation();
        }

        private void UpdateHelpContent()
        {
            if (_currentHelpData == null) return;

            HelpIcon.Text = _currentHelpData.Icon;
            HelpTitle.Text = _currentHelpData.Title;
            HelpDescription.Text = _currentHelpData.Description;

            // メディア表示を更新
            UpdateMediaDisplay();
            
            // ステップを更新
            UpdateSteps();
            UpdateNavigationButtons();
        }

        private void UpdateMediaDisplay()
        {
            if (_currentHelpData == null) return;

            // メディアコンテナをリセット
            MediaContainer.IsVisible = false;
            VideoPlayer.IsVisible = false;
            ScreenshotImage.IsVisible = false;
            AnimationContainer.IsVisible = false;
            PlayButton.IsVisible = false;
            PauseButton.IsVisible = false;

            // 現在のステップのメディアを表示
            if (_currentStepIndex < _currentHelpData.Steps.Count)
            {
                var currentStep = _currentHelpData.Steps[_currentStepIndex];
                
                // 動画がある場合
                if (currentStep.HasVideo && !string.IsNullOrEmpty(currentStep.VideoUrl))
                {
                    ShowVideo(currentStep.VideoUrl);
                }
                // スクリーンショットがある場合
                else if (currentStep.HasScreenshot && !string.IsNullOrEmpty(currentStep.ScreenshotPath))
                {
                    ShowScreenshot(currentStep.ScreenshotPath);
                }
                // アニメーションがある場合
                else if (currentStep.AnimationType != AnimationType.None)
                {
                    ShowAnimation(currentStep);
                }
                // ヘルプ全体に動画がある場合
                else if (_currentHelpData.HasVideo && !string.IsNullOrEmpty(_currentHelpData.VideoUrl))
                {
                    ShowVideo(_currentHelpData.VideoUrl);
                }
                // ヘルプ全体にスクリーンショットがある場合
                else if (_currentHelpData.HasScreenshot && !string.IsNullOrEmpty(_currentHelpData.ScreenshotPath))
                {
                    ShowScreenshot(_currentHelpData.ScreenshotPath);
                }
            }
        }

        private void ShowVideo(string videoUrl)
        {
            MediaContainer.IsVisible = true;
            VideoPlayer.IsVisible = true;
            PlayButton.IsVisible = true;
            
            // HTML5動画プレイヤーを作成
            var htmlContent = $@"
                <html>
                <head>
                    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
                    <style>
                        body {{ margin: 0; padding: 0; background: transparent; }}
                        video {{ 
                            width: 100%; 
                            height: 100%; 
                            object-fit: contain;
                            border-radius: 8px;
                        }}
                    </style>
                </head>
                <body>
                    <video id='helpVideo' controls>
                        <source src='{videoUrl}' type='video/mp4'>
                        お使いのブラウザは動画再生をサポートしていません。
                    </video>
                </body>
                </html>";
            
            VideoPlayer.Source = new HtmlWebViewSource { Html = htmlContent };
        }

        private void ShowScreenshot(string screenshotPath)
        {
            MediaContainer.IsVisible = true;
            ScreenshotImage.IsVisible = true;
            
            ScreenshotImage.Source = ImageSource.FromFile(screenshotPath);
        }

        private async void ShowAnimation(HelpStep step)
        {
            MediaContainer.IsVisible = true;
            AnimationContainer.IsVisible = true;
            
            // アニメーションアイコンを設定
            AnimationLabel.Text = step.Animation;
            
            // アニメーションタイプに応じてアニメーションを実行
            if (step.AnimationType != AnimationType.None)
            {
                await AnimateStep(step);
            }
        }

        private async Task AnimateStep(HelpStep step)
        {
            if (_animationService == null)
            {
                _animationService = new AnimationService(Application.Current.Handler.MauiContext.Services.GetService<IAnimationManager>());
            }

            await _animationService.AnimateElement(AnimationLabel, step.AnimationType, 1000);
        }

        private void UpdateSteps()
        {
            StepsContainer.Children.Clear();
            _visibleSteps.Clear();

            // 現在のステップのみを表示
            if (_currentStepIndex < _currentHelpData.Steps.Count)
            {
                var currentStep = _currentHelpData.Steps[_currentStepIndex];
                _visibleSteps.Add(currentStep);

                var stepCard = CreateStepCard(currentStep);
                StepsContainer.Children.Add(stepCard);
            }
        }

        private Frame CreateStepCard(HelpStep step)
        {
            var card = new Frame
            {
                Style = (Style)Resources["StepCardStyle"],
                Opacity = 0,
                Scale = 0.9
            };

            var content = new Grid();
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });

            // アニメーションアイコン
            var iconLabel = new Label
            {
                Text = step.Animation,
                FontSize = 24,
                VerticalOptions = LayoutOptions.Center,
                Margin = new Thickness(0, 0, 12, 0)
            };

            // ステップ内容
            var stepContent = new StackLayout
            {
                Spacing = 4
            };

            var titleLabel = new Label
            {
                Text = step.Title,
                FontSize = 16,
                FontAttributes = FontAttributes.Bold,
                TextColor = Application.Current.RequestedTheme == AppTheme.Light ? Color.FromHex("#1F1F1F") : Color.FromHex("#FFFFFF")
            };

            var descriptionLabel = new Label
            {
                Text = step.Description,
                FontSize = 14,
                TextColor = Application.Current.RequestedTheme == AppTheme.Light ? Color.FromHex("#666666") : Color.FromHex("#CCCCCC"),
                LineBreakMode = LineBreakMode.WordWrap
            };

            stepContent.Children.Add(titleLabel);
            stepContent.Children.Add(descriptionLabel);

            content.Children.Add(iconLabel);
            Grid.SetColumn(iconLabel, 0);
            content.Children.Add(stepContent);
            Grid.SetColumn(stepContent, 1);

            card.Content = content;

            // アニメーション
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                await Task.Delay(300);
                await card.FadeTo(1, 300, Easing.CubicOut);
                await card.ScaleTo(1, 300, Easing.CubicOut);
            });

            return card;
        }

        private void UpdateNavigationButtons()
        {
            PreviousButton.IsVisible = _currentStepIndex > 0;
            NextButton.IsVisible = _currentStepIndex < _currentHelpData.Steps.Count - 1;
            FinishButton.IsVisible = _currentStepIndex == _currentHelpData.Steps.Count - 1;
        }

        private async Task ShowHelpAnimation()
        {
            if (_isAnimating) return;
            _isAnimating = true;

            IsVisible = true;

            // 背景フェードイン
            await BackgroundOverlay.FadeTo(1, 300, Easing.CubicOut);

            // カードアニメーション
            await HelpCard.FadeTo(1, 400, Easing.CubicOut);
            await HelpCard.ScaleTo(1, 400, Easing.CubicOut);

            _isAnimating = false;
        }

        private async Task HideHelpAnimation()
        {
            if (_isAnimating) return;
            _isAnimating = true;

            // カードアニメーション
            await HelpCard.ScaleTo(0.8, 300, Easing.CubicIn);
            await HelpCard.FadeTo(0, 300, Easing.CubicIn);

            // 背景フェードアウト
            await BackgroundOverlay.FadeTo(0, 300, Easing.CubicIn);

            IsVisible = false;
            _isAnimating = false;
        }

        private async void OnCloseClicked(object sender, EventArgs e)
        {
            await CloseHelp();
        }

        private async void OnPreviousClicked(object sender, EventArgs e)
        {
            if (_currentStepIndex > 0)
            {
                _currentStepIndex--;
                UpdateHelpContent();
            }
        }

        private async void OnNextClicked(object sender, EventArgs e)
        {
            if (_currentStepIndex < _currentHelpData.Steps.Count - 1)
            {
                _currentStepIndex++;
                UpdateHelpContent();
            }
        }

        private async void OnFinishClicked(object sender, EventArgs e)
        {
            await CloseHelp();
        }

        private void OnPlayClicked(object sender, EventArgs e)
        {
            if (VideoPlayer.IsVisible)
            {
                // JavaScriptで動画を再生
                VideoPlayer.EvaluateJavaScriptAsync("document.getElementById('helpVideo').play();");
                _isVideoPlaying = true;
                PlayButton.IsVisible = false;
                PauseButton.IsVisible = true;
            }
        }

        private void OnPauseClicked(object sender, EventArgs e)
        {
            if (VideoPlayer.IsVisible)
            {
                // JavaScriptで動画を一時停止
                VideoPlayer.EvaluateJavaScriptAsync("document.getElementById('helpVideo').pause();");
                _isVideoPlaying = false;
                PlayButton.IsVisible = true;
                PauseButton.IsVisible = false;
            }
        }

        public async Task CloseHelp()
        {
            // 動画を停止
            if (_isVideoPlaying && VideoPlayer.IsVisible)
            {
                VideoPlayer.EvaluateJavaScriptAsync("document.getElementById('helpVideo').pause();");
                _isVideoPlaying = false;
            }

            await HideHelpAnimation();
            HelpClosed?.Invoke(this, EventArgs.Empty);
        }
    }
} 