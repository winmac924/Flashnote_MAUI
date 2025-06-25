using Microsoft.Maui.Controls;

namespace Flashnote
{
    public partial class DropdownMenu : ContentView
    {
        public event Action<string> MenuItemSelected;

        public DropdownMenu()
        {
            InitializeComponent();
        }

        private void OnRenameClicked(object sender, EventArgs e) => MenuItemSelected?.Invoke("名前の変更");
        private void OnExportClicked(object sender, EventArgs e) => MenuItemSelected?.Invoke("書き出す");
        private void OnMoveClicked(object sender, EventArgs e) => MenuItemSelected?.Invoke("移動");
        private void OnShareClicked(object sender, EventArgs e) => MenuItemSelected?.Invoke("共有設定");
        private void OnDeleteClicked(object sender, EventArgs e) => MenuItemSelected?.Invoke("ノートの削除");
    }
}
