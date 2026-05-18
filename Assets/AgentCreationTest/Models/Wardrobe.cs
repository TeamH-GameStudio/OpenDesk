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

    // Immutable wardrobe selection. Each field is an index into the catalog's
    // options for that slot (0..N-1), with a special value of -1 meaning
    // "none" (no item equipped). Eyes/Mouth never use -1; the UI doesn't
    // expose it for those slots. The `None` constant is exported for clarity
    // at call sites that explicitly want to clear a slot.
    public sealed class Wardrobe
    {
        public const int None = -1;

        public int Skin { get; }
        public int Hair { get; }
        public int Eyes { get; }
        public int Mouth { get; }
        public int Top { get; }
        public int Bottom { get; }
        public int Shoes { get; }

        // 머리 색상 — "#RRGGBB" / "#RRGGBBAA" hex. null/empty 면 "사용자가 색 안 골랐음" → office 스폰 시 applier 의 default 사용.
        // UnityEngine.Color 가 아닌 string 으로 보관해 모델 레이어에 Unity 의존성을 끌어들이지 않는다.
        // View(AgentPreviewActionRail) 가 Color → "#RRGGBBAA" 변환, AgentRosterBootstrapper 가 역변환.
        public string HairColor { get; }

        public Wardrobe(int skin = 0, int hair = 0, int eyes = 0, int mouth = 0, int top = 0, int bottom = 0, int shoes = 0, string hairColor = null)
        {
            Skin = Clamp(skin);
            Hair = Clamp(hair);
            Eyes = Clamp(eyes);
            Mouth = Clamp(mouth);
            Top = Clamp(top);
            Bottom = Clamp(bottom);
            Shoes = Clamp(shoes);
            HairColor = hairColor;
        }

        public static Wardrobe Default => new Wardrobe();

        public Wardrobe With(WardrobePart part, int value)
        {
            switch (part)
            {
                case WardrobePart.Skin:   return new Wardrobe(value, Hair, Eyes, Mouth, Top, Bottom, Shoes, HairColor);
                case WardrobePart.Hair:   return new Wardrobe(Skin, value, Eyes, Mouth, Top, Bottom, Shoes, HairColor);
                case WardrobePart.Eyes:   return new Wardrobe(Skin, Hair, value, Mouth, Top, Bottom, Shoes, HairColor);
                case WardrobePart.Mouth:  return new Wardrobe(Skin, Hair, Eyes, value, Top, Bottom, Shoes, HairColor);
                case WardrobePart.Top:    return new Wardrobe(Skin, Hair, Eyes, Mouth, value, Bottom, Shoes, HairColor);
                case WardrobePart.Bottom: return new Wardrobe(Skin, Hair, Eyes, Mouth, Top, value, Shoes, HairColor);
                case WardrobePart.Shoes:  return new Wardrobe(Skin, Hair, Eyes, Mouth, Top, Bottom, value, HairColor);
                default: return this;
            }
        }

        public Wardrobe WithHairColor(string hairColor)
            => new Wardrobe(Skin, Hair, Eyes, Mouth, Top, Bottom, Shoes, hairColor);

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
            // -1 ("none") is allowed and preserved; only clamp the upper end.
            if (value < None) return None;
            if (value >= AgentPalette.OptionCount) return AgentPalette.OptionCount - 1;
            return value;
        }
    }
}
