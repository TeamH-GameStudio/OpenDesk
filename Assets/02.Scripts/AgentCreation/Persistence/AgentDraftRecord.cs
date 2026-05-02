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
        }

        public static AgentDraftRecord FromDraft(AgentDraft draft, WardrobeCatalogSO catalog)
        {
            if (draft == null) throw new ArgumentNullException(nameof(draft));

            return new AgentDraftRecord
            {
                id = "agent_" + Guid.NewGuid().ToString("N").Substring(0, 12),
                name = draft.Name,
                role = draft.Role,
                traits = draft.Traits != null ? new List<string>(draft.Traits) : new List<string>(),
                modelId = draft.ModelId,
                wardrobe = new WardrobeRecord
                {
                    skin   = ResolveId(catalog, WardrobePart.Skin,   draft.Wardrobe.Skin),
                    hair   = ResolveId(catalog, WardrobePart.Hair,   draft.Wardrobe.Hair),
                    eyes   = ResolveId(catalog, WardrobePart.Eyes,   draft.Wardrobe.Eyes),
                    mouth  = ResolveId(catalog, WardrobePart.Mouth,  draft.Wardrobe.Mouth),
                    top    = ResolveId(catalog, WardrobePart.Top,    draft.Wardrobe.Top),
                    bottom = ResolveId(catalog, WardrobePart.Bottom, draft.Wardrobe.Bottom),
                    shoes  = ResolveId(catalog, WardrobePart.Shoes,  draft.Wardrobe.Shoes),
                },
                createdAt = DateTime.UtcNow.ToString("o"),
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
