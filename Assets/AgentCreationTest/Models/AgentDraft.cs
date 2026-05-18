using System.Collections.Generic;

namespace AgentCreationTest.Models
{
    // Final, immutable result emitted when the user finishes the wizard.
    public sealed class AgentDraft
    {
        public string Name { get; }
        public string Role { get; }
        public IReadOnlyList<string> Traits { get; }
        public Wardrobe Wardrobe { get; }
        public string ModelId { get; }

        public AgentDraft(string name, string role, IReadOnlyList<string> traits, Wardrobe wardrobe, string modelId)
        {
            Name = name;
            Role = role;
            Traits = traits;
            Wardrobe = wardrobe;
            ModelId = modelId;
        }
    }
}
