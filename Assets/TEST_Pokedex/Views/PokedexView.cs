using System.Collections.Generic;
using System.ComponentModel;
using TestPokedex.Data;
using TestPokedex.ViewModels;
using UnityEngine;
using UnityEngine.UIElements;

namespace TestPokedex.Views
{
    [RequireComponent(typeof(UIDocument))]
    public sealed class PokedexView : MonoBehaviour
    {
        private const string DetailVisibleClass = "detail-panel--visible";
        private const string FavoriteActiveClass = "favorite-icon--active";
        private const string FavoritePulsingClass = "favorite-icon--pulsing";
        private const string FavoriteButtonActiveClass = "favorite-button--active";
        private const long PulseDurationMs = 280L;
        private const float ListItemHeight = 56f;

        private static readonly Dictionary<string, string> TypeColorClassByType = new Dictionary<string, string>
        {
            { "Normal",   "type-normal" },
            { "Fire",     "type-fire" },
            { "Water",    "type-water" },
            { "Grass",    "type-grass" },
            { "Electric", "type-electric" },
            { "Ice",      "type-ice" },
            { "Fighting", "type-fighting" },
            { "Poison",   "type-poison" },
            { "Ground",   "type-ground" },
            { "Flying",   "type-flying" },
            { "Psychic",  "type-psychic" },
            { "Bug",      "type-bug" },
            { "Rock",     "type-rock" },
            { "Ghost",    "type-ghost" },
            { "Dragon",   "type-dragon" },
            { "Dark",     "type-dark" },
            { "Steel",    "type-steel" },
            { "Fairy",    "type-fairy" }
        };

        [SerializeField] private VisualTreeAsset _itemTemplate;

        private UIDocument _document;
        private PokedexViewModel _viewModel;

        private ListView _listView;
        private TextField _searchField;
        private Toggle _favoritesToggle;
        private VisualElement _detailPanel;
        private VisualElement _detailEmpty;
        private VisualElement _detailContent;
        private Label _detailNumber;
        private Label _detailName;
        private Label _detailType1;
        private Label _detailType2;
        private Label _detailHp;
        private Label _detailAttack;
        private Label _detailDefense;
        private Label _detailSpeed;
        private Label _detailDescription;
        private Button _detailFavoriteButton;

        private bool _suppressSelectionFeedback;

        private void OnEnable()
        {
            _document = GetComponent<UIDocument>();
            if (_document == null || _document.rootVisualElement == null)
            {
                Debug.LogError("[PokedexView] UIDocument or rootVisualElement is null. Assign a UXML to the UIDocument.");
                return;
            }

            var root = _document.rootVisualElement;
            CacheElements(root);

            var pokemons = PokemonRepository.GetAll();
            _viewModel = new PokedexViewModel(pokemons);

            ConfigureListView();
            ConfigureDetailPanel();
            RegisterCallbacks();

            ApplyDetailPanelVisibility();
        }

        private void OnDisable()
        {
            UnregisterCallbacks();
            if (_viewModel != null)
            {
                _viewModel.Dispose();
                _viewModel = null;
            }
        }

        private void CacheElements(VisualElement root)
        {
            _listView = root.Q<ListView>("pokemon-list");
            _searchField = root.Q<TextField>("search-field");
            _favoritesToggle = root.Q<Toggle>("favorites-toggle");
            _detailPanel = root.Q<VisualElement>("detail-panel");
            _detailEmpty = root.Q<VisualElement>("detail-empty");
            _detailContent = root.Q<VisualElement>("detail-content");
            _detailNumber = root.Q<Label>("detail-number");
            _detailName = root.Q<Label>("detail-name");
            _detailType1 = root.Q<Label>("detail-type1");
            _detailType2 = root.Q<Label>("detail-type2");
            _detailHp = root.Q<Label>("detail-hp");
            _detailAttack = root.Q<Label>("detail-attack");
            _detailDefense = root.Q<Label>("detail-defense");
            _detailSpeed = root.Q<Label>("detail-speed");
            _detailDescription = root.Q<Label>("detail-description");
            _detailFavoriteButton = root.Q<Button>("detail-favorite-button");
        }

        private void ConfigureListView()
        {
            _listView.fixedItemHeight = ListItemHeight;
            _listView.virtualizationMethod = CollectionVirtualizationMethod.FixedHeight;
            _listView.selectionType = SelectionType.Single;
            _listView.makeItem = MakeListItem;
            _listView.bindItem = BindListItem;
            _listView.unbindItem = UnbindListItem;
            _listView.itemsSource = (System.Collections.IList)_viewModel.FilteredItems;
            _listView.RefreshItems();
        }

        private void ConfigureDetailPanel()
        {
            _searchField.value = string.Empty;
            _favoritesToggle.value = false;
            _detailPanel.RemoveFromClassList(DetailVisibleClass);
        }

        private void RegisterCallbacks()
        {
            _searchField.RegisterValueChangedCallback(OnSearchChanged);
            _favoritesToggle.RegisterValueChangedCallback(OnFavoritesToggleChanged);
            _detailFavoriteButton.clicked += OnDetailFavoriteClicked;
            _listView.selectionChanged += OnListSelectionChanged;
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            _viewModel.FilteredItemsChanged += OnFilteredItemsChanged;
            _viewModel.ItemFavoriteChanged += OnItemFavoriteChanged;
        }

        private void UnregisterCallbacks()
        {
            if (_searchField != null) _searchField.UnregisterValueChangedCallback(OnSearchChanged);
            if (_favoritesToggle != null) _favoritesToggle.UnregisterValueChangedCallback(OnFavoritesToggleChanged);
            if (_detailFavoriteButton != null) _detailFavoriteButton.clicked -= OnDetailFavoriteClicked;
            if (_listView != null) _listView.selectionChanged -= OnListSelectionChanged;
            if (_viewModel != null)
            {
                _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
                _viewModel.FilteredItemsChanged -= OnFilteredItemsChanged;
                _viewModel.ItemFavoriteChanged -= OnItemFavoriteChanged;
            }
        }

        private VisualElement MakeListItem()
        {
            if (_itemTemplate != null)
            {
                return _itemTemplate.CloneTree();
            }
            return BuildListItemTemplate();
        }

        private static VisualElement BuildListItemTemplate()
        {
            var row = new VisualElement { name = "item-root" };
            row.AddToClassList("pokemon-item");

            var number = new Label { name = "item-number" };
            number.AddToClassList("item-number");
            row.Add(number);

            var nameLabel = new Label { name = "item-name" };
            nameLabel.AddToClassList("item-name");
            row.Add(nameLabel);

            var spacer = new VisualElement();
            spacer.AddToClassList("item-spacer");
            row.Add(spacer);

            var typesContainer = new VisualElement { name = "item-types" };
            typesContainer.AddToClassList("item-types");

            var type1 = new Label { name = "item-type1" };
            type1.AddToClassList("type-pill");
            type1.AddToClassList("type-pill--small");
            typesContainer.Add(type1);

            var type2 = new Label { name = "item-type2" };
            type2.AddToClassList("type-pill");
            type2.AddToClassList("type-pill--small");
            typesContainer.Add(type2);

            row.Add(typesContainer);

            var favorite = new Label { name = "item-favorite", text = "★" };
            favorite.AddToClassList("favorite-icon");
            row.Add(favorite);

            return row;
        }

        private void BindListItem(VisualElement element, int index)
        {
            if (_viewModel == null) return;
            if (index < 0 || index >= _viewModel.FilteredItems.Count) return;

            var item = _viewModel.FilteredItems[index];

            var number = element.Q<Label>("item-number");
            var nameLabel = element.Q<Label>("item-name");
            var type1 = element.Q<Label>("item-type1");
            var type2 = element.Q<Label>("item-type2");
            var favorite = element.Q<Label>("item-favorite");

            if (number != null) number.text = item.DisplayNumber;
            if (nameLabel != null) nameLabel.text = item.Name;

            ApplyTypePill(type1, item.Type1, true);
            ApplyTypePill(type2, item.Type2, item.HasSecondType);

            if (favorite != null)
            {
                favorite.EnableInClassList(FavoriteActiveClass, item.IsFavorite);
                favorite.RemoveFromClassList(FavoritePulsingClass);
            }

            element.userData = item;
        }

        private void UnbindListItem(VisualElement element, int index)
        {
            element.userData = null;
            var favorite = element.Q<Label>("item-favorite");
            if (favorite != null)
            {
                favorite.RemoveFromClassList(FavoritePulsingClass);
            }
        }

        private static void ApplyTypePill(Label label, string typeName, bool visible)
        {
            if (label == null) return;
            if (!visible)
            {
                label.style.display = DisplayStyle.None;
                return;
            }
            label.style.display = DisplayStyle.Flex;
            label.text = typeName;
            ApplyTypeColorClass(label, typeName);
        }

        private static void ApplyTypeColorClass(Label label, string typeName)
        {
            foreach (var entry in TypeColorClassByType)
            {
                label.RemoveFromClassList(entry.Value);
            }
            if (TypeColorClassByType.TryGetValue(typeName, out var className))
            {
                label.AddToClassList(className);
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

        private void OnDetailFavoriteClicked()
        {
            if (_viewModel == null) return;
            var selected = _viewModel.Selected;
            if (selected == null) return;
            _viewModel.ToggleFavorite(selected);
            PulseDetailFavoriteButton();
        }

        private void OnListSelectionChanged(IEnumerable<object> selection)
        {
            if (_viewModel == null || _suppressSelectionFeedback) return;
            PokemonItemViewModel newSelection = null;
            foreach (var obj in selection)
            {
                newSelection = obj as PokemonItemViewModel;
                break;
            }
            _viewModel.Selected = newSelection;
        }

        private void OnViewModelPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(PokedexViewModel.Selected))
            {
                ApplyDetailPanelVisibility();
                SyncListSelection();
            }
        }

        private void OnFilteredItemsChanged()
        {
            if (_listView == null || _viewModel == null) return;
            _listView.RefreshItems();
            SyncListSelection();
        }

        private void OnItemFavoriteChanged(PokemonItemViewModel item)
        {
            if (_listView == null || _viewModel == null) return;
            var idx = -1;
            for (var i = 0; i < _viewModel.FilteredItems.Count; i++)
            {
                if (ReferenceEquals(_viewModel.FilteredItems[i], item))
                {
                    idx = i;
                    break;
                }
            }

            if (idx >= 0)
            {
                _listView.RefreshItem(idx);
                PulseListItemFavorite(idx, item.IsFavorite);
            }

            if (ReferenceEquals(_viewModel.Selected, item))
            {
                UpdateDetailFavoriteButton(item);
            }
        }

        private void PulseListItemFavorite(int index, bool isFavorite)
        {
            if (!isFavorite) return;
            var element = _listView.GetRootElementForIndex(index);
            if (element == null) return;
            var favorite = element.Q<Label>("item-favorite");
            if (favorite == null) return;
            favorite.AddToClassList(FavoritePulsingClass);
            favorite.schedule.Execute(() => favorite.RemoveFromClassList(FavoritePulsingClass)).StartingIn(PulseDurationMs);
        }

        private void PulseDetailFavoriteButton()
        {
            if (_detailFavoriteButton == null) return;
            _detailFavoriteButton.AddToClassList(FavoritePulsingClass);
            _detailFavoriteButton.schedule.Execute(() => _detailFavoriteButton.RemoveFromClassList(FavoritePulsingClass)).StartingIn(PulseDurationMs);
        }

        private void ApplyDetailPanelVisibility()
        {
            if (_viewModel == null) return;
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

        private void UpdateDetailContent(PokemonItemViewModel item)
        {
            if (item == null) return;

            if (_detailNumber != null) _detailNumber.text = item.DisplayNumber;
            if (_detailName != null) _detailName.text = item.Name;

            ApplyTypePill(_detailType1, item.Type1, true);
            ApplyTypePill(_detailType2, item.Type2, item.HasSecondType);

            if (_detailHp != null) _detailHp.text = item.Hp.ToString();
            if (_detailAttack != null) _detailAttack.text = item.Attack.ToString();
            if (_detailDefense != null) _detailDefense.text = item.Defense.ToString();
            if (_detailSpeed != null) _detailSpeed.text = item.Speed.ToString();
            if (_detailDescription != null) _detailDescription.text = item.Description;

            UpdateDetailFavoriteButton(item);
        }

        private void UpdateDetailFavoriteButton(PokemonItemViewModel item)
        {
            if (_detailFavoriteButton == null) return;
            _detailFavoriteButton.text = item.IsFavorite ? "★ Favorited" : "☆ Add Favorite";
            _detailFavoriteButton.EnableInClassList(FavoriteButtonActiveClass, item.IsFavorite);
        }

        private void SyncListSelection()
        {
            if (_listView == null || _viewModel == null) return;
            var selected = _viewModel.Selected;
            if (selected == null)
            {
                _suppressSelectionFeedback = true;
                _listView.ClearSelection();
                _suppressSelectionFeedback = false;
                return;
            }

            var idx = -1;
            for (var i = 0; i < _viewModel.FilteredItems.Count; i++)
            {
                if (ReferenceEquals(_viewModel.FilteredItems[i], selected))
                {
                    idx = i;
                    break;
                }
            }

            if (idx < 0)
            {
                _suppressSelectionFeedback = true;
                _listView.ClearSelection();
                _suppressSelectionFeedback = false;
                return;
            }

            _suppressSelectionFeedback = true;
            _listView.SetSelectionWithoutNotify(new[] { idx });
            _listView.ScrollToItem(idx);
            _suppressSelectionFeedback = false;
        }
    }
}
