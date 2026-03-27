using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OpenDesk.Claude
{
    /// <summary>
    /// Claude 채팅 UI 관리 — TestChattingScene 전용.
    /// 메시지 버블 생성, 스트리밍 텍스트 업데이트, 상태 표시.
    /// </summary>
    public class ClaudeChatManager : MonoBehaviour
    {
        [Header("WebSocket 클라이언트")]
        [SerializeField] private ClaudeWebSocketClient _client;

        [Header("채팅 영역")]
        [SerializeField] private ScrollRect    _scrollRect;
        [SerializeField] private RectTransform _chatContent;

        [Header("메시지 프리팹")]
        [SerializeField] private GameObject _userBubblePrefab;
        [SerializeField] private GameObject _aiBubblePrefab;
        [SerializeField] private GameObject _systemBubblePrefab;

        [Header("입력")]
        [SerializeField] private TMP_InputField _inputField;
        [SerializeField] private Button         _sendButton;
        [SerializeField] private Button         _clearButton;

        [Header("상태 표시")]
        [SerializeField] private Image      _statusDot;
        [SerializeField] private TMP_Text   _statusText;          // "연결됨" / "연결 끊김"
        [SerializeField] private TMP_Text   _modelText;
        [SerializeField] private TMP_Text   _typingIndicator;     // "대기 중" / "응답 중..." 등

        [Header("설정")]
        [SerializeField] private int _maxBubbles = 100;

        // ── 내부 상태 ─────────────────────────────────────────

        private readonly List<GameObject> _activeBubbles = new();
        private GameObject _currentStreamingBubble;
        private TMP_Text   _currentStreamingTMP;
        private string     _streamingText = "";
        private bool       _isStreaming;

        // ── 초기화 ───────────────────────────────────────────

        private void Start()
        {
            // 이벤트 구독
            if (_client != null)
            {
                _client.OnDelta             += HandleDelta;
                _client.OnFinal             += HandleFinal;
                _client.OnError             += HandleError;
                _client.OnConnectionChanged += HandleConnectionChanged;
                _client.OnCleared           += HandleCleared;
                _client.OnStatus            += HandleStatus;
            }

            // 버튼 리스너
            if (_sendButton != null)
                _sendButton.onClick.AddListener(OnSendClicked);

            if (_clearButton != null)
                _clearButton.onClick.AddListener(OnClearClicked);

            // Enter → 전송 (Input System 호환)
            if (_inputField != null)
                _inputField.onSubmit.AddListener(_ => OnSendClicked());

            // 초기 상태
            SetConnectionUI(false, "");
            CreateSystemBubble("서버에 연결 중...");
        }

        private void OnDestroy()
        {
            if (_client != null)
            {
                _client.OnDelta             -= HandleDelta;
                _client.OnFinal             -= HandleFinal;
                _client.OnError             -= HandleError;
                _client.OnConnectionChanged -= HandleConnectionChanged;
                _client.OnCleared           -= HandleCleared;
                _client.OnStatus            -= HandleStatus;
            }
        }

        // ── 전송 ────────────────────────────────────────────

        private void OnSendClicked()
        {
            if (_inputField == null) return;

            var text = _inputField.text.Trim();
            if (string.IsNullOrEmpty(text)) return;

            if (_isStreaming)
            {
                // 이전 응답 대기 중
                return;
            }

            // User 버블 생성
            CreateUserBubble(text);

            // 서버로 전송
            _client?.SendChat(text);

            // 입력란 초기화 + 포커스
            _inputField.text = "";
            _inputField.ActivateInputField();

            // 상태: 응답 대기
            _isStreaming = true;
            SetAgentStatus("응답 대기 중...");
        }

        private void OnClearClicked()
        {
            _client?.SendClear();
            DestroyAllBubbles();
            _currentStreamingBubble = null;
            _currentStreamingTMP    = null;
            _streamingText          = "";
            _isStreaming             = false;
            SetAgentStatus("대기 중");
        }

        // ── 서버 이벤트 핸들러 ──────────────────────────────

        private void HandleDelta(string text)
        {
            SetAgentStatus("응답 중...");

            if (_currentStreamingBubble == null)
            {
                // 첫 delta — 새 AI 버블 생성
                _currentStreamingBubble = CreateBubble(_aiBubblePrefab, text);
                _currentStreamingTMP    = _currentStreamingBubble?.GetComponentInChildren<TMP_Text>();
                _streamingText          = text;
            }
            else
            {
                // 기존 버블에 append
                _streamingText += text;
                if (_currentStreamingTMP != null)
                    _currentStreamingTMP.text = _streamingText;
            }

            AutoScroll();
        }

        private void HandleFinal(string text, float cost)
        {
            if (_currentStreamingBubble != null)
            {
                // 최종 텍스트로 교체 (포매팅 적용됨)
                if (_currentStreamingTMP != null && !string.IsNullOrEmpty(text))
                    _currentStreamingTMP.text = text;
            }
            else if (!string.IsNullOrEmpty(text))
            {
                // delta 없이 final만 온 경우
                CreateAIBubble(text);
            }

            // 스트리밍 종료
            _currentStreamingBubble = null;
            _currentStreamingTMP    = null;
            _streamingText          = "";
            _isStreaming             = false;
            SetAgentStatus("대기 중");

            if (cost > 0f)
                Debug.Log($"[Claude] 비용: ${cost:F4}");

            AutoScroll();
        }

        private void HandleError(string message)
        {
            CreateSystemBubble($"오류: {message}");

            // 스트리밍 중이었으면 중단
            if (_isStreaming)
            {
                if (_currentStreamingBubble != null && _currentStreamingTMP != null)
                {
                    // 마지막 delta 텍스트 유지하고 중단 표시
                    _currentStreamingTMP.text = _streamingText + "\n<color=#F44336><i>(응답 중단됨)</i></color>";
                }

                _currentStreamingBubble = null;
                _currentStreamingTMP    = null;
                _streamingText          = "";
                _isStreaming             = false;
                SetAgentStatus("대기 중");
            }
        }

        private void HandleConnectionChanged(bool connected, string model)
        {
            SetConnectionUI(connected, model);

            if (connected)
                CreateSystemBubble("Claude 채팅 준비 완료");
            else
                CreateSystemBubble("서버 연결 끊김 - 재연결 시도 중...");
        }

        private void HandleStatus(string text)
        {
            SetAgentStatus(text);
        }

        private void HandleCleared()
        {
            CreateSystemBubble("대화가 초기화되었습니다");
        }

        // ── UI 헬퍼 ─────────────────────────────────────────

        private void SetAgentStatus(string text)
        {
            if (_typingIndicator != null)
                _typingIndicator.text = text;
        }

        private void SetConnectionUI(bool connected, string model)
        {
            if (_statusDot != null)
                _statusDot.color = connected
                    ? new Color32(76, 175, 80, 255)    // #4CAF50 초록
                    : new Color32(244, 67, 54, 255);   // #F44336 빨강

            if (_statusText != null)
                _statusText.text = connected ? "연결됨" : "연결 끊김";

            SetAgentStatus(connected ? "대기 중" : "연결 끊김");

            if (_modelText != null)
                _modelText.text = model ?? "";
        }

        // ── 버블 생성 ───────────────────────────────────────

        private void CreateUserBubble(string text)
        {
            CreateBubble(_userBubblePrefab, text);
            AutoScroll();
        }

        private void CreateAIBubble(string text)
        {
            CreateBubble(_aiBubblePrefab, text);
            AutoScroll();
        }

        private void CreateSystemBubble(string text)
        {
            CreateBubble(_systemBubblePrefab, text);
            AutoScroll();
        }

        private GameObject CreateBubble(GameObject prefab, string text)
        {
            if (prefab == null || _chatContent == null) return null;

            var obj = Instantiate(prefab, _chatContent);
            var tmp = obj.GetComponentInChildren<TMP_Text>();
            if (tmp != null)
                tmp.text = text;

            _activeBubbles.Add(obj);

            // 오래된 버블 제거
            while (_activeBubbles.Count > _maxBubbles)
            {
                var oldest = _activeBubbles[0];
                _activeBubbles.RemoveAt(0);
                if (oldest != null) Destroy(oldest);
            }

            return obj;
        }

        private void DestroyAllBubbles()
        {
            foreach (var obj in _activeBubbles)
            {
                if (obj != null) Destroy(obj);
            }
            _activeBubbles.Clear();
        }

        private void AutoScroll()
        {
            // 다음 프레임에서 스크롤 (레이아웃 계산 후)
            Canvas.ForceUpdateCanvases();
            if (_scrollRect != null)
                _scrollRect.verticalNormalizedPosition = 0f;
        }
    }
}
