using UnityEngine;

namespace OpenDesk.Presentation.SceneLoading
{
    /// <summary>
    /// 로딩 화면 하단 팁 텍스트. ProjectH 의 Localization 의존을 제거하고
    /// 한국어 정적 배열로 단순화. 추가/수정은 이 한 곳만 만지면 됨.
    /// NotoSansKR 글리프만 사용 — 이모지/특수문자 금지.
    /// </summary>
    public static class LoadingTips
    {
        private static readonly string[] _tips =
        {
            "에이전트 이름은 짧고 부르기 좋게 지으면 더 자연스러워요.",
            "디스켓을 드래그해서 에이전트에게 새 능력을 장착할 수 있어요.",
            "In-box에 파일을 떨어뜨리면 대화 컨텍스트로 자동 포함돼요.",
            "Out-box 는 응답 결과를 자동으로 정리해 보관해요.",
            "에이전트마다 다른 말투를 부여하면 협업이 더 즐거워져요.",
            "캐릭터를 드래그해서 자유롭게 회전시켜볼 수 있어요.",
            "성격을 너무 많이 고르면 흐릿해져요. 3개 이내가 좋아요.",
            "Ctrl + Enter (Mac 은 Cmd + Enter) 로도 다음 단계로 갈 수 있어요.",
            "옷이 마음에 안 들면 언제든 옷장을 다시 열 수 있어요.",
            "시작이 어려울 땐 '랜덤' 버튼이 좋은 출발점이 돼요.",
        };

        public static string GetRandom()
        {
            if (_tips == null || _tips.Length == 0) return string.Empty;
            int idx = Random.Range(0, _tips.Length);
            return _tips[idx];
        }
    }
}
