using Probation.Surgery;
using Unity.Netcode;
using UnityEngine;

namespace Probation.Game
{
    public enum WardZoneKind
    {
        /// <summary>Where the living go. This is what the quota counts.</summary>
        Discharge,

        /// <summary>Where the dead go. Counts for nothing, but they cannot stay on the table.</summary>
        Morgue,
    }

    /// <summary>
    /// The end of the line for a patient. Wheel one in and it leaves the ward.
    ///
    /// Discharge being <em>physical</em> is the point: treating a patient is not the same as
    /// getting them out, so the last thirty seconds of a night are a scramble of people shoving
    /// gurneys down a corridor. It also finally gives the heavy-haul system a job that matters.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class WardZone : MonoBehaviour
    {
        [SerializeField] private WardZoneKind kind = WardZoneKind.Discharge;

        private void Reset()
        {
            var box = GetComponent<Collider>();
            if (box != null) box.isTrigger = true;
        }

        private void OnTriggerEnter(Collider other)
        {
            var net = NetworkManager.Singleton;
            if (net == null || !net.IsServer) return;

            var patient = other.GetComponentInParent<Patient>();
            if (patient == null || patient.HasLeft) return;

            var director = ShiftDirector.Instance;

            if (kind == WardZoneKind.Discharge)
            {
                if (patient.IsDead)
                {
                    director?.Announce("That one is dead. It does not go out the front.");
                    return;
                }

                if (!patient.IsTreated)
                {
                    var unfinished = patient.Chart;
                    director?.Announce(unfinished == null || !unfinished.IsWritten
                        ? "Nobody has charted that one."
                        : "You have not finished with that one.");
                    return;
                }

                director?.RecordDischarge();
                director?.Announce("Patient discharged.");

                ChargeForSendingThemHomeUntreated(patient, director);
            }
            else
            {
                if (!patient.IsDead)
                {
                    director?.Announce("That one is still alive.");
                    return;
                }

                director?.Announce("Body received.");
            }

            patient.SendAway();
        }

        /// <summary>
        /// The moment a no-operation chart counts as treated, the obvious play is to chart every
        /// arrival "no operation" and wheel the lot straight out for a free quota.
        ///
        /// That is closed here rather than by refusing the discharge, because refusing it would
        /// be a hard block and this ward never blocks. They walk out, the quota moves, and the
        /// thing nobody took out of them kills them at home. Tonight is satisfied. The week is
        /// the thing that pays, through the hospital's body count - and the review names whoever
        /// wrote the chart, not whoever pushed the trolley.
        /// </summary>
        private static void ChargeForSendingThemHomeUntreated(Patient patient, ShiftDirector director)
        {
            var chart = patient.Chart;
            if (chart == null || !chart.SaysNoOperation) return;

            var condition = patient.Condition;
            if (condition == null || condition.TreatmentFor(patient.Species) == null) return;

            director?.RecordDeath();

            string species = patient.Species != null ? patient.Species.displayName : "patient";
            IncidentLog.Record(chart.ChartedBy, $"sent a {species} home untreated - it came back");
        }
    }
}
