namespace OpenDesk.Onboarding.Models
{
    /// <summary>
    /// Google OAuth(스텁 포함) 결과. 성공 시 Email, 실패 시 ErrorMessage.
    /// </summary>
    public sealed class AuthResult
    {
        public bool Success { get; }
        public string Email { get; }
        public string ErrorMessage { get; }

        private AuthResult(bool success, string email, string errorMessage)
        {
            Success = success;
            Email = email;
            ErrorMessage = errorMessage;
        }

        public static AuthResult Ok(string email) => new AuthResult(true, email ?? string.Empty, null);
        public static AuthResult Fail(string errorMessage) => new AuthResult(false, null, errorMessage ?? "unknown error");
    }
}
