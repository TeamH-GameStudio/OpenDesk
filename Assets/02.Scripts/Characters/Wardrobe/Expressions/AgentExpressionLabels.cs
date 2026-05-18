namespace OpenDesk.Characters.Wardrobe.Expressions
{
    // Korean short-form labels for the expression rail pills (≤2 글자 권장,
    // 64px 컬럼 안에 들어가야 함). Centralised here so the rail UI never
    // hardcodes a per-key string and authoring stays in one place.
    public static class AgentExpressionLabels
    {
        public static string ToKorean(AgentExpressionKey key)
        {
            switch (key)
            {
                case AgentExpressionKey.Default:   return "기본";
                case AgentExpressionKey.Happy:     return "기쁨";
                case AgentExpressionKey.Sad:       return "슬픔";
                case AgentExpressionKey.Angry:     return "화남";
                case AgentExpressionKey.Surprised: return "놀람";
                case AgentExpressionKey.Closed:    return "감음";
                case AgentExpressionKey.Wink:      return "윙크";
                case AgentExpressionKey.Focused:   return "집중";
                case AgentExpressionKey.Confused:  return "혼란";
                case AgentExpressionKey.Talking:   return "대화";
                default:                           return key.ToString();
            }
        }
    }
}
