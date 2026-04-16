using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace BotMain
{
    public class DeckSelectionListState : INotifyPropertyChanged
    {
        public ObservableCollection<DeckSelectionItem> Items { get; } = new();

        private bool _isDropDownOpen;

        public bool IsDropDownOpen
        {
            get => _isDropDownOpen;
            set
            {
                if (_isDropDownOpen == value)
                    return;

                _isDropDownOpen = value;
                Notify();
            }
        }

        public IReadOnlyList<string> SelectedDeckNames => DeckSelectionState.Normalize(
            Items.Where(item => item.IsSelected).Select(item => item.Name));

        public string Summary => DeckSelectionState.BuildSummary(SelectedDeckNames);

        public void SetDecks(IEnumerable<string> availableDecks, IEnumerable<string> selectedDecks)
        {
            foreach (var item in Items)
                item.PropertyChanged -= OnItemPropertyChanged;

            Items.Clear();

            var selected = new HashSet<string>(
                DeckSelectionState.Normalize(selectedDecks),
                System.StringComparer.OrdinalIgnoreCase);

            foreach (var deck in DeckSelectionState.Normalize(availableDecks))
            {
                var item = new DeckSelectionItem
                {
                    Name = deck,
                    IsSelected = selected.Contains(deck)
                };
                item.PropertyChanged += OnItemPropertyChanged;
                Items.Add(item);
            }

            Notify(nameof(SelectedDeckNames));
            Notify(nameof(Summary));
        }

        private void OnItemPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(DeckSelectionItem.IsSelected))
                return;

            Notify(nameof(SelectedDeckNames));
            Notify(nameof(Summary));
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void Notify([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
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
