using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using MusicCollectionTest.Common;
using MusicCollectionTest.Models;

namespace MusicCollectionTest.ViewModels
{
    public sealed class MusicCollectionViewModel : ObservableObject, IDisposable
    {
        private readonly List<AlbumItemViewModel> _allItems;
        private readonly List<AlbumItemViewModel> _filteredItems = new List<AlbumItemViewModel>();

        public IReadOnlyList<AlbumItemViewModel> FilteredItems => _filteredItems;

        public event Action FilteredItemsChanged;
        public event Action<AlbumItemViewModel> ItemFavoriteChanged;

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

        private AlbumItemViewModel _selected;
        public AlbumItemViewModel Selected
        {
            get => _selected;
            set => SetField(ref _selected, value);
        }

        public MusicCollectionViewModel(IEnumerable<Album> models)
        {
            if (models == null)
            {
                throw new ArgumentNullException(nameof(models));
            }
            _allItems = models.Select(m => new AlbumItemViewModel(m)).ToList();
            foreach (var item in _allItems)
            {
                item.PropertyChanged += OnItemPropertyChanged;
            }
            ApplyFilter();
        }

        public void ToggleFavorite(AlbumItemViewModel item)
        {
            if (item == null) return;
            item.IsFavorite = !item.IsFavorite;
        }

        private void OnItemPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(AlbumItemViewModel.IsFavorite)) return;
            var item = (AlbumItemViewModel)sender;
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

        private static bool MatchesQuery(AlbumItemViewModel item, string query)
        {
            if (item.Title.ToLowerInvariant().Contains(query)) return true;
            if (item.Artist.ToLowerInvariant().Contains(query)) return true;
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
