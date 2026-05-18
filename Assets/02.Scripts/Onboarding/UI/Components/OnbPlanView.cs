using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using Cysharp.Threading.Tasks;
using OpenDesk.Onboarding.Models;
using OpenDesk.Onboarding.ViewModels;
using UnityEngine.UIElements;

namespace OpenDesk.Onboarding.UI.Components
{
    /// <summary>
    /// §2 플랜 선택 화면 컨트롤러. 카드 3개 동적 생성 + selected 상태 토글.
    /// </summary>
    public sealed class OnbPlanView : IDisposable
    {
        private readonly OnbPlanViewModel _vm;
        private readonly CancellationToken _ct;
        private readonly Button _prev;
        private readonly Button _next;
        private readonly Dictionary<PlanTier, VisualElement> _cards = new();

        private static readonly PlanCardSpec[] CardSpecs =
        {
            new PlanCardSpec(PlanTier.Dev,  "Dev Plan",  "무료",      "Claude CLI를 이미 쓰는 분",
                new[] { "무제한 메시지", "본인 API 사용", "개발자 친화" }, false, false),
            new PlanCardSpec(PlanTier.Free, "Free Plan", "무료",      "가볍게 시작하고 싶은 분",
                new[] { "제한된 메시지", "동료 1명", "기본 모델" }, true, false),
            new PlanCardSpec(PlanTier.Pro,  "Pro Plan",  "곧 공개",    "본격적으로 사무실을 운영하고 싶은 분",
                new[] { "무제한 메시지", "여러 명", "모든 모델" }, false, true),
        };

        public OnbPlanView(VisualElement root, OnbPlanViewModel vm, CancellationToken ct)
        {
            _vm = vm;
            _ct = ct;
            _prev = root.Q<Button>("onb-plan__prev");
            _next = root.Q<Button>("onb-plan__next");
            var cardsContainer = root.Q<VisualElement>("onb-plan__cards");

            BuildCards(cardsContainer);

            if (_prev != null) _prev.clicked += OnPrevClicked;
            if (_next != null) _next.clicked += OnNextClicked;

            _vm.PropertyChanged += OnVmPropertyChanged;
            RefreshState();
        }

        private void BuildCards(VisualElement container)
        {
            if (container == null) return;
            container.Clear();

            foreach (var spec in CardSpecs)
            {
                var card = new VisualElement();
                card.AddToClassList("onb-plan-card");
                if (spec.IsDisabled) card.AddToClassList("onb-plan-card--disabled");

                if (spec.IsRecommended)
                {
                    var badge = new Label("추천");
                    badge.AddToClassList("onb-plan-card__badge");
                    card.Add(badge);
                }

                var name = new Label(spec.Name);
                name.AddToClassList("onb-plan-card__name");
                card.Add(name);

                var price = new Label(spec.Price);
                price.AddToClassList("onb-plan-card__price");
                card.Add(price);

                var hook = new Label(spec.Hook);
                hook.AddToClassList("onb-plan-card__hook");
                card.Add(hook);

                var features = new VisualElement();
                features.AddToClassList("onb-plan-card__features");
                foreach (var f in spec.Features)
                {
                    var item = new Label("· " + f);
                    item.AddToClassList("onb-plan-card__feature");
                    features.Add(item);
                }
                card.Add(features);

                if (!spec.IsDisabled)
                {
                    var capturedTier = spec.Tier;
                    card.RegisterCallback<ClickEvent>(_ => _vm.Select(capturedTier));
                }

                _cards[spec.Tier] = card;
                container.Add(card);
            }
        }

        private void OnPrevClicked() => _vm.Back();

        private void OnNextClicked() => _vm.AdvanceAsync(_ct).Forget();

        private void OnVmPropertyChanged(object sender, PropertyChangedEventArgs e) => RefreshState();

        private void RefreshState()
        {
            foreach (var pair in _cards)
            {
                var isSelected = _vm.SelectedTier == pair.Key;
                pair.Value.EnableInClassList("onb-plan-card--selected", isSelected);
            }

            if (_next != null) _next.SetEnabled(_vm.CanAdvance);
        }

        public void Dispose()
        {
            if (_prev != null) _prev.clicked -= OnPrevClicked;
            if (_next != null) _next.clicked -= OnNextClicked;
            _vm.PropertyChanged -= OnVmPropertyChanged;
        }

        private readonly struct PlanCardSpec
        {
            public PlanTier Tier { get; }
            public string Name { get; }
            public string Price { get; }
            public string Hook { get; }
            public string[] Features { get; }
            public bool IsRecommended { get; }
            public bool IsDisabled { get; }

            public PlanCardSpec(PlanTier tier, string name, string price, string hook, string[] features, bool isRecommended, bool isDisabled)
            {
                Tier = tier;
                Name = name;
                Price = price;
                Hook = hook;
                Features = features;
                IsRecommended = isRecommended;
                IsDisabled = isDisabled;
            }
        }
    }
}
