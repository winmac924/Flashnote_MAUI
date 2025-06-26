using Microsoft.Maui.Controls;

namespace Flashnote
{
    public partial class ImagePopupPage : ContentPage
    {
        public ImagePopupPage(string imagePath, string imageFileName = null)
        {
            InitializeComponent();
            
            // 画像を設定
            PopupImage.Source = imagePath;
            
            // 画像ファイル名を表示（オプション）
            if (!string.IsNullOrEmpty(imageFileName))
            {
                ImageInfoLabel.Text = imageFileName;
                ImageInfoLabel.IsVisible = true;
            }
            
            // タップで閉じる機能を追加
            var tapGestureRecognizer = new TapGestureRecognizer();
            tapGestureRecognizer.Tapped += (s, e) => ClosePopup();
            PopupImage.GestureRecognizers.Add(tapGestureRecognizer);
        }

        private void OnCloseClicked(object sender, EventArgs e)
        {
            ClosePopup();
        }

        private async void ClosePopup()
        {
            await Navigation.PopModalAsync();
        }
    }
} 