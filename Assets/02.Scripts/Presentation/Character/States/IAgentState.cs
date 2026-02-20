namespace OpenDesk.Presentation.Character.States
{
    public interface IAgentState
    {
        string Name { get; }
        void Enter();
        void Update(float deltaTime);
        void Exit();
    }
}
