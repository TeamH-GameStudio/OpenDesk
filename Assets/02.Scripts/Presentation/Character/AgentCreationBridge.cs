using OpenDesk.AgentCreation.Models;
using OpenDesk.Presentation.UI.AgentCreation;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OpenDesk.Presentation.Character
{
    /// <summary>
    /// 오피스 씬에서 "에이전트 생성" 버튼을 위저드와 연결하는 브릿지.
    /// 생성은 위저드를 통해서만 이루어지며, 위저드 완료 콜백에서 가이드 UI를 갱신한다.
    /// </summary>
    public class AgentCreationBridge : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private AgentSpawner _spawner;
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
                _wizardController.StartWizard();
                return;
            }

            Debug.LogWarning("[Bridge] 위저드 컨트롤러가 연결되지 않았습니다. 에이전트는 위저드를 통해서만 생성됩니다.");
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
