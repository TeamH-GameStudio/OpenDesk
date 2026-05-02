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

        public static string ChannelsConfig => Path.Combine(Base, "channels.yaml");

        public static string SkillDir(string skillName) => Path.Combine(Skills, skillName);

        /// <summary>
        /// 영속 게임/앱 데이터 저장 루트 (LocalGameDataRepository).
        /// </summary>
        public static string GameData => Path.Combine(Base, "data");
    }
}
