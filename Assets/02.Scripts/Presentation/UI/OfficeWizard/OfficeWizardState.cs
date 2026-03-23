namespace OpenDesk.Presentation.UI.OfficeWizard
{
    public enum OfficeWizardState
    {
        Hidden,          // 마법사 비표시 (설정 완료 or 재방문)
        Welcome,         // "AI 비서 환경이 준비되었어요!"
        ModelChoice,     // 무료(Ollama) vs API 키 선택
        OllamaSetup,    // Ollama 설치/확인 진행
        ApiKeySetup,     // 프로바이더 선택 + 키 입력
        ChannelSetup,    // 채널 연결 (선택사항)
        TestChat,        // 첫 대화 테스트
        Complete,        // "모든 준비가 끝났어요!"
    }
}
