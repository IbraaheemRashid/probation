using Probation.Game;
using Probation.Player;
using Unity.Netcode;
using UnityEngine;

namespace Probation.Surgery
{
    /// <summary>
    /// Asking a patient what happened to them.
    ///
    /// The third diagnostic channel, and the only one that can lie to you. The scanner reports
    /// physical fact and the species page reports general rules; this reports <em>testimony</em>,
    /// which is a different kind of evidence and is worth having precisely because it can
    /// disagree with the other two. A patient who says the lump has been there since they were
    /// born, on a species where that is normal anatomy, is telling you not to cut. A patient who
    /// says it moved last night is telling you something the scanner has not caught yet.
    ///
    /// The cost is the thing that makes it a decision rather than a free lookup:
    ///
    ///   <b>They stop talking the moment you put them under, and every procedure begins by
    ///   putting them under.</b>
    ///
    /// So the interview happens before the operation or not at all, it happens while they are
    /// frightened and in pain, and every question costs seconds of a night that has a clock on
    /// it. Sedating early is comfortable and blind; leaving them awake to keep asking means
    /// operating on somebody who can feel it, which the ward already punishes.
    ///
    /// Host-authoritative. Which questions have been asked replicates, so four people crowding
    /// one bed cannot each ask the same thing and get a fresh answer.
    /// </summary>
    public class PatientInterview : NetworkBehaviour, IInteractable
    {
        /// <summary>How many things a patient will put up with being asked. Five, like the ID desk.</summary>
        public const int Questions = 5;

        private readonly NetworkVariable<int> _asked = new();

        private Patient _patient;

        private void Awake() => _patient = GetComponentInParent<Patient>();

        /// <summary>Whether question <paramref name="index"/> has been put to them yet.</summary>
        public bool Asked(int index) => (_asked.Value & (1 << index)) != 0;

        public int AskedCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < Questions; i++) if (Asked(i)) n++;
                return n;
            }
        }

        /// <summary>
        /// Can they still answer?
        ///
        /// Unconscious is the obvious one. Dead is the grim one. Critical is the interesting one:
        /// somebody whose rate is through the roof has stopped being able to hold a conversation,
        /// which quietly puts a clock on the interview that is not the shift clock.
        /// </summary>
        public bool CanTalk =>
            _patient != null && !_patient.HasLeft && !_patient.IsDead
            && _patient.IsConscious && _patient.State != PatientState.Critical;

        /// <summary>Reset for the next occupant of this bed. Called from Patient.Admit.</summary>
        public void Clear()
        {
            if (IsServer) _asked.Value = 0;
        }

        // ---------------------------------------------------------------- IInteractable

        public string Prompt
        {
            get
            {
                if (_patient != null && _patient.IsDead) return "";
                if (_patient != null && !_patient.IsConscious) return "They are under";
                if (!CanTalk) return "They cannot answer";

                int left = Questions - AskedCount;
                return left <= 0 ? "They have nothing else to tell you" : $"Ask them something ({left} left)";
            }
        }

        public bool CanInteract(PlayerInteractor interactor) => CanTalk && AskedCount < Questions;

        public void Interact(PlayerInteractor interactor) => AskRpc();

        /// <summary>
        /// Ask the next thing they have not been asked.
        ///
        /// Deliberately not a menu of questions to choose from. Choosing which question to ask is
        /// a puzzle-game verb, and it would put a list on screen in a game whose whole interface
        /// rule is that information lives on objects and in people's heads. You ask them what you
        /// have not asked them yet, and you decide when to stop.
        /// </summary>
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void AskRpc(RpcParams rpc = default)
        {
            if (!CanTalk) return;

            int next = -1;
            for (int i = 0; i < Questions; i++)
                if (!Asked(i)) { next = i; break; }

            if (next < 0) return;

            _asked.Value |= 1 << next;

            var book = Casebook.Active;
            string question = book != null ? book.QuestionAt(next) : "...";
            string answer = Answer(next);

            SayRpc(question, answer);
        }

        /// <summary>
        /// Out loud, to everybody.
        ///
        /// The answer goes to the whole ward rather than to whoever asked, and that is the point:
        /// the person at the bedside and the person at the book are different people, and this is
        /// how the second one hears it without walking over.
        /// </summary>
        [Rpc(SendTo.ClientsAndHost)]
        private void SayRpc(string question, string answer)
        {
            ShiftDirector.Instance?.Announce($"\"{question}\"  -  \"{answer}\"");
        }

        /// <summary>
        /// What they say.
        ///
        /// Their condition answers first, because that is what is actually wrong with them. If it
        /// has nothing to say about that question the species answers instead, which is how a
        /// Thoracid asked whether the mass moves says "it beats, it always has" - a true answer,
        /// freely given, that will kill them if you take it at face value without knowing what a
        /// Thoracid is.
        /// </summary>
        private string Answer(int index)
        {
            var condition = _patient != null ? _patient.Condition : null;
            var species = _patient != null ? _patient.Species : null;

            string fromCondition = condition != null ? Line(condition.testimony, index) : null;
            if (!string.IsNullOrEmpty(fromCondition)) return fromCondition;

            string fromSpecies = species != null ? Line(species.testimony, index) : null;
            if (!string.IsNullOrEmpty(fromSpecies)) return fromSpecies;

            return "I do not know.";
        }

        private static string Line(string[] lines, int index) =>
            lines != null && index < lines.Length ? lines[index] : null;
    }
}
