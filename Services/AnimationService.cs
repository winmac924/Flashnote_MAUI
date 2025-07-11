using Microsoft.Maui.Animations;
using Flashnote.Models;

namespace Flashnote.Services
{
    public class AnimationService
    {
        private readonly IAnimationManager _animationManager;

        public AnimationService(IAnimationManager animationManager)
        {
            _animationManager = animationManager;
        }

        /// <summary>
        /// 要素にアニメーションを適用
        /// </summary>
        public async Task AnimateElement(VisualElement element, AnimationType animationType, uint duration = 500)
        {
            switch (animationType)
            {
                case AnimationType.FadeIn:
                    await AnimateFadeIn(element, duration);
                    break;
                case AnimationType.SlideIn:
                    await AnimateSlideIn(element, duration);
                    break;
                case AnimationType.Pulse:
                    await AnimatePulse(element, duration);
                    break;
                case AnimationType.Bounce:
                    await AnimateBounce(element, duration);
                    break;
                case AnimationType.Shake:
                    await AnimateShake(element, duration);
                    break;
                case AnimationType.Rotate:
                    await AnimateRotate(element, duration);
                    break;
                case AnimationType.Scale:
                    await AnimateScale(element, duration);
                    break;
            }
        }

        /// <summary>
        /// フェードインアニメーション
        /// </summary>
        private async Task AnimateFadeIn(VisualElement element, uint duration)
        {
            element.Opacity = 0;
            await element.FadeTo(1, duration);
        }

        /// <summary>
        /// スライドインアニメーション
        /// </summary>
        private async Task AnimateSlideIn(VisualElement element, uint duration)
        {
            element.TranslationX = -100;
            element.Opacity = 0;
            
            await Task.WhenAll(
                element.TranslateTo(0, 0, duration, Easing.CubicOut),
                element.FadeTo(1, duration, Easing.CubicOut)
            );
        }

        /// <summary>
        /// パルスアニメーション
        /// </summary>
        private async Task AnimatePulse(VisualElement element, uint duration)
        {
            await element.ScaleTo(1.1, duration / 2, Easing.CubicOut);
            await element.ScaleTo(1.0, duration / 2, Easing.CubicIn);
        }

        /// <summary>
        /// バウンスアニメーション
        /// </summary>
        private async Task AnimateBounce(VisualElement element, uint duration)
        {
            await element.ScaleTo(1.2, duration / 3, Easing.BounceOut);
            await element.ScaleTo(0.9, duration / 3, Easing.BounceIn);
            await element.ScaleTo(1.0, duration / 3, Easing.BounceOut);
        }

        /// <summary>
        /// シェイクアニメーション
        /// </summary>
        private async Task AnimateShake(VisualElement element, uint duration)
        {
            var originalX = element.TranslationX;
            var shakeDistance = 10;
            
            for (int i = 0; i < 5; i++)
            {
                await element.TranslateTo(originalX + shakeDistance, 0, duration / 10);
                await element.TranslateTo(originalX - shakeDistance, 0, duration / 10);
            }
            
            await element.TranslateTo(originalX, 0, duration / 10);
        }

        /// <summary>
        /// 回転アニメーション
        /// </summary>
        private async Task AnimateRotate(VisualElement element, uint duration)
        {
            await element.RotateTo(360, duration, Easing.CubicOut);
        }

        /// <summary>
        /// スケールアニメーション
        /// </summary>
        private async Task AnimateScale(VisualElement element, uint duration)
        {
            await element.ScaleTo(1.2, duration / 2, Easing.CubicOut);
            await element.ScaleTo(1.0, duration / 2, Easing.CubicIn);
        }

        /// <summary>
        /// ハイライトアニメーション（要素を強調表示）
        /// </summary>
        public async Task AnimateHighlight(VisualElement element, uint duration = 1000)
        {
            var originalBackgroundColor = element.BackgroundColor;
            var originalBorderColor = element is Frame frame ? frame.BorderColor : Colors.Transparent;
            
            // ハイライト色を設定
            element.BackgroundColor = Colors.Yellow.WithAlpha(0.3f);
            if (element is Frame frameElement)
            {
                frameElement.BorderColor = Colors.Orange;
            }
            
            // パルスアニメーション
            await AnimatePulse(element, duration);
            
            // 元の色に戻す
            element.BackgroundColor = originalBackgroundColor;
            if (element is Frame frameElement2)
            {
                frameElement2.BorderColor = originalBorderColor;
            }
        }

        /// <summary>
        /// 複数の要素を順番にアニメーション
        /// </summary>
        public async Task AnimateSequence(List<VisualElement> elements, AnimationType animationType, uint delayBetween = 200)
        {
            foreach (var element in elements)
            {
                await AnimateElement(element, animationType);
                await Task.Delay((int)delayBetween);
            }
        }
    }
} 