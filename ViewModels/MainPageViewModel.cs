using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Flashnote.Models;
using System.Windows.Input;
using Microsoft.Maui.Graphics;

namespace Flashnote.ViewModels
{
    public class MainPageViewModel : INotifyPropertyChanged
    {
        private ObservableCollection<Note> _notes;
        public ObservableCollection<Note> Notes
        {
            get => _notes;
            set
            {
                _notes = value;
                OnPropertyChanged();
            }
        }

        public MainPageViewModel()
        {
            Notes = new ObservableCollection<Note>();
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public event EventHandler<Note> ContextMenuRequested;

        public ICommand ShowContextMenuCommand => new Command<Note>(note =>
        {
            if (note == null) return;
            ContextMenuRequested?.Invoke(this, note);
        });
    }

    public class Note
    {
        public string Name { get; set; }
        public string Icon { get; set; }
        public bool IsFolder { get; set; }
        public string FullPath { get; set; }
        public DateTime LastModified { get; set; }

        /// <summary>
        /// metadata.json の folderColor / noteColor を反映した表示色。
        /// 色が設定されていない場合は Colors.Transparent（=カード上に何も表示しない）。
        /// </summary>
        public Color Color { get; set; } = Colors.Transparent;
    }
}