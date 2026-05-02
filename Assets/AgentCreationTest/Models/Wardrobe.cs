namespace AgentCreationTest.Models
{
    public enum WardrobePart
    {
        Skin,
        Hair,
        Eyes,
        Mouth,
        Top,
        Bottom,
        Shoes,
    }

    // Immutable wardrobe selection. Each field is an index into AgentPalette arrays (0..8).
    public sealed class Wardrobe
    {
        public int Skin { get; }
        public int Hair { get; }
        public int Eyes { get; }
        public int Mouth { get; }
        public int Top { get; }
        public int Bottom { get; }
        public int Shoes { get; }

        public Wardrobe(int skin = 0, int hair = 0, int eyes = 0, int mouth = 0, int top = 0, int bottom = 0, int shoes = 0)
        {
            Skin = Clamp(skin);
            Hair = Clamp(hair);
            Eyes = Clamp(eyes);
            Mouth = Clamp(mouth);
            Top = Clamp(top);
            Bottom = Clamp(bottom);
            Shoes = Clamp(shoes);
        }

        public static Wardrobe Default => new Wardrobe();

        public Wardrobe With(WardrobePart part, int value)
        {
            switch (part)
            {
                case WardrobePart.Skin:   return new Wardrobe(value, Hair, Eyes, Mouth, Top, Bottom, Shoes);
                case WardrobePart.Hair:   return new Wardrobe(Skin, value, Eyes, Mouth, Top, Bottom, Shoes);
                case WardrobePart.Eyes:   return new Wardrobe(Skin, Hair, value, Mouth, Top, Bottom, Shoes);
                case WardrobePart.Mouth:  return new Wardrobe(Skin, Hair, Eyes, value, Top, Bottom, Shoes);
                case WardrobePart.Top:    return new Wardrobe(Skin, Hair, Eyes, Mouth, value, Bottom, Shoes);
                case WardrobePart.Bottom: return new Wardrobe(Skin, Hair, Eyes, Mouth, Top, value, Shoes);
                case WardrobePart.Shoes:  return new Wardrobe(Skin, Hair, Eyes, Mouth, Top, Bottom, value);
                default: return this;
            }
        }

        public int Get(WardrobePart part)
        {
            switch (part)
            {
                case WardrobePart.Skin:   return Skin;
                case WardrobePart.Hair:   return Hair;
                case WardrobePart.Eyes:   return Eyes;
                case WardrobePart.Mouth:  return Mouth;
                case WardrobePart.Top:    return Top;
                case WardrobePart.Bottom: return Bottom;
                case WardrobePart.Shoes:  return Shoes;
                default: return 0;
            }
        }

        private static int Clamp(int value)
        {
            if (value < 0) return 0;
            if (value >= AgentPalette.OptionCount) return AgentPalette.OptionCount - 1;
            return value;
        }
    }
}
