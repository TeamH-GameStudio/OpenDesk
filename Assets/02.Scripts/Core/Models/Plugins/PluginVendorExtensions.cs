using UnityEngine;

namespace OpenDesk.Core.Models.Plugins
{
    public static class PluginVendorExtensions
    {
        public static string ToSerializedKey(this PluginVendor vendor) => vendor switch
        {
            PluginVendor.Notion         => "notion",
            PluginVendor.GoogleDrive    => "google-drive",
            PluginVendor.GoogleCalendar => "google-calendar",
            PluginVendor.Gmail          => "gmail",
            PluginVendor.Figma          => "figma",
            PluginVendor.GitHub         => "github",
            PluginVendor.Linear         => "linear",
            PluginVendor.Slack          => "slack",
            _                           => "custom",
        };

        public static PluginVendor ParseVendor(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return PluginVendor.Custom;
            var key = raw.Trim().ToLowerInvariant();
            return key switch
            {
                "notion"          => PluginVendor.Notion,
                "google-drive"    => PluginVendor.GoogleDrive,
                "googledrive"     => PluginVendor.GoogleDrive,
                "google-calendar" => PluginVendor.GoogleCalendar,
                "googlecalendar"  => PluginVendor.GoogleCalendar,
                "gmail"           => PluginVendor.Gmail,
                "figma"           => PluginVendor.Figma,
                "github"          => PluginVendor.GitHub,
                "linear"          => PluginVendor.Linear,
                "slack"           => PluginVendor.Slack,
                _                 => PluginVendor.Custom,
            };
        }

        public static string DisplayName(this PluginVendor vendor) => vendor switch
        {
            PluginVendor.Notion         => "Notion",
            PluginVendor.GoogleDrive    => "Google Drive",
            PluginVendor.GoogleCalendar => "Google Calendar",
            PluginVendor.Gmail          => "Gmail",
            PluginVendor.Figma          => "Figma",
            PluginVendor.GitHub         => "GitHub",
            PluginVendor.Linear         => "Linear",
            PluginVendor.Slack          => "Slack",
            _                           => "Custom",
        };

        public static Color DisplayColor(this PluginVendor vendor) => vendor switch
        {
            PluginVendor.Notion         => new Color(0.10f, 0.10f, 0.10f),
            PluginVendor.GoogleDrive    => new Color(0.20f, 0.60f, 0.95f),
            PluginVendor.GoogleCalendar => new Color(0.25f, 0.55f, 0.95f),
            PluginVendor.Gmail          => new Color(0.95f, 0.30f, 0.25f),
            PluginVendor.Figma          => new Color(0.95f, 0.45f, 0.30f),
            PluginVendor.GitHub         => new Color(0.15f, 0.15f, 0.15f),
            PluginVendor.Linear         => new Color(0.40f, 0.40f, 0.95f),
            PluginVendor.Slack          => new Color(0.55f, 0.25f, 0.55f),
            _                           => new Color(0.55f, 0.55f, 0.55f),
        };

        public static string ToSerializedKey(this PluginTransport transport) => transport switch
        {
            PluginTransport.Sse  => "sse",
            PluginTransport.Http => "http",
            _                    => "stdio",
        };

        public static PluginTransport ParseTransport(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return PluginTransport.Stdio;
            return raw.Trim().ToLowerInvariant() switch
            {
                "sse"   => PluginTransport.Sse,
                "http"  => PluginTransport.Http,
                _       => PluginTransport.Stdio,
            };
        }

        public static string ToSerializedKey(this CredentialKind kind) => kind switch
        {
            CredentialKind.OAuth2 => "oauth2",
            CredentialKind.Bearer => "bearer",
            CredentialKind.Custom => "custom",
            _                     => "api-key",
        };

        public static CredentialKind ParseCredentialKind(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return CredentialKind.ApiKey;
            return raw.Trim().ToLowerInvariant() switch
            {
                "oauth2" => CredentialKind.OAuth2,
                "bearer" => CredentialKind.Bearer,
                "custom" => CredentialKind.Custom,
                _        => CredentialKind.ApiKey,
            };
        }
    }
}
