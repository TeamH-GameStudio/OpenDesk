using MusicCollectionTest.Common;
using MusicCollectionTest.Models;

namespace MusicCollectionTest.ViewModels
{
    public sealed class AlbumItemViewModel : ObservableObject
    {
        public Album Model { get; }

        private bool _isFavorite;
        public bool IsFavorite
        {
            get => _isFavorite;
            set => SetField(ref _isFavorite, value);
        }

        public int Id => Model.Id;
        public string Title => Model.Title;
        public string Artist => Model.Artist;
        public int Year => Model.Year;

        public AlbumItemViewModel(Album model)
        {
            Model = model;
        }
    }
}
