using System;
using System.Collections.Generic;

namespace OpenDesk.Onboarding.Models
{
    /// <summary>
    /// OpenDesk가 설치한 항목 기록 — 롤백 시 참조
    /// JSON으로 직렬화하여 로컬 파일에 저장
    /// </summary>
    [Serializable]
    public class InstallationRecord
    {
        public List<InstalledItem> Items = new();
        public string CreatedAt = "";
        public string LastUpdated = "";
    }

    [Serializable]
    public class InstalledItem
    {
        /// <summary>고유 키 (nodejs, nodejs_nvm, nvm, wsl2, openclaw, openclaw_daemon)</summary>
        public string Id = "";

        /// <summary>사용자에게 보여줄 이름</summary>
        public string DisplayName = "";

        /// <summary>설치 전 상태 (예: "v20.14.0", "미설치", "활성화됨")</summary>
        public string PreviousState = "";

        /// <summary>설치 후 상태 (예: "v24.1.0", "설치됨")</summary>
        public string InstalledState = "";

        /// <summary>설치 방법 (msi, nvm, npm, wsl_install, script)</summary>
        public string Method = "";

        /// <summary>설치 경로 (롤백 시 참조)</summary>
        public string InstallPath = "";

        /// <summary>설치 시각</summary>
        public string InstalledAt = "";

        /// <summary>롤백 가능 여부</summary>
        public bool CanRollback = true;

        /// <summary>롤백 완료 여부</summary>
        public bool RolledBack = false;

        /// <summary>롤백 시 실행할 명령 (자동 생성)</summary>
        public string RollbackCommand = "";

        /// <summary>비전공자 친화 설명</summary>
        public string RollbackDescription = "";
    }
}
