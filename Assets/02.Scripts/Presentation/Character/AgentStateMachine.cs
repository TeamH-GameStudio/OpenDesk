using System;
using System.Collections.Generic;
using OpenDesk.Presentation.Character.States;
using UnityEngine;

namespace OpenDesk.Presentation.Character
{
    /// <summary>
    /// ProjectH StateMachine을 OpenDesk 캐릭터용으로 간소화
    /// - 전투/스킬/상태이상 제거
    /// - Pending 방식 유지 (Update 타이밍 안전)
    /// - IAgentState 기반
    /// </summary>
    public class AgentStateMachine
    {
        private readonly Dictionary<Type, IAgentState> _states = new();
        private IAgentState _currentState;
        private IAgentState _pendingState;

        public string CurrentStateName => _currentState?.Name ?? "None";
        public event Action<string, string> OnStateChanged; // (from, to)

        // ── 등록 ─────────────────────────────────────────────────────────────

        public void AddState(IAgentState state)
        {
            _states[state.GetType()] = state;
        }

        public void Initialize<TState>() where TState : IAgentState
        {
            if (!_states.TryGetValue(typeof(TState), out var state))
            {
                Debug.LogError($"[AgentFSM] State 미등록: {typeof(TState).Name}");
                return;
            }
            _currentState = state;
            _currentState.Enter();
        }

        // ── 전환 요청 (Pending 방식 — Update에서 실제 전환) ─────────────────

        public void TransitionTo<TState>() where TState : IAgentState
        {
            if (!_states.TryGetValue(typeof(TState), out var next))
            {
                Debug.LogError($"[AgentFSM] State 미등록: {typeof(TState).Name}");
                return;
            }

            if (_currentState?.GetType() == typeof(TState)) return;

            _pendingState = next;
        }

        // ── 매 프레임 ────────────────────────────────────────────────────────

        public void Update(float deltaTime)
        {
            if (_pendingState != null)
            {
                var from = _currentState?.Name ?? "None";
                _currentState?.Exit();
                _currentState = _pendingState;
                _currentState.Enter();
                _pendingState = null;

                OnStateChanged?.Invoke(from, _currentState.Name);
            }

            _currentState?.Update(deltaTime);
        }

        public bool IsInState<TState>() where TState : IAgentState
            => _currentState?.GetType() == typeof(TState);

        public void Cleanup()
        {
            _currentState?.Exit();
            _states.Clear();
            _currentState = null;
            _pendingState = null;
        }
    }
}
