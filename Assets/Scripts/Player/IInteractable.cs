namespace Probation.Player
{
    /// <summary>
    /// Anything an intern can look at and press Interact on: a scalpel, a gurney brake, a
    /// breaker switch, a downed colleague. Implementations live with their own systems.
    /// </summary>
    public interface IInteractable
    {
        /// <summary>Verb shown on the prompt, e.g. "Pick up scalpel" or "Release brake".</summary>
        string Prompt { get; }

        bool CanInteract(PlayerInteractor interactor);

        void Interact(PlayerInteractor interactor);
    }
}
