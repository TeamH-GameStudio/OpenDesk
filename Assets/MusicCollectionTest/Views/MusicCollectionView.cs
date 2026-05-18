using System.Collections.Generic;
using System.ComponentModel;
using MusicCollectionTest.Data;
using MusicCollectionTest.ViewModels;
using UnityEngine;
using UnityEngine.UIElements;

namespace MusicCollectionTest.Views
{
    [RequireComponent(typeof(UIDocument))]
    public sealed class MusicCollectionView : MonoBehaviour
    {
        private const string DetailVisibleClass = "detail-panel--visible";
        private const string FavoriteActiveClass = "favorite-icon--active";
        private const string FavoritePulsingClass = "favorite-icon--pulsing";
        private const string CardSelectedClass = "album-card--selected";
        private const long PulseDurationMs = 280L;

        private UIDocument _document;
        private MusicCollectionViewModel _viewModel;

        private TextField _searchField;
        private Toggle _favoritesToggle;
        private ScrollView _albumScroll;
        private VisualElement _albumGrid;
        private VisualElement _detailPanel;
        private VisualElement _detailEmpty;
        private VisualElement _detailContent;
        private Label _detailTitle;
        private Label _detailArtist;
        private Label _detailYear;
        private VisualElement _detailCover;
        private Label _detailCoverInitial;
        private Button _detailCloseButton;

        private readonly Dictionary<AlbumItemViewModel, VisualElement> _cardByItem = new Dictionary<AlbumItemViewModel, VisualElement>();

        private void OnEnable()
        {
            _document = GetComponent<UIDocument>();
            if (_document == null || _document.rootVisualElement == null)
            {
                Debug.LogError("[MusicCollectionView] UIDocument or rootVisualElement is null. Assign a UXML to the UIDocument.");
                return;
            }

            var root = _document.rootVisualElement;
            CacheElements(root);

            _viewModel = new MusicCollectionViewModel(AlbumRepository.GetAll());

            ResetInputs();
            RebuildGrid();
            RegisterCallbacks();
            ApplyDetailPanelVisibility();
        }

        private void OnDisable()
        {
            UnregisterCallbacks();
            ClearGrid();
            if (_viewModel != null)
            {
                _viewModel.Dispose();
                _viewModel = null;
            }
        }

        private void CacheElements(VisualElement root)
        {
            _searchField = root.Q<TextField>("search-field");
            _favoritesToggle = root.Q<Toggle>("favorites-toggle");
            _albumScroll = root.Q<ScrollView>("album-scroll");
            _albumGrid = root.Q<VisualElement>("album-grid");
            _detailPanel = root.Q<VisualElement>("detail-panel");
            _detailEmpty = root.Q<VisualElement>("detail-empty");
            _detailContent = root.Q<VisualElement>("detail-content");
            _detailTitle = root.Q<Label>("detail-title");
            _detailArtist = root.Q<Label>("detail-artist");
            _detailYear = root.Q<Label>("detail-year");
            _detailCover = root.Q<VisualElement>("detail-cover");
            _detailCoverInitial = root.Q<Label>("detail-cover-initial");
            _detailCloseButton = root.Q<Button>("detail-close-button");
        }

        private void ResetInputs()
        {
            if (_searchField != null) _searchField.value = string.Empty;
            if (_favoritesToggle != null) _favoritesToggle.value = false;
            if (_detailPanel != null) _detailPanel.RemoveFromClassList(DetailVisibleClass);
        }

        private void RegisterCallbacks()
        {
            _searchField.RegisterValueChangedCallback(OnSearchChanged);
            _favoritesToggle.RegisterValueChangedCallback(OnFavoritesToggleChanged);
            if (_detailCloseButton != null) _detailCloseButton.clicked += OnDetailCloseClicked;
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            _viewModel.FilteredItemsChanged += OnFilteredItemsChanged;
            _viewModel.ItemFavoriteChanged += OnItemFavoriteChanged;
        }

        private void UnregisterCallbacks()
        {
            if (_searchField != null) _searchField.UnregisterValueChangedCallback(OnSearchChanged);
            if (_favoritesToggle != null) _favoritesToggle.UnregisterValueChangedCallback(OnFavoritesToggleChanged);
            if (_detailCloseButton != null) _detailCloseButton.clicked -= OnDetailCloseClicked;
            if (_viewModel != null)
            {
                _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
                _viewModel.FilteredItemsChanged -= OnFilteredItemsChanged;
                _viewModel.ItemFavoriteChanged -= OnItemFavoriteChanged;
            }
        }

        private void OnSearchChanged(ChangeEvent<string> evt)
        {
            if (_viewModel == null) return;
            _viewModel.SearchQuery = evt.newValue ?? string.Empty;
        }

        private void OnFavoritesToggleChanged(ChangeEvent<bool> evt)
        {
            if (_viewModel == null) return;
            _viewModel.FavoritesOnly = evt.newValue;
        }

        private void OnDetailCloseClicked()
        {
            if (_viewModel == null) return;
            _viewModel.Selected = null;
        }

        private void OnViewModelPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MusicCollectionViewModel.Selected))
            {
                ApplyDetailPanelVisibility();
                ApplySelectionHighlight();
            }
        }

        private void OnFilteredItemsChanged()
        {
            RebuildGrid();
        }

        private void OnItemFavoriteChanged(AlbumItemViewModel item)
        {
            if (_cardByItem.TryGetValue(item, out var card))
            {
                var heart = card.Q<Label>("card-heart");
                if (heart != null)
                {
                    heart.EnableInClassList(FavoriteActiveClass, item.IsFavorite);
                    if (item.IsFavorite)
                    {
                        PulseHeart(heart);
                    }
                }
            }
        }

        private void RebuildGrid()
        {
            if (_albumGrid == null || _viewModel == null) return;

            ClearGrid();

            for (var i = 0; i < _viewModel.FilteredItems.Count; i++)
            {
                var item = _viewModel.FilteredItems[i];
                var card = BuildAlbumCard(item);
                _albumGrid.Add(card);
                _cardByItem[item] = card;
            }

            ApplySelectionHighlight();
        }

        private void ClearGrid()
        {
            if (_albumGrid != null)
            {
                _albumGrid.Clear();
            }
            _cardByItem.Clear();
        }

        private VisualElement BuildAlbumCard(AlbumItemViewModel item)
        {
            var card = new VisualElement { name = $"album-card-{item.Id}" };
            card.AddToClassList("album-card");

            var coverWrapper = new VisualElement();
            coverWrapper.AddToClassList("album-cover-wrapper");

            var cover = new VisualElement();
            cover.AddToClassList("album-cover");
            cover.style.backgroundColor = new StyleColor(GetAlbumColor(item.Id));
            coverWrapper.Add(cover);

            var sheen = new VisualElement();
            sheen.AddToClassList("album-cover-sheen");
            cover.Add(sheen);

            var initial = new Label(GetInitial(item.Title));
            initial.AddToClassList("album-cover-initial");
            cover.Add(initial);

            var heart = new Label("♥") { name = "card-heart" };
            heart.AddToClassList("favorite-icon");
            heart.EnableInClassList(FavoriteActiveClass, item.IsFavorite);
            heart.RegisterCallback<ClickEvent>(evt =>
            {
                evt.StopPropagation();
                if (_viewModel == null) return;
                _viewModel.ToggleFavorite(item);
            });
            coverWrapper.Add(heart);

            card.Add(coverWrapper);

            var title = new Label(item.Title);
            title.AddToClassList("album-title");
            card.Add(title);

            var artist = new Label(item.Artist);
            artist.AddToClassList("album-artist");
            card.Add(artist);

            card.RegisterCallback<ClickEvent>(evt =>
            {
                if (_viewModel == null) return;
                _viewModel.Selected = item;
            });

            return card;
        }

        private void PulseHeart(VisualElement heart)
        {
            heart.AddToClassList(FavoritePulsingClass);
            heart.schedule.Execute(() => heart.RemoveFromClassList(FavoritePulsingClass)).StartingIn(PulseDurationMs);
        }

        private void ApplyDetailPanelVisibility()
        {
            if (_viewModel == null || _detailPanel == null) return;
            var item = _viewModel.Selected;
            if (item == null)
            {
                _detailPanel.RemoveFromClassList(DetailVisibleClass);
                if (_detailContent != null) _detailContent.style.display = DisplayStyle.None;
                if (_detailEmpty != null) _detailEmpty.style.display = DisplayStyle.Flex;
                return;
            }

            if (_detailContent != null) _detailContent.style.display = DisplayStyle.Flex;
            if (_detailEmpty != null) _detailEmpty.style.display = DisplayStyle.None;
            _detailPanel.AddToClassList(DetailVisibleClass);
            UpdateDetailContent(item);
        }

        private void UpdateDetailContent(AlbumItemViewModel item)
        {
            if (_detailTitle != null) _detailTitle.text = item.Title;
            if (_detailArtist != null) _detailArtist.text = item.Artist;
            if (_detailYear != null) _detailYear.text = item.Year.ToString();
            if (_detailCover != null) _detailCover.style.backgroundColor = new StyleColor(GetAlbumColor(item.Id));
            if (_detailCoverInitial != null) _detailCoverInitial.text = GetInitial(item.Title);
        }

        private static Color GetAlbumColor(int id)
        {
            const float goldenRatioConjugate = 0.6180339887f;
            var hue = (id * goldenRatioConjugate) % 1f;
            if (hue < 0f) hue += 1f;
            return Color.HSVToRGB(hue, 0.55f, 0.62f);
        }

        private static string GetInitial(string title)
        {
            if (string.IsNullOrEmpty(title)) return "?";
            return title.Substring(0, 1).ToUpperInvariant();
        }

        private void ApplySelectionHighlight()
        {
            if (_viewModel == null) return;
            var selected = _viewModel.Selected;
            foreach (var pair in _cardByItem)
            {
                pair.Value.EnableInClassList(CardSelectedClass, ReferenceEquals(pair.Key, selected));
            }
        }
    }
}
