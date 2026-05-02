namespace TestPokedex.Models
{
    public sealed class Pokemon
    {
        public int Id { get; }
        public string Name { get; }
        public string Type1 { get; }
        public string Type2 { get; }
        public int Hp { get; }
        public int Attack { get; }
        public int Defense { get; }
        public int Speed { get; }
        public string Description { get; }

        public Pokemon(int id, string name, string type1, string type2, int hp, int attack, int defense, int speed, string description)
        {
            Id = id;
            Name = name ?? string.Empty;
            Type1 = type1 ?? string.Empty;
            Type2 = type2 ?? string.Empty;
            Hp = hp;
            Attack = attack;
            Defense = defense;
            Speed = speed;
            Description = description ?? string.Empty;
        }

        public bool HasSecondType => !string.IsNullOrEmpty(Type2);
    }
}
