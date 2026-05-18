namespace OpenDesk.Core.Models.Plugins
{
    public sealed record CredentialRequirement(
        string Key,
        string DisplayName,
        CredentialKind Kind,
        bool Optional
    )
    {
        public static CredentialRequirement ApiKey(string key, string displayName, bool optional = false) =>
            new(key ?? string.Empty, displayName ?? key ?? string.Empty, CredentialKind.ApiKey, optional);
    }
}
