using System;
using System.Collections.Generic;
using AgentCreationTest.Models;
using OpenDesk.Characters.Wardrobe;

namespace OpenDesk.AgentCreation.Persistence
{
    // JSON-friendly snapshot of an AgentDraft.
    //
    // Wardrobe is stored by stable ID rather than index, so reordering catalogue
    // options later does not silently re-skin existing agents. JsonUtility-
    // compatible (public fields, [Serializable]).
    [Serializable]
    public sealed class AgentDraftRecord
    {
        public string id;
        public string name;
        public string role;
        public List<string> traits;
        public string modelId;
        public WardrobeRecord wardrobe;
        public string createdAt;

        // Added 2026-05-14 — fields that the wizard does not yet populate but the
        // runtime needs in order to make AgentDraftRecord the single source of
        // truth. JsonUtility tolerates missing fields on load, so existing files
        // remain compatible.
        public string tone;       // free-text or AgentTone enum name; null → fallback
        public string updatedAt;  // ISO-8601; null until first edit
        public string soulBlock;  // optional Haiku-generated soul prompt cache

        [Serializable]
        public sealed class WardrobeRecord
        {
            public string skin;
            public string hair;
            public string eyes;
            public string mouth;
            public string top;
            public string bottom;
            public string shoes;

            // 머리 색상 — "#RRGGBB" 또는 "#RRGGBBAA" hex. 빈 문자열/null 이면 WardrobeApplier
            // 의 기본 색상으로 폴백한다. WardrobeOutfit (옵션 ID 슬롯) 과 별도 채널로 보관:
            // 색상은 카탈로그 옵션이 아니라 자유 RGB 픽이므로 Outfit 모델을 오염시키지 않기 위함.
            public string hairColor;
        }

        public static AgentDraftRecord FromDraft(AgentDraft draft, WardrobeCatalogSO catalog, string existingId = null)
        {
            if (draft == null) throw new ArgumentNullException(nameof(draft));

            var now = DateTime.UtcNow.ToString("o");
            var isNew = string.IsNullOrWhiteSpace(existingId);

            return new AgentDraftRecord
            {
                id = isNew
                    ? "agent_" + Guid.NewGuid().ToString("N").Substring(0, 12)
                    : existingId,
                name = draft.Name,
                role = draft.Role,
                traits = draft.Traits != null ? new List<string>(draft.Traits) : new List<string>(),
                modelId = draft.ModelId,
                wardrobe = new WardrobeRecord
                {
                    skin      = ResolveId(catalog, WardrobePart.Skin,   draft.Wardrobe.Skin),
                    hair      = ResolveId(catalog, WardrobePart.Hair,   draft.Wardrobe.Hair),
                    eyes      = ResolveId(catalog, WardrobePart.Eyes,   draft.Wardrobe.Eyes),
                    mouth     = ResolveId(catalog, WardrobePart.Mouth,  draft.Wardrobe.Mouth),
                    top       = ResolveId(catalog, WardrobePart.Top,    draft.Wardrobe.Top),
                    bottom    = ResolveId(catalog, WardrobePart.Bottom, draft.Wardrobe.Bottom),
                    shoes     = ResolveId(catalog, WardrobePart.Shoes,  draft.Wardrobe.Shoes),
                    hairColor = draft.Wardrobe.HairColor,
                },
                // 편집 흐름이 도입되면 호출자가 원본 createdAt 을 별도 인자로 보존해야 한다.
                // 현재 위저드 흐름은 신규 생성 전용이므로 항상 now 로 채운다.
                createdAt = now,
                updatedAt = now,
                tone = null,
                soulBlock = null,
            };
        }

        private static string ResolveId(WardrobeCatalogSO catalog, WardrobePart part, int index)
        {
            if (catalog == null) return null;
            var option = catalog.Resolve(part, index);
            return option != null ? option.Id : null;
        }
    }
}
