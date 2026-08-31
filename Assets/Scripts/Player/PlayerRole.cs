using Unity.Netcode;
using UnityEngine;

namespace Probation.Player
{
    /// <summary>
    /// What this intern specialised in for the current shift.
    ///
    /// Specialism grants <em>information nobody else has</em>, never a stat advantage. Stat
    /// classes make one player the good one; information classes force everyone to talk, which
    /// is the entire reason to build a proximity chat game.
    ///
    /// Chosen per shift at the locker, never permanent, never assigned by the game - two people
    /// can pick the same one, and the resulting "we have no anaesthetist" panic is the point.
    /// </summary>
    public enum Specialism
    {
        None,

        /// <summary>Sees pressure and flow. Only they can close a bleed permanently.</summary>
        Vascular,

        /// <summary>Sees true consciousness and pain. Everyone else sees a machine that beeps.</summary>
        Anaesthesia,

        /// <summary>Sees stress fractures. Opens patients nobody else can open.</summary>
        Exostructure,

        /// <summary>Sees the species rules - what it reacts to, and what is actually wrong.</summary>
        Xenobiology,
    }

    public class PlayerRole : NetworkBehaviour
    {
        private readonly NetworkVariable<Specialism> _specialism =
            new(Specialism.None, NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Owner);

        public Specialism Specialism => _specialism.Value;

        /// <summary>Owner-writable: you pick your own at the locker, nobody assigns it to you.</summary>
        public void Choose(Specialism specialism)
        {
            if (IsOwner) _specialism.Value = specialism;
        }

        public bool CanSeeDiagnosis => _specialism.Value == Specialism.Xenobiology;
        public bool CanSeePainState => _specialism.Value == Specialism.Anaesthesia;
        public bool CanCloseBleeds => _specialism.Value == Specialism.Vascular;
        public bool CanOpenShells => _specialism.Value == Specialism.Exostructure;
    }
}
