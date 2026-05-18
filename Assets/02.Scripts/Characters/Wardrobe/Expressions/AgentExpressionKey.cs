namespace OpenDesk.Characters.Wardrobe.Expressions
{
    // Discrete, named facial expressions an eye option can swap to at runtime.
    // Authored as an enum (not a free-form string) so the inspector lists every
    // slot a catalogue author should fill, and so SetEyeExpression() callers
    // can't typo a key.
    //
    // Default = the neutral/idle look. Every EyeExpressionSetSO MUST provide
    // a texture for Default — it's the one applied when WardrobeApplier first
    // resolves the eye option, before any emotion signal arrives.
    public enum AgentExpressionKey
    {
        Default = 0,
        Happy,
        Sad,
        Angry,
        Surprised,
        Closed,
        Wink,
        Focused,
        Confused,
        // Talking = 9 — 채팅 응답 중(ChatDelta) / Typing 상태에서 사용. EyeExpressionSetSO 의 Entries 에
        // 새 key 로 추가해 텍스처 authoring 필요. 미authoring 시 ResolveExpressionTexture 가 _texture 폴백.
        Talking,
    }
}
