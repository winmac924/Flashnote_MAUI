using System;
using Microsoft.Maui.Controls;
using System.IO;

namespace Flashnote.Views
{
    public class PdfViewerPage : ContentPage
    {
        public PdfViewerPage(string pdfPath)
        {
            Title = "PDF Viewer";

            var fileNameLabel = new Label
            {
                Text = Path.GetFileName(pdfPath),
                FontAttributes = FontAttributes.Bold,
                VerticalOptions = LayoutOptions.Center
            };

            var closeButton = new Button
            {
                Text = "•Â‚¶‚é",
                VerticalOptions = LayoutOptions.Center
            };
            closeButton.Clicked += async (s, e) => await Navigation.PopModalAsync();

            var header = new Grid { Padding = 10, BackgroundColor = Colors.Transparent };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.Add(fileNameLabel);
            header.Add(closeButton);
            Grid.SetColumn(closeButton, 1);

            var webView = new WebView { VerticalOptions = LayoutOptions.FillAndExpand };
            try
            {
                var url = pdfPath;
                if (!url.Contains("://"))
                {
                    url = "file:///" + pdfPath.Replace('\\', '/');
                }
                webView.Source = new UrlWebViewSource { Url = url };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"PdfViewerPage error: {ex}");
            }

            var layout = new Grid();
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Star });
            layout.Add(header);
            layout.Add(webView);
            Grid.SetRow(webView, 1);

            Content = layout;
        }
    }
}