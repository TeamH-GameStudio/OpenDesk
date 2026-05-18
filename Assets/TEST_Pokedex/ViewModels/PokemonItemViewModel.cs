using TestPokedex.Common;
using TestPokedex.Models;

namespace TestPokedex.ViewModels
{
    public sealed class PokemonItemViewModel : ObservableObject
    {
        public Pokemon Model { get; }

        private bool _isFavorite;
        public bool IsFavorite
        {
            get => _isFavorite;
            set => SetField(ref _isFavorite, value);
        }

        public int Id => Model.Id;
        public string Name => Model.Name;
        public string Type1 => Model.Type1;
        public string Type2 => Model.Type2;
        public bool HasSecondType => Model.HasSecondType;
        public string DisplayNumber => $"#{Model.Id:000}";
        public int Hp => Model.Hp;
        public int Attack => Model.Attack;
        public int Defense => Model.Defense;
        public int Speed => Model.Speed;
        public string Description => Model.Description;

        public PokemonItemViewModel(Pokemon model)
        {
            Model = model;
        }
    }
}
