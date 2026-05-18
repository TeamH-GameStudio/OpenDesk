using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using TestPokedex.Common;
using TestPokedex.Models;

namespace TestPokedex.ViewModels
{
    public sealed class PokedexViewModel : ObservableObject, IDisposable
    {
        private readonly List<PokemonItemViewModel> _allItems;
        private readonly List<PokemonItemViewModel> _filteredItems = new List<PokemonItemViewModel>();

        public IReadOnlyList<PokemonItemViewModel> FilteredItems => _filteredItems;

        public event Action FilteredItemsChanged;
        public event Action<PokemonItemViewModel> ItemFavoriteChanged;

        private string _searchQuery = string.Empty;
        public string SearchQuery
        {
            get => _searchQuery;
            set
            {
                if (SetField(ref _searchQuery, value ?? string.Empty))
                {
                    ApplyFilter();
                }
            }
        }

        private bool _favoritesOnly;
        public bool FavoritesOnly
        {
            get => _favoritesOnly;
            set
            {
                if (SetField(ref _favoritesOnly, value))
                {
                    ApplyFilter();
                }
            }
        }

        private PokemonItemViewModel _selected;
        public PokemonItemViewModel Selected
        {
            get => _selected;
            set => SetField(ref _selected, value);
        }

        public PokedexViewModel(IEnumerable<Pokemon> models)
        {
            if (models == null)
            {
                throw new ArgumentNullException(nameof(models));
            }

            _allItems = models.Select(m => new PokemonItemViewModel(m)).ToList();
            foreach (var item in _allItems)
            {
                item.PropertyChanged += OnItemPropertyChanged;
            }
            ApplyFilter();
        }

        public void ToggleFavorite(PokemonItemViewModel item)
        {
            if (item == null) return;
            item.IsFavorite = !item.IsFavorite;
        }

        private void OnItemPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(PokemonItemViewModel.IsFavorite)) return;
            var item = (PokemonItemViewModel)sender;
            ItemFavoriteChanged?.Invoke(item);
            if (_favoritesOnly)
            {
                ApplyFilter();
            }
        }

        private void ApplyFilter()
        {
            _filteredItems.Clear();
            var query = _searchQuery.Trim().ToLowerInvariant();
            for (var i = 0; i < _allItems.Count; i++)
            {
                var item = _allItems[i];
                if (_favoritesOnly && !item.IsFavorite) continue;
                if (query.Length > 0 && !MatchesQuery(item, query)) continue;
                _filteredItems.Add(item);
            }

            if (_selected != null && !_filteredItems.Contains(_selected))
            {
                Selected = null;
            }

            FilteredItemsChanged?.Invoke();
        }

        private static bool MatchesQuery(PokemonItemViewModel item, string query)
        {
            if (item.Name.ToLowerInvariant().Contains(query)) return true;
            if (item.Type1.ToLowerInvariant().Contains(query)) return true;
            if (!string.IsNullOrEmpty(item.Type2) && item.Type2.ToLowerInvariant().Contains(query)) return true;
            if (item.DisplayNumber.Contains(query)) return true;
            return false;
        }

        public void Dispose()
        {
            foreach (var item in _allItems)
            {
                item.PropertyChanged -= OnItemPropertyChanged;
            }
            FilteredItemsChanged = null;
            ItemFavoriteChanged = null;
        }
    }
}
