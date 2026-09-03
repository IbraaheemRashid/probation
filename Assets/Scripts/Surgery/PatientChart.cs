using System.Collections.Generic;
using Probation.Game;
using Probation.Player;
using Unity.Netcode;
using UnityEngine;

namespace Probation.Surgery
{
    /// <summary>
    /// The board at the foot of the bed, and the only thing in the game that decides what is
    /// wrong with a patient.
    ///
    /// This exists because diagnosis without a committing act is decoration. If the ward derives
    /// the correct procedure from the condition and hands it to <see cref="Operation"/>, then the
    /// HUD prints the next tool and the next site and following instructions is the whole game -
    /// which is exactly what this project was doing until the chart landed. Somebody has to
    /// decide, out loud, in front of the others, and be wrong sometimes.
    ///
    /// It offers every procedure the night can call for, not the ones that could plausibly be
    /// right for <em>this</em> patient. A shortlist would put the answer in the chart.
    ///
    /// Lives on its own child object rather than on the patient root, and not for tidiness:
    /// PlayerInteractor resolves focus with GetComponentInParent&lt;IInteractable&gt;, and the
    /// patient root is already a Grabbable, which is also an IInteractable. On the root the two
    /// would fight over the prompt and which one won would depend on component order.
    /// </summary>
    public class PatientChart : NetworkBehaviour, IInteractable
    {
        /// <summary>Nobody has written it. Distinct from "written, and it says do not operate".</summary>
        private const int Unwritten = -2;

        /// <summary>Written, and the decision was to leave them alone.</summary>
        private const int NoOperation = -1;

        private readonly NetworkVariable<int> _choice = new(Unwritten);
        private readonly NetworkVariable<ulong> _chartedBy = new(ulong.MaxValue);

        public bool IsWritten => _choice.Value != Unwritten;
        public bool SaysNoOperation => _choice.Value == NoOperation;

        /// <summary>
        /// Who decided. The review blames wrong-procedure harm on this person and not on whoever
        /// was holding the forceps - the surgeon did as they were told.
        /// </summary>
        public ulong ChartedBy => _chartedBy.Value;

        /// <summary>What it currently says, or null for no-operation and for unwritten.</summary>
        public Procedure Choice =>
            _choice.Value >= 0 ? Casebook.Active?.ProcedureAt(_choice.Value) : null;

        private Patient _patient;
        private Operation _operation;

        private void Awake()
        {
            _patient = GetComponentInParent<Patient>();
            _operation = GetComponentInParent<Operation>();
        }

        /// <summary>Wipe it for the next occupant of this bed. Called from Patient.Admit.</summary>
        public void Clear()
        {
            if (!IsServer) return;

            _choice.Value = Unwritten;
            _chartedBy.Value = ulong.MaxValue;
        }

        // ---------------------------------------------------------------- IInteractable

        public string Prompt
        {
            get
            {
                if (_choice.Value == Unwritten) return "Write the chart";
                if (_choice.Value == NoOperation) return "Chart: no operation (change)";

                var current = Choice;
                return current != null ? $"Chart: {current.displayName} (change)" : "Write the chart";
            }
        }

        public bool CanInteract(PlayerInteractor interactor) =>
            _patient != null && !_patient.HasLeft && !_patient.IsDead;

        public void Interact(PlayerInteractor interactor) => CycleRpc();

        // ---------------------------------------------------------------- writing it

        /// <summary>
        /// Cycle to the next option.
        ///
        /// A first pass at the interaction and it shows - picking the third procedure means
        /// pressing Interact three times, which will not survive a playtest with four people
        /// crowding one board. The decision it represents is the part worth getting right first.
        /// </summary>
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void CycleRpc(RpcParams rpc = default)
        {
            ulong clientId = rpc.Receive.SenderClientId;

            if (_patient == null || _patient.HasLeft || _patient.IsDead) return;

            var book = Casebook.Active;
            var director = ShiftDirector.Instance;
            if (book == null || director == null) return;

            var options = book.ChartableOn(director.Day);
            if (options.Count == 0) return;

            // Positions run [0 .. options.Count-1] for procedures, then one past the end for
            // no-operation. Unwritten sits at -1 so the first press lands on the first option.
            int position = PositionOf(book, options);
            position = (position + 1) % (options.Count + 1);

            Procedure chosen = position < options.Count ? options[position] : null;

            _choice.Value = chosen != null ? book.IndexOf(chosen) : NoOperation;
            _chartedBy.Value = clientId;

            // Assign resets step progress, so changing your mind halfway costs the work. That is
            // deliberate: a chart you can swap for free on the last step is a way to try every
            // procedure in turn until one of them sticks.
            _operation?.Assign(chosen);

            IncidentLog.Record(clientId, chosen != null
                ? $"charted a patient for {chosen.displayName}"
                : "charted a patient for no operation");
        }

        private int PositionOf(Casebook book, List<Procedure> options)
        {
            if (_choice.Value == Unwritten) return -1;
            if (_choice.Value == NoOperation) return options.Count;

            var current = book.ProcedureAt(_choice.Value);
            int index = current != null ? options.IndexOf(current) : -1;
            return index;
        }
    }
}
