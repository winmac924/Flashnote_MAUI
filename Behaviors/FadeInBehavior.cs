using Microsoft.Maui.Controls;

namespace Flashnote.Behaviors
{
    public class FadeInBehavior : Behavior<Image>
    {
        protected override void OnAttachedTo(Image image)
        {
            base.OnAttachedTo(image);
            image.Opacity = 0;
            image.PropertyChanged += OnImagePropertyChanged;
        }

        protected override void OnDetachingFrom(Image image)
        {
            base.OnDetachingFrom(image);
            image.PropertyChanged -= OnImagePropertyChanged;
        }

        private async void OnImagePropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "IsLoading" && sender is Image image && !image.IsLoading)
            {
                await image.FadeTo(1, 500, Easing.CubicOut);
            }
        }
    }
} 