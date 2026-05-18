namespace OpenDesk.Onboarding.Models
{
    /// <summary>
    /// 사용자 기본 프로필 (immutable). 영속 컨테이너는 <see cref="UserProfileData"/>가 별도로 관리한다.
    /// </summary>
    public sealed class UserProfile
    {
        public string Name { get; }
        public Gender Gender { get; }
        public AgeBucket Age { get; }

        public UserProfile(string name, Gender gender, AgeBucket age)
        {
            Name = name ?? string.Empty;
            Gender = gender;
            Age = age;
        }

        public UserProfile WithName(string name) => new UserProfile(name, Gender, Age);
        public UserProfile WithGender(Gender gender) => new UserProfile(Name, gender, Age);
        public UserProfile WithAge(AgeBucket age) => new UserProfile(Name, Gender, age);
    }
}
