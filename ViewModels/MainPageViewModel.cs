using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Flashnote.Models;

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
    }

    public class Note
    {
        public string Name { get; set; }
        public string Icon { get; set; }
        public bool IsFolder { get; set; }
        public string FullPath { get; set; }
        public DateTime LastModified { get; set; }
    }
} 