using UnityEngine;
using UnityEditor;
using OpenDesk.Core.Models;

namespace OpenDesk.Editor
{
    /// <summary>
    /// 프리셋 스킬 디스켓 ScriptableObject 5개를 Resources/SkillDisks/에 자동 생성.
    /// 메뉴: OpenDesk > Build Preset Skill Disks
    /// </summary>
    public static class SkillDiskettePresetBuilder
    {
        private const string OutputPath = "Assets/Resources/SkillDisks/";

        [MenuItem("OpenDesk/Build Preset Skill Disks")]
        public static void BuildAll()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Resources"))
                AssetDatabase.CreateFolder("Assets", "Resources");
            if (!AssetDatabase.IsValidFolder("Assets/Resources/SkillDisks"))
                AssetDatabase.CreateFolder("Assets/Resources", "SkillDisks");

            CreatePreset(
                skillId: "code-reviewer",
                displayName: "코드 리뷰어",
                description: "코드 품질, 보안, 성능을 전문적으로 리뷰합니다",
                category: SkillCategory.Development,
                promptContent:
@"당신은 시니어 소프트웨어 엔지니어이자 코드 리뷰 전문가입니다.
사용자가 제공하는 코드를 다음 관점에서 리뷰하세요:

1. 코드 품질: 가독성, 네이밍, 구조, 중복 제거
2. 보안: 인젝션, XSS, 인증/인가 취약점
3. 성능: 불필요한 연산, 메모리 누수, N+1 쿼리
4. 베스트 프랙티스: SOLID 원칙, 디자인 패턴 적용 여부

리뷰 결과는 구체적 코드 라인을 언급하며, 수정 제안과 함께 제공하세요.
심각도를 [Critical], [Warning], [Suggestion]으로 분류하세요.",
                color: new Color(0.2f, 0.8f, 0.4f));

            CreatePreset(
                skillId: "doc-writer",
                displayName: "문서 작성자",
                description: "기술 문서, 보고서, 제안서를 전문적으로 작성합니다",
                category: SkillCategory.Document,
                promptContent:
@"당신은 테크니컬 라이터이자 문서 작성 전문가입니다.
사용자의 요청에 맞는 문서를 작성하세요.

문서 작성 원칙:
1. 명확하고 간결한 문장 사용
2. 논리적인 구조 (제목, 소제목, 단락 구분)
3. 전문 용어 사용 시 필요하면 설명 추가
4. 표, 목록 등을 활용한 정보 구조화
5. 대상 독자 수준에 맞는 톤 조절

지원 문서 유형: 기술 문서, 주간 보고서, 제안서, 회의록 정리, README, API 문서",
                color: new Color(0.3f, 0.5f, 1.0f));

            CreatePreset(
                skillId: "translator",
                displayName: "번역가",
                description: "다국어 번역과 현지화를 수행합니다",
                category: SkillCategory.General,
                promptContent:
@"당신은 전문 번역가입니다.
사용자가 제공하는 텍스트를 요청한 언어로 번역하세요.

번역 원칙:
1. 원문의 의미와 뉘앙스를 정확히 전달
2. 대상 언어의 자연스러운 표현 사용 (직역 지양)
3. 전문 용어는 해당 분야의 표준 번역어 사용
4. 문화적 맥락을 고려한 현지화
5. 번역이 애매한 부분은 원문을 병기

기본 번역 방향: 한국어 <-> 영어 (다른 언어도 가능)
특별한 지시가 없으면 한국어로 번역합니다.",
                color: new Color(0.5f, 0.9f, 1.0f));

            CreatePreset(
                skillId: "data-analyst",
                displayName: "데이터 분석가",
                description: "CSV, JSON 등 데이터를 분석하고 인사이트를 도출합니다",
                category: SkillCategory.Analysis,
                promptContent:
@"당신은 데이터 분석 전문가입니다.
사용자가 제공하는 데이터(CSV, JSON, 텍스트 등)를 분석하세요.

분석 프로세스:
1. 데이터 구조 파악 (컬럼, 행수, 데이터 타입)
2. 기초 통계 (평균, 중앙값, 분포, 이상치)
3. 패턴 및 트렌드 발견
4. 핵심 인사이트 5개 이상 도출
5. 시각화 제안 (어떤 차트가 적합한지)
6. 실행 가능한 액션 아이템 제안

결과는 비전공자도 이해할 수 있도록 쉽게 설명하세요.",
                color: new Color(1.0f, 0.7f, 0.2f));

            CreatePreset(
                skillId: "notion-planner",
                displayName: "Notion 기획자",
                description: "Notion 페이지를 구조화하여 생성합니다",
                category: SkillCategory.ExternalTool,
                promptContent:
@"당신은 Notion 전문가이자 기획 담당자입니다.
사용자가 제공하는 내용을 Notion 페이지로 구조화하여 생성하세요.

작업 방식:
1. 내용을 분석하여 최적의 페이지 구조 설계
2. 제목, 소제목, 단락으로 체계적 구성
3. 핵심 정보는 데이터베이스 또는 테이블로 구조화
4. 액션 아이템은 체크리스트로 변환
5. 관련 내용끼리 그룹핑

Notion MCP 도구를 사용하여 실제 Notion 워크스페이스에 페이지를 생성합니다.
페이지 생성 후 URL을 사용자에게 알려주세요.",
                color: new Color(0.9f, 0.3f, 0.9f),
                mcpServerCommand: "npx @notionhq/notion-mcp-server",
                requiredTokens: new[] { "NOTION_API_TOKEN" });

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[SkillDiskettePresetBuilder] 프리셋 디스켓 5개 생성 완료");
        }

        private static void CreatePreset(
            string skillId,
            string displayName,
            string description,
            SkillCategory category,
            string promptContent,
            Color color,
            string mcpServerCommand = null,
            string[] requiredTokens = null)
        {
            var assetPath = $"{OutputPath}Skill_{skillId.Replace("-", "_")}.asset";

            // 기존 에셋이 있으면 로드하여 업데이트
            var existing = AssetDatabase.LoadAssetAtPath<SkillDiskette.SkillDiskette>(assetPath);
            if (existing != null)
            {
                Debug.Log($"[SkillDiskettePresetBuilder] 이미 존재: {assetPath} (스킵)");
                return;
            }

            var so = ScriptableObject.CreateInstance<SkillDiskette.SkillDiskette>();

            // SerializeField에 직접 접근 (에디터 전용)
            var serialized = new SerializedObject(so);
            serialized.FindProperty("_skillId").stringValue = skillId;
            serialized.FindProperty("_displayName").stringValue = displayName;
            serialized.FindProperty("_description").stringValue = description;
            serialized.FindProperty("_category").enumValueIndex = (int)category;
            serialized.FindProperty("_promptContent").stringValue = promptContent;
            serialized.FindProperty("_color").colorValue = color;
            serialized.FindProperty("_isCustomCrafted").boolValue = false;

            if (!string.IsNullOrEmpty(mcpServerCommand))
                serialized.FindProperty("_mcpServerCommand").stringValue = mcpServerCommand;

            if (requiredTokens != null)
            {
                var tokensProp = serialized.FindProperty("_requiredTokens");
                tokensProp.ClearArray();
                for (int i = 0; i < requiredTokens.Length; i++)
                {
                    tokensProp.InsertArrayElementAtIndex(i);
                    tokensProp.GetArrayElementAtIndex(i).stringValue = requiredTokens[i];
                }
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.CreateAsset(so, assetPath);
            Debug.Log($"[SkillDiskettePresetBuilder] 생성: {assetPath}");
        }
    }
}
