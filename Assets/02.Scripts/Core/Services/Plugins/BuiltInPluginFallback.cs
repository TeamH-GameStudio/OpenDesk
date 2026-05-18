using System;
using System.Collections.Generic;
using OpenDesk.Core.Models.Plugins;

namespace OpenDesk.Core.Services.Plugins
{
    /// <summary>
    /// PluginCatalog 의 빌트인 seed. SkillMarketView v4 통합 뷰가 "플러그인" 섹션에 노출할 도구 목록.
    /// Claude Design v4 §A 의 "도구 느낌" 카드들 — Gmail / Calendar / Notion / Slack / Linear / Web Search /
    /// File Manager 등. capabilities 는 route_capability 가 매칭에 사용.
    ///
    /// 원격 PluginRegistry 가 도입되면 이 seed 는 fallback 역할로 축소.
    /// 데이터 분류는 일관성 위해 BuiltInFallback(스킬) 과 한 쌍으로 관리한다.
    /// </summary>
    internal static class BuiltInPluginFallback
    {
        public static IReadOnlyList<PluginDescriptor> Build()
        {
            return new List<PluginDescriptor>
            {
                Plugin(
                    id: "gmail",
                    name: "Gmail",
                    desc: "이메일 읽기 · 답장 · 분류",
                    vendor: PluginVendor.Gmail,
                    capabilities: new[] { "mail.read", "mail.draft", "mail.send" }),

                Plugin(
                    id: "google-calendar",
                    name: "Google Calendar",
                    desc: "일정 추가 · 조회 · 알림",
                    vendor: PluginVendor.GoogleCalendar,
                    capabilities: new[] { "calendar.read", "calendar.create_event", "calendar.update_event" }),

                Plugin(
                    id: "outlook-calendar",
                    name: "Outlook Calendar",
                    desc: "Outlook 일정 추가 · 조회",
                    vendor: PluginVendor.Custom,
                    capabilities: new[] { "calendar.read", "calendar.create_event" }),

                Plugin(
                    id: "notion",
                    name: "Notion",
                    desc: "페이지 읽기 · 생성 · 갱신",
                    vendor: PluginVendor.Notion,
                    capabilities: new[] { "doc.read", "doc.create", "doc.update" }),

                Plugin(
                    id: "slack",
                    name: "Slack",
                    desc: "채널 메시지 · DM 송수신",
                    vendor: PluginVendor.Slack,
                    capabilities: new[] { "chat.read", "chat.send" }),

                Plugin(
                    id: "linear",
                    name: "Linear",
                    desc: "이슈 생성 · 추적 · 코멘트",
                    vendor: PluginVendor.Linear,
                    capabilities: new[] { "issue.read", "issue.create", "issue.update" }),

                Plugin(
                    id: "web-search",
                    name: "Web Search",
                    desc: "웹 검색 · 결과 요약",
                    vendor: PluginVendor.Custom,
                    capabilities: new[] { "web.search", "web.fetch" }),

                Plugin(
                    id: "file-manager",
                    name: "File Manager",
                    desc: "파일 읽기 · 쓰기 · 검색",
                    vendor: PluginVendor.Custom,
                    capabilities: new[] { "file.read", "file.write", "file.delete" }),
            };
        }

        private static PluginDescriptor Plugin(
            string id, string name, string desc, PluginVendor vendor, string[] capabilities)
        {
            return new PluginDescriptor(
                Id: id,
                DisplayName: name,
                Description: desc,
                Vendor: vendor,
                Transport: PluginTransport.Stdio,
                Version: "1.0.0",
                IconUrl: string.Empty,
                ServerSpec: McpServerSpec.Empty,
                RequiredCredentials: Array.Empty<CredentialRequirement>(),
                DownloadUrl: string.Empty,
                Checksum: string.Empty,
                PublishedAt: DateTime.MinValue,
                IsInstalled: false,
                InstallPath: string.Empty,
                Capabilities: capabilities ?? Array.Empty<string>()
            );
        }
    }
}
