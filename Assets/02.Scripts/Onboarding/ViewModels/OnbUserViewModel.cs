using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using OpenDesk.Onboarding.Common;
using OpenDesk.Onboarding.Models;
using OpenDesk.Onboarding.Services;

namespace OpenDesk.Onboarding.ViewModels
{
    /// <summary>
    /// §4 유저 정보 입력 ViewModel.
    /// </summary>
    public sealed class OnbUserViewModel : ObservableObject
    {
        private readonly IUserProfileService _userProfileService;

        private string _name = string.Empty;
        public string Name
        {
            get => _name;
            set
            {
                if (SetField(ref _name, value ?? string.Empty))
                {
                    Raise(nameof(CanAdvance));
                }
            }
        }

        private Gender? _selectedGender;
        public Gender? SelectedGender
        {
            get => _selectedGender;
            private set
            {
                if (SetField(ref _selectedGender, value))
                {
                    Raise(nameof(CanAdvance));
                }
            }
        }

        private AgeBucket? _selectedAge;
        public AgeBucket? SelectedAge
        {
            get => _selectedAge;
            private set
            {
                if (SetField(ref _selectedAge, value))
                {
                    Raise(nameof(CanAdvance));
                }
            }
        }

        public bool CanAdvance =>
            !string.IsNullOrWhiteSpace(_name) &&
            _selectedGender.HasValue &&
            _selectedAge.HasValue;

        public event Action<UserProfile> UserProfileCommitted;
        public event Action BackRequested;

        public OnbUserViewModel(IUserProfileService userProfileService)
        {
            _userProfileService = userProfileService;

            var existing = userProfileService?.Current;
            if (existing != null)
            {
                _name = existing.Name;
                _selectedGender = existing.Gender;
                _selectedAge = existing.Age;
            }
        }

        public void SelectGender(Gender gender) => SelectedGender = gender;

        public void SelectAge(AgeBucket age) => SelectedAge = age;

        public void Back() => BackRequested?.Invoke();

        public async UniTask AdvanceAsync(CancellationToken ct = default)
        {
            if (!CanAdvance || _userProfileService == null) return;

            var profile = new UserProfile(_name.Trim(), _selectedGender!.Value, _selectedAge!.Value);
            await _userProfileService.SaveAsync(profile, ct);
            UserProfileCommitted?.Invoke(profile);
        }
    }
}
