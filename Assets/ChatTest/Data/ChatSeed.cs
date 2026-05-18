using System.Collections.Generic;
using ChatTest.Models;

namespace ChatTest.Data
{
    public static class ChatSeed
    {
        public static IReadOnlyList<ChatMessage> GetInitialConversation()
        {
            return new List<ChatMessage>
            {
                new ChatMessage(1, ChatSender.User,
                    "뉴스레터 초안 잡고 있는데, 톤은 차분하게."),
                new ChatMessage(2, ChatSender.Assistant,
                    "뉴스레터 초안 잘 잡아드릴게요. 이번 주에 다룰 주제 몇 가지 알려주세요."),
                new ChatMessage(3, ChatSender.User,
                    "주제는 세 개야.\n1) 작은 팀의 AI 도입기\n2) 사무실 인테리어 리포트\n3) 이번 달 읽은 책"),
                new ChatMessage(4, ChatSender.Assistant,
                    "좋아요. 세 꼭지 모두 짧게 살리면서, 차분한 어조로 다음 흐름을 추천해요.\n\n"
                    + "오프닝은 책 이야기로 부드럽게 열고, 본문에서 AI 도입기를 길게 다룬 뒤\n"
                    + "사무실 사진은 마지막 큐레이션처럼")
            };
        }

        public static IReadOnlyList<string> GetMockReplies()
        {
            return new List<string>
            {
                "네, 그 흐름 좋네요. 첫 문단을 한 번 잡아볼게요.",
                "차분한 톤으로 다듬어보겠습니다. 잠시만요.",
                "흥미로운 포인트네요. 이 부분은 조금 더 풀어쓰면 어떨까요?",
                "정리해보면 이렇게 되겠네요. 한 단락씩 펴봅시다."
            };
        }
    }
}
