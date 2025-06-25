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

        private void OnRenameClicked(object sender, EventArgs e) => MenuItemSelected?.Invoke("���O�̕ύX");
        private void OnExportClicked(object sender, EventArgs e) => MenuItemSelected?.Invoke("�����o��");
        private void OnMoveClicked(object sender, EventArgs e) => MenuItemSelected?.Invoke("�ړ�");
        private void OnShareClicked(object sender, EventArgs e) => MenuItemSelected?.Invoke("���L�ݒ�");
        private void OnDeleteClicked(object sender, EventArgs e) => MenuItemSelected?.Invoke("�m�[�g�̍폜");
    }
}
