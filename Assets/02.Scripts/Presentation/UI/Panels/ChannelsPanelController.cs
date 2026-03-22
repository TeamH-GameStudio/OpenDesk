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
    /// <summary>채널 연동 패널 — Telegram/Discord/Slack 등 토큰 입력 + 연결 관리</summary>
    public class ChannelsPanelController : MonoBehaviour
    {
        [SerializeField] private RectTransform _channelContainer;
        [SerializeField] private GameObject    _channelCardPrefab;

        [Inject] private IChannelService _channelService;

        private readonly Dictionary<ChannelType, ChannelCardRefs> _cards = new();

        private void Start()
        {
            if (_channelService == null || _channelCardPrefab == null) return;

            foreach (var config in _channelService.GetChannels())
                CreateCard(config);

            _channelService.OnChannelStatusChanged.Subscribe(OnStatusChanged).AddTo(this);
        }

        private void CreateCard(ChannelConfig config)
        {
            var obj  = Instantiate(_channelCardPrefab, _channelContainer);
            var refs = new ChannelCardRefs
            {
                Root        = obj,
                NameText    = obj.transform.Find("NameText")?.GetComponent<TMP_Text>(),
                StatusText  = obj.transform.Find("StatusText")?.GetComponent<TMP_Text>(),
                StatusIcon  = obj.transform.Find("StatusIcon")?.GetComponent<Image>(),
                TokenInput  = obj.GetComponentInChildren<TMP_InputField>(),
                ConnectBtn  = obj.transform.Find("ConnectButton")?.GetComponent<Button>(),
                DisconnectBtn = obj.transform.Find("DisconnectButton")?.GetComponent<Button>(),
                TestBtn     = obj.transform.Find("TestButton")?.GetComponent<Button>(),
                GuideBtn    = obj.transform.Find("GuideButton")?.GetComponent<Button>(),
            };

            if (refs.NameText != null) refs.NameText.text = config.DisplayName;

            // 연결 버튼
            refs.ConnectBtn?.onClick.AddListener(() =>
            {
                var token = refs.TokenInput != null ? refs.TokenInput.text : "";
                _channelService.ConfigureChannelAsync(config.Type, token).Forget();
            });

            // 해제 버튼
            refs.DisconnectBtn?.onClick.AddListener(() =>
                _channelService.DisconnectChannelAsync(config.Type).Forget());

            // 테스트 버튼
            refs.TestBtn?.onClick.AddListener(() =>
                _channelService.TestConnectionAsync(config.Type).Forget());

            // 가이드 링크
            if (!string.IsNullOrEmpty(config.SetupGuideUrl))
                refs.GuideBtn?.onClick.AddListener(() => Application.OpenURL(config.SetupGuideUrl));

            _cards[config.Type] = refs;
            UpdateCardUI(config.Type, config.Status);
        }

        private void OnStatusChanged(ChannelConfig config)
        {
            UpdateCardUI(config.Type, config.Status);
        }

        private void UpdateCardUI(ChannelType type, ChannelStatus status)
        {
            if (!_cards.TryGetValue(type, out var refs)) return;

            var (text, color) = status switch
            {
                ChannelStatus.Connected     => ("연결됨", Color.green),
                ChannelStatus.Connecting    => ("연결 중...", Color.yellow),
                ChannelStatus.Disconnected  => ("연결 끊김", Color.red),
                ChannelStatus.Error         => ("오류", Color.red),
                _                           => ("미설정", Color.gray),
            };

            if (refs.StatusText != null) refs.StatusText.text  = text;
            if (refs.StatusIcon != null) refs.StatusIcon.color  = color;

            var isConnected = status == ChannelStatus.Connected;
            refs.ConnectBtn?.gameObject.SetActive(!isConnected);
            refs.DisconnectBtn?.gameObject.SetActive(isConnected);
            refs.TestBtn?.gameObject.SetActive(isConnected);
        }

        private class ChannelCardRefs
        {
            public GameObject      Root;
            public TMP_Text        NameText, StatusText;
            public Image           StatusIcon;
            public TMP_InputField  TokenInput;
            public Button          ConnectBtn, DisconnectBtn, TestBtn, GuideBtn;
        }
    }
}
