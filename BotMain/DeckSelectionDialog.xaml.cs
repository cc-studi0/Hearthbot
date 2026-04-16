using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;

namespace BotMain
{
    public partial class DeckSelectionDialog : Window
    {
        public ObservableCollection<DeckSelectionItem> DeckItems { get; } = new();

        public IReadOnlyList<string> SelectedDeckNames => DeckItems
            .Where(item => item.IsSelected)
            .Select(item => item.Name)
            .ToList();

        public DeckSelectionDialog(IReadOnlyList<string> availableDecks, IReadOnlyList<string> selectedDecks)
        {
            InitializeComponent();
            DataContext = this;

            var selected = new HashSet<string>(
                DeckSelectionState.Normalize(selectedDecks),
                System.StringComparer.OrdinalIgnoreCase);

            foreach (var deck in DeckSelectionState.Normalize(availableDecks))
            {
                DeckItems.Add(new DeckSelectionItem
                {
                    Name = deck,
                    IsSelected = selected.Contains(deck)
                });
            }
        }

        private void OnSelectAll(object sender, RoutedEventArgs e)
        {
            foreach (var item in DeckItems)
                item.IsSelected = true;
        }

        private void OnClear(object sender, RoutedEventArgs e)
        {
            foreach (var item in DeckItems)
                item.IsSelected = false;
        }

        private void OnOk(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }
    }

    public class DeckSelectionItem : INotifyPropertyChanged
    {
        private bool _isSelected;

        public string Name { get; set; } = string.Empty;

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value)
                    return;

                _isSelected = value;
                Notify();
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void Notify([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
