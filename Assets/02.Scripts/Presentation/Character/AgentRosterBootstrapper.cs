using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using OpenDesk.AgentCreation.Models;
using OpenDesk.AgentCreation.Persistence;
using OpenDesk.Characters.Wardrobe;
using OpenDesk.Characters.Wardrobe.Persistence;
using OpenDesk.Core.Services;
using OpenDesk.SkillDiskette;
using UnityEngine;
using VContainer;

namespace OpenDesk.Presentation.Character
{
    /// <summary>
    /// 오피스 씬 진입 시 AgentDraftJsonStore 의 모든 record 를 다중 스폰.
    /// IAgentCreationBridge.AgentSaved 구독 — 위저드에서 새 에이전트가 저장되면 즉시 추가 스폰.
    /// 스폰 + 와드로브 적용 + 최소 3초 대기 후 RaiseOfficeSetupCompleted 발행.
    ///
    /// 0명일 때 OnEmptyRoster 이벤트 발행 — UI 측에서 빈 사무실 CTA 노출.
    /// </summary>
    public sealed class AgentRosterBootstrapper : MonoBehaviour
    {
        private const int MinSetupDelayMs = 3000;

        [Header("Spawn")]
        [SerializeField] private AgentSpawner _spawner;
        [Tooltip("모든 에이전트가 공유하는 마네킹 프리팹. 외형 차이는 와드로브로만 분기.")]
        [SerializeField] private GameObject _mannequinPrefab;

        public event Action OnEmptyRoster;
        public event Action<string> OnAgentSpawned; // sessionId

        private AgentDraftJsonStore _store;
        private IAgentCreationBridge _bridge;

        private WardrobeCatalogSO _catalog;
        private bool _catalogReady;

        [Inject]
        public void Construct(AgentDraftJsonStore store, IAgentCreationBridge bridge)
        {
            _store = store;
            _bridge = bridge;
        }

        private void OnEnable()
        {
            if (_bridge != null)
                _bridge.AgentSaved += OnAgentSavedFromBridge;
        }

        private void OnDisable()
        {
            if (_bridge != null)
                _bridge.AgentSaved -= OnAgentSavedFromBridge;
        }

        private async void Start()
        {
            await BootAsync(this.GetCancellationTokenOnDestroy());
        }

        // ────────────────────────────────────────────────────────
        //  최초 부팅
        // ────────────────────────────────────────────────────────

        private async UniTask BootAsync(CancellationToken ct)
        {
            if (_spawner == null)
            {
                Debug.LogError("[AgentRosterBootstrapper] AgentSpawner reference missing.");
                return;
            }
            if (_store == null)
            {
                Debug.LogError("[AgentRosterBootstrapper] AgentDraftJsonStore not injected.");
                return;
            }

            await EnsureCatalogAsync(ct);

            var records = _store.LoadAll();
            if (records == null || records.Count == 0)
            {
                Debug.Log("[AgentRosterBootstrapper] 저장된 에이전트 0명 — OnEmptyRoster 발행.");
                OnEmptyRoster?.Invoke();
                return;
            }

            Debug.Log($"[AgentRosterBootstrapper] 저장된 에이전트 {records.Count}명 로드.");
            foreach (var rec in records)
            {
                if (rec == null) continue;
                SpawnFromRecord(rec);
            }
        }

        // ────────────────────────────────────────────────────────
        //  Bridge 핸들러 (위저드 완료 → 추가 스폰)
        // ────────────────────────────────────────────────────────

        private void OnAgentSavedFromBridge(AgentDraftRecord record, string savedPath)
        {
            HandleAddAgentAsync(record, this.GetCancellationTokenOnDestroy()).Forget();
        }

        private async UniTaskVoid HandleAddAgentAsync(AgentDraftRecord record, CancellationToken ct)
        {
            try
            {
                await EnsureCatalogAsync(ct);

                // 작업 + 최소 3초 보장 — UniTask.WhenAll 패턴.
                var minDelay = UniTask.Delay(MinSetupDelayMs, cancellationToken: ct);
                SpawnFromRecord(record);
                // 1프레임 yield — 이번 프레임 안에 와드로브 Apply 까지 마무리.
                await UniTask.Yield(PlayerLoopTiming.LastPostLateUpdate, ct);
                await minDelay;

                _bridge?.RaiseOfficeSetupCompleted();
            }
            catch (OperationCanceledException) { /* 씬 종료 — 무시 */ }
            catch (Exception e)
            {
                Debug.LogError($"[AgentRosterBootstrapper] HandleAddAgent 실패: {e}");
                _bridge?.RaiseOfficeSetupCompleted(); // 안전망: 실패해도 위저드는 unload 되어야 함
            }
        }

        // ────────────────────────────────────────────────────────
        //  스폰 + 와드로브
        // ────────────────────────────────────────────────────────

        private AgentSpawner.SpawnedAgent SpawnFromRecord(AgentDraftRecord record)
        {
            var profile = AgentProfileFactory.FromRecord(record, _mannequinPrefab);
            var spawned = _spawner.SpawnAgent(profile);
            if (spawned == null)
            {
                Debug.LogWarning($"[AgentRosterBootstrapper] 스폰 실패: {record.name} ({record.id})");
                return null;
            }

            ApplyWardrobe(spawned, record);
            ApplyEquipmentProfile(spawned, record);
            OnAgentSpawned?.Invoke(record.id);
            return spawned;
        }

        // record → AgentEquipmentManager.BindAgent — traits/tone/soulBlock 등 모든 필드를 손실 없이 전달.
        // 옛 SetAgentProfile 은 traits 를 어조 슬롯에 합성하는 우회 처리를 했지만, BindAgent 는
        // <personality> XML 블록으로 분리해 모델이 무시할 수 없게 한다.
        private static void ApplyEquipmentProfile(AgentSpawner.SpawnedAgent spawned, AgentDraftRecord record)
        {
            if (spawned?.ModelInstance == null || record == null) return;

            var controller = spawned.ModelInstance.GetComponent<AgentCharacterController>()
                             ?? spawned.ModelInstance.GetComponentInChildren<AgentCharacterController>();
            var equipment = controller != null ? controller.Equipment : null;
            if (equipment == null)
            {
                Debug.LogWarning($"[AgentRosterBootstrapper] AgentEquipmentManager 미발견: {record.name}");
                return;
            }

            equipment.BindAgent(record);
        }

        private void ApplyWardrobe(AgentSpawner.SpawnedAgent spawned, AgentDraftRecord record)
        {
            if (!_catalogReady || _catalog == null) return;
            if (record?.wardrobe == null) return;

            var applier = spawned.ModelInstance != null
                ? spawned.ModelInstance.GetComponentInChildren<WardrobeApplier>()
                : null;
            if (applier == null)
            {
                Debug.LogWarning($"[AgentRosterBootstrapper] 마네킹 프리팹에 WardrobeApplier 없음: {record.name}");
                return;
            }

            var outfit = ToOutfit(record.wardrobe);
            applier.SetCatalog(_catalog);
            applier.Apply(outfit.ToWardrobe(_catalog));

            // 머리 색상 — Outfit 옵션 ID 와 별개 채널로 저장됨. 위저드에서 사용자가 고른 hex
            // (preset swatch 또는 커스텀 ColorField) 를 office 스폰 시 그대로 복원한다.
            // 빈 문자열 / 파싱 실패 시 applier 의 _defaultHairColor 가 그대로 유지된다.
            var hairHex = record.wardrobe.hairColor;
            if (!string.IsNullOrEmpty(hairHex) && ColorUtility.TryParseHtmlString(hairHex, out var hairColor))
            {
                applier.SetHairColor(hairColor);
            }
        }

        private static WardrobeOutfit ToOutfit(AgentDraftRecord.WardrobeRecord wr)
        {
            return new WardrobeOutfit
            {
                Skin   = wr.skin   ?? string.Empty,
                Hair   = wr.hair   ?? string.Empty,
                Eyes   = wr.eyes   ?? string.Empty,
                Mouth  = wr.mouth  ?? string.Empty,
                Top    = wr.top    ?? string.Empty,
                Bottom = wr.bottom ?? string.Empty,
                Shoes  = wr.shoes  ?? string.Empty,
            };
        }

        private async UniTask EnsureCatalogAsync(CancellationToken ct)
        {
            if (_catalogReady) return;
            try
            {
                _catalog = await WardrobeCatalogService.GetAsync(ct);
                _catalogReady = _catalog != null;
                if (!_catalogReady)
                    Debug.LogWarning("[AgentRosterBootstrapper] WardrobeCatalog 로드 실패 — 와드로브 미적용.");
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception e)
            {
                Debug.LogError($"[AgentRosterBootstrapper] WardrobeCatalog 예외: {e.Message}");
            }
        }
    }
}
