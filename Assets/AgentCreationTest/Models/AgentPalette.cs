namespace AgentCreationTest.Models
{
    // Centralised palette mirroring opendesk-onboarding.jsx AgentPreview definitions.
    // Each part has 9 options (index 0–8); UI indices map directly into these arrays.
    public static class AgentPalette
    {
        public const int OptionCount = 9;

        public static readonly string[] SkinColors =
        {
            "#F4DBC0", "#EAC9A2", "#D9B080", "#C49263", "#A87248",
            "#7E5535", "#5E3F26", "#FAEAD6", "#E0BFA0",
        };

        public static readonly string[] HairColors =
        {
            "#6B4A2E", "#3A2E26", "#2A201A", "#8C6B3F", "#A8835A",
            "#5C4530", "#1A1410", "#7A5A3A", "#9B7A55",
        };

        // Hair shape names used by USS modifier classes: agent-hair--<name>
        public static readonly string[] HairShapes =
        {
            "rounded", "spiky", "bowl", "side", "wavy",
            "bun", "short", "long", "split",
        };

        public static readonly string[] EyeStyles =
        {
            "dot", "line", "curve", "wide", "wink",
            "closed", "sparkle", "tired", "focused",
        };

        public static readonly string[] MouthStyles =
        {
            "smile", "open", "flat", "smirk", "o",
            "grin", "small", "frown", "tongue",
        };

        public static readonly string[] TopColors =
        {
            "#A8835A", "#5C7A8C", "#6B7F58", "#9B6B5A", "#4A5568",
            "#B89668", "#7A6B8C", "#5A7B6B", "#8B5A4A",
        };

        public static readonly string[] BottomColors =
        {
            "#5C4A38", "#3A4452", "#3F4838", "#4A3F36", "#2D3748",
            "#5C4A38", "#3F3548", "#3A4D40", "#4D3530",
        };

        public static readonly string[] ShoesColors =
        {
            "#3A2A20", "#2A201A", "#5C4530", "#1A1410", "#3F2F22",
            "#2A2520", "#4A3525", "#1F1812", "#3A2820",
        };
    }
}
