using System;
using OpenDesk.Claude;
using OpenDesk.Claude.Models;
using R3;
using UnityEngine;

namespace OpenDesk.Core.Services.Auth
{
    /// <summary>
    /// 미들웨어 'claude login' RPC 를 R3 Observable 로 노출한다.
    /// ClaudeWebSocketClient 의 OnAuthEvent 를 구독하여 phase 머신을 갱신.
    ///
    /// 성공 시 AnthropicCredentialService 에 OAuth 토큰 변경 알림을 푸시 — UI 가 즉시 갱신.
    /// </summary>
    public sealed class AuthLoginService : IAuthLoginService, IDisposable
    {
        private readonly ClaudeWebSocketClient _client;
        private readonly AnthropicCredentialService _credentials; // optional; null-safe
        private readonly Subject<AuthLoginState> _onState = new();
        private AuthLoginState _state = new() { Phase = AuthLoginPhase.Idle };

        public Observable<AuthLoginState> OnState => _onState;
        public AuthLoginState CurrentState => _state;
        public bool IsActive =>
            _state.Phase == AuthLoginPhase.Starting
            || _state.Phase == AuthLoginPhase.AwaitingUser
            || _state.Phase == AuthLoginPhase.Polling;

        public AuthLoginService(ClaudeWebSocketClient client, IAnthropicCredentialService credentials = null)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _credentials = credentials as AnthropicCredentialService; // 토큰 변경 알림용 (concrete 일 때만)
            _client.OnAuthEvent += HandleAuthEvent;
        }

        public void Start()
        {
            if (IsActive) return;
            if (!_client.IsConnected)
            {
                Update(new AuthLoginState
                {
                    Phase = AuthLoginPhase.Failed,
                    Message = "미들웨어에 연결되지 않았습니다",
                });
                return;
            }
            Update(new AuthLoginState { Phase = AuthLoginPhase.Starting, Message = "login 요청 전송" });
            _client.SendAuthStart();
        }

        public void Cancel()
        {
            if (!IsActive) return;
            _client.SendAuthCancel();
            Update(new AuthLoginState { Phase = AuthLoginPhase.Cancelled, Message = "사용자 취소" });
        }

        public void Dispose()
        {
            if (_client != null) _client.OnAuthEvent -= HandleAuthEvent;
            _onState.Dispose();
        }

        // ── 내부 ──────────────────────────────────────────────

        private void HandleAuthEvent(AuthEventMessage msg)
        {
            if (msg == null || string.IsNullOrEmpty(msg.state)) return;

            switch (msg.state)
            {
                case "url":
                    Update(new AuthLoginState
                    {
                        Phase = AuthLoginPhase.AwaitingUser,
                        Url = msg.url ?? string.Empty,
                        Code = _state.Code,
                        Message = string.IsNullOrEmpty(msg.message) ? "브라우저에서 인증을 완료해주세요" : msg.message,
                    });
                    break;

                case "code":
                    Update(new AuthLoginState
                    {
                        Phase = AuthLoginPhase.AwaitingUser,
                        Url = _state.Url,
                        Code = msg.code ?? string.Empty,
                        Message = string.IsNullOrEmpty(msg.message) ? "코드를 입력해주세요" : msg.message,
                    });
                    break;

                case "status":
                    if (_state.Phase == AuthLoginPhase.AwaitingUser
                        || _state.Phase == AuthLoginPhase.Polling
                        || _state.Phase == AuthLoginPhase.Starting)
                    {
                        Update(new AuthLoginState
                        {
                            Phase = AuthLoginPhase.Polling,
                            Url = _state.Url,
                            Code = _state.Code,
                            Message = msg.message ?? string.Empty,
                        });
                    }
                    break;

                case "success":
                    Update(new AuthLoginState
                    {
                        Phase = AuthLoginPhase.Success,
                        Url = _state.Url,
                        Code = _state.Code,
                        Message = msg.message ?? "인증 완료",
                    });
                    _credentials?.NotifyOAuthTokensChanged();
                    break;

                case "failed":
                    Update(new AuthLoginState
                    {
                        Phase = AuthLoginPhase.Failed,
                        Url = _state.Url,
                        Code = _state.Code,
                        Message = msg.message ?? "인증 실패",
                    });
                    break;

                default:
                    Debug.LogWarning($"[AuthLogin] 알 수 없는 state: {msg.state}");
                    break;
            }
        }

        private void Update(AuthLoginState next)
        {
            _state = next;
            _onState.OnNext(next);
        }
    }
}
