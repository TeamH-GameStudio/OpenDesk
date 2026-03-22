using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using OpenDesk.Core.Models;
using OpenDesk.Core.Services;
using R3;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

namespace OpenDesk.Presentation.UI.Panels
{
    /// <summary>API 키 관리 패널 — 14개+ 제공업체 키 입력/검증/삭제</summary>
    public class ApiKeysPanelController : MonoBehaviour
    {
        [SerializeField] private RectTransform _providerContainer;
        [SerializeField] private GameObject    _providerCardPrefab;
        [SerializeField] private TMP_Text      _ollamaStatusText;

        [Inject] private IApiKeyVaultService _vault;

        private readonly Dictionary<string, ProviderCardRefs> _cards = new();

        private async void Start()
        {
            if (_vault == null || _providerCardPrefab == null) return;

            // Ollama 상태 표시
            var canRunFree = await _vault.CanRunWithoutApiKeyAsync();
            if (_ollamaStatusText != null)
                _ollamaStatusText.text = canRunFree
                    ? "✓ Ollama 감지됨 — Free 모드 사용 가능 (API 키 없이 무료)"
                    : "Ollama 미감지 — API 키를 입력하거나 Ollama를 설치하세요";

            // 제공업체 카드 생성
            foreach (var provider in _vault.GetProviders())
                CreateProviderCard(provider);

            _vault.OnKeyChanged.Subscribe(OnKeyChanged).AddTo(this);
        }

        private void CreateProviderCard(ApiProvider provider)
        {
            var obj  = Instantiate(_providerCardPrefab, _providerContainer);
            var refs = new ProviderCardRefs
            {
                ProviderId = provider.Id,
                NameText   = obj.transform.Find("NameText")?.GetComponent<TMP_Text>(),
                HintText   = obj.transform.Find("HintText")?.GetComponent<TMP_Text>(),
                StatusText = obj.transform.Find("StatusText")?.GetComponent<TMP_Text>(),
                StatusIcon = obj.transform.Find("StatusIcon")?.GetComponent<Image>(),
                KeyInput   = obj.GetComponentInChildren<TMP_InputField>(),
                SaveBtn    = obj.transform.Find("SaveButton")?.GetComponent<Button>(),
                DeleteBtn  = obj.transform.Find("DeleteButton")?.GetComponent<Button>(),
                SignupBtn  = obj.transform.Find("SignupButton")?.GetComponent<Button>(),
                LocalBadge = obj.transform.Find("LocalBadge")?.gameObject,
            };

            if (refs.NameText != null) refs.NameText.text = provider.DisplayName;
            if (refs.HintText != null) refs.HintText.text = provider.KeyHint;

            // 로컬 모델 뱃지
            if (refs.LocalBadge != null) refs.LocalBadge.SetActive(provider.IsLocal);

            // 키 불필요 시 입력란 숨김
            if (!provider.RequiresKey && refs.KeyInput != null)
                refs.KeyInput.gameObject.SetActive(false);

            // 저장 버튼
            refs.SaveBtn?.onClick.AddListener(() =>
            {
                var key = refs.KeyInput != null ? refs.KeyInput.text : "";
                _vault.SaveKeyAsync(provider.Id, key).Forget();
            });

            // 삭제 버튼
            refs.DeleteBtn?.onClick.AddListener(() =>
                _vault.DeleteKeyAsync(provider.Id).Forget());

            // 가입 링크
            if (!string.IsNullOrEmpty(provider.SignupUrl))
                refs.SignupBtn?.onClick.AddListener(() => Application.OpenURL(provider.SignupUrl));

            _cards[provider.Id] = refs;

            // 현재 상태 표시
            var status = _vault.GetKeyStatus(provider.Id);
            UpdateStatusUI(refs, status.Status);
        }

        private void OnKeyChanged(ApiKeyEntry entry)
        {
            if (_cards.TryGetValue(entry.ProviderId, out var refs))
                UpdateStatusUI(refs, entry.Status);
        }

        private void UpdateStatusUI(ProviderCardRefs refs, ApiKeyStatus status)
        {
            var (text, color) = status switch
            {
                ApiKeyStatus.Valid      => ("✓ 유효", Color.green),
                ApiKeyStatus.Invalid    => ("✗ 유효하지 않음", Color.red),
                ApiKeyStatus.Validating => ("검증 중...", Color.yellow),
                ApiKeyStatus.Error      => ("오류", Color.red),
                _                       => ("미설정", Color.gray),
            };

            if (refs.StatusText != null) refs.StatusText.text  = text;
            if (refs.StatusIcon != null) refs.StatusIcon.color  = color;
        }

        private class ProviderCardRefs
        {
            public string          ProviderId;
            public TMP_Text        NameText, HintText, StatusText;
            public Image           StatusIcon;
            public TMP_InputField  KeyInput;
            public Button          SaveBtn, DeleteBtn, SignupBtn;
            public GameObject      LocalBadge;
        }
    }
}
