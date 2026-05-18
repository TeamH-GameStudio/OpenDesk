using System;
using OpenDesk.Onboarding.ViewModels;
using UnityEngine.UIElements;

namespace OpenDesk.Onboarding.UI.Components
{
    /// <summary>
    /// §1 환영 화면 컨트롤러.
    /// </summary>
    public sealed class OnbWelcomeView : IDisposable
    {
        private readonly OnbWelcomeViewModel _vm;
        private readonly Button _cta;

        public OnbWelcomeView(VisualElement root, OnbWelcomeViewModel vm)
        {
            _vm = vm;
            _cta = root.Q<Button>("onb-welcome__cta");

            if (_cta != null)
            {
                _cta.clicked += OnCtaClicked;
            }
        }

        private void OnCtaClicked() => _vm.Start();

        public void Dispose()
        {
            if (_cta != null)
            {
                _cta.clicked -= OnCtaClicked;
            }
        }
    }
}
