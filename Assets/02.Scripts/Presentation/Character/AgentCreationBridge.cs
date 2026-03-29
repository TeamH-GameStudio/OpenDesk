using OpenDesk.AgentCreation.Models;
using OpenDesk.Presentation.UI.AgentCreation;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OpenDesk.Presentation.Character
{
    /// <summary>
    /// 오피스 씬에서 위저드 UI를 열고, 생성 완료 시 AgentSpawner로 소환하는 브릿지.
    /// "에이전트 생성" 버튼 클릭 → AgentCreationScene 로드 대신
    /// 인라인 위저드 패널을 활성화하는 심플 브릿지.
    ///
    /// 현 단계에서는 위저드 컨트롤러를 동적 생성하지 않고,
    /// 런타임에 AgentProfileSO를 직접 생성하여 Spawner에 전달.
    /// </summary>
    public class AgentCreationBridge : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private AgentSpawner _spawner;
        [SerializeField] private GameObject _defaultModelPrefab;
        [SerializeField] private Button _startButton;
        [SerializeField] private GameObject _guideText;

        [Header("Wizard (옵션 — AgentCreationScene 사용 시)")]
        [SerializeField] private AgentCreationWizardController _wizardController;

        private int _createdCount;

        private void Start()
        {
            if (_startButton != null)
                _startButton.onClick.AddListener(OnStartButtonClicked);

            // 위저드 컨트롤러 이벤트 연결
            if (_wizardController != null)
                _wizardController.OnAgentCreated += OnWizardCompleted;
        }

        private void OnDestroy()
        {
            if (_wizardController != null)
                _wizardController.OnAgentCreated -= OnWizardCompleted;
        }

        private void OnStartButtonClicked()
        {
            if (_wizardController != null)
            {
                // 인라인 위저드 사용
                _wizardController.StartWizard();
            }
            else
            {
                // 위저드 없이 즉시 테스트 소환
                SpawnTestAgent();
            }
        }

        /// <summary>위저드 없이 바로 테스트 소환 (디버그/데모)</summary>
        private void SpawnTestAgent()
        {
            if (_spawner == null || _spawner.AvailableSpawnPointCount <= 0)
            {
                Debug.LogWarning("[Bridge] 소환 불가: Spawner 없음 또는 SpawnPoint 부족");
                return;
            }

            _createdCount++;

            var roles = new[] { AgentRole.Development, AgentRole.Planning, AgentRole.Design, AgentRole.Research };
            var models = new[] { AgentAIModel.GPT4o, AgentAIModel.ClaudeSonnet, AgentAIModel.GeminiPro };
            var tones = new[] { AgentTone.Friendly, AgentTone.Logical, AgentTone.Humorous };
            var names = new[] { "스카우트", "플래너", "아티스트", "리서처" };

            var idx = (_createdCount - 1) % names.Length;

            var data = new AgentCreationData
            {
                AgentName = names[idx],
                Role = roles[idx % roles.Length],
                AIModel = models[idx % models.Length],
                Tone = tones[idx % tones.Length],
            };

            var profile = AgentProfileSO.CreateFromData(data, _defaultModelPrefab);
            _spawner.SpawnAgent(profile);

            UpdateGuide();
            Debug.Log($"[Bridge] 테스트 소환: {data.AgentName}");
        }

        /// <summary>위저드 완료 콜백</summary>
        private void OnWizardCompleted(AgentCreationData data)
        {
            _createdCount++;
            UpdateGuide();
            Debug.Log($"[Bridge] 위저드 완료 소환: {data.AgentName}");
        }

        private void UpdateGuide()
        {
            if (_guideText != null)
            {
                var tmp = _guideText.GetComponent<TMP_Text>();
                if (tmp != null)
                {
                    var remaining = _spawner != null ? _spawner.AvailableSpawnPointCount : 0;
                    tmp.text = remaining > 0
                        ? $"에이전트 {_createdCount}명 배치 완료 — {remaining}자리 남음"
                        : "모든 자리가 채워졌습니다!";
                }
            }

            // SpawnPoint 다 차면 버튼 비활성화
            if (_startButton != null && _spawner != null)
                _startButton.interactable = _spawner.AvailableSpawnPointCount > 0;
        }
    }
}
