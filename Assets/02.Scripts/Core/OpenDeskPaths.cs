using System;
using System.IO;
using System.Runtime.InteropServices;

namespace OpenDesk.Core
{
    /// <summary>
    /// OpenDesk 네이티브 사용자 디렉토리 경로 모음.
    /// 2026-04-27: ~/.openclaw → ~/.opendesk 마이그레이션 진입점.
    /// 새 코드는 이 클래스만 참조할 것.
    /// </summary>
    public static class OpenDeskPaths
    {
        public const string DirName = "opendesk";

        public static string Base
        {
            get
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    return Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        DirName);

                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "." + DirName);
            }
        }

        public static string Skills => Path.Combine(Base, "skills");

        public static string Plugins => Path.Combine(Base, "plugins");

        public static string ChannelsConfig => Path.Combine(Base, "channels.yaml");

        public static string SkillDir(string skillName) => Path.Combine(Skills, skillName);

        public static string PluginDir(string pluginId) => Path.Combine(Plugins, pluginId);

        /// <summary>
        /// 원격 catalog.json 의 로컬 캐시 경로.
        /// RemoteSkillRegistry 가 24h TTL + ETag 검사 후 갱신.
        /// </summary>
        public static string CatalogCache => Path.Combine(Skills, "catalog-cache.json");

        /// <summary>
        /// 원격 plugins-catalog.json 의 로컬 캐시 경로.
        /// </summary>
        public static string PluginCatalogCache => Path.Combine(Plugins, "catalog-cache.json");

        /// <summary>
        /// 스킬 zip 다운로드 임시 디렉토리. 검증 후 atomic move 로 Skills/{id}/ 로 이동.
        /// </summary>
        public static string SkillsTmp => Path.Combine(Skills, ".tmp");

        /// <summary>
        /// 영속 게임/앱 데이터 저장 루트 (LocalGameDataRepository).
        /// </summary>
        public static string GameData => Path.Combine(Base, "data");

        /// <summary>
        /// Claude Code CLI 격리 설정 디렉토리. CLAUDE_CONFIG_DIR 환경변수로 미들웨어와 자식 subprocess 에 주입.
        /// 글로벌 ~/.claude/ 와 완전히 분리하기 위함이며, 첫 사용 시 별도 'claude login' 또는
        /// ANTHROPIC_API_KEY 환경변수가 필요하다.
        /// </summary>
        public static string ClaudeConfigDir => Path.Combine(Base, "claude-cli");
    }
}
