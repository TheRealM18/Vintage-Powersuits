using System;
using VEPowersuit.Items;
using Vintagestory.API.Common;

namespace VEPowersuit.Systems
{
    /// <summary>
    /// Orchestrates one charge tick for a <see cref="ItemVEPowersuit"/> sitting
    /// inside a Vintage Engineering charger.
    ///
    /// This class contains ONLY our mod's logic. It never uses reflection and
    /// never references any VE type directly. Everything it needs from the
    /// charger is provided through <see cref="VEChargerAccessor"/>.
    ///
    /// Flow:
    ///   1. The Harmony patch (see <see cref="VEChargerPatch"/>) detects that
    ///      the item in the charger is one of ours.
    ///   2. It creates a <see cref="VEChargerAccessor"/> for the live charger
    ///      instance and calls <see cref="TryCharge"/>.
    ///   3. This class uses <see cref="EnergyStore"/> / <see cref="VEPowerAdapter"/>
    ///      to push EU into the suit's own energy store.
    ///   4. If power was moved, it instructs the accessor to debit the charger
    ///      and set the state machine accordingly.
    ///   5. Returns <c>true</c> if we fully handled the tick (Harmony should
    ///      skip VE), <c>false</c> if VE should run normally.
    /// </summary>
    public static class SuitChargeSession
    {
        /// <summary>
        /// Attempt to charge the power suit that is in the charger this tick.
        /// </summary>
        /// <param name="accessor">
        ///     Thin wrapper around the live <c>BELVCharger</c> — provides power
        ///     readings and state control without leaking VE types.
        /// </param>
        /// <param name="stack">The item stack confirmed to be an ItemVEPowersuit.</param>
        /// <param name="suit">The suit's Item (cast already done by the patch).</param>
        /// <param name="dt">Delta time for this sim tick (seconds).</param>
        /// <returns>
        ///     <c>true</c>  — we handled it; Harmony should suppress VE's method.<br/>
        ///     <c>false</c> — something unexpected; let VE run.
        /// </returns>
        public static bool TryCharge(
            VEChargerAccessor accessor,
            ItemStack stack,
            ItemVEPowersuit suit,
            float dt)
        {
            // ── How much can the charger deliver? ─────────────────────────
            ulong rated   = accessor.GetRatedPower(dt);
            ulong stored  = accessor.GetCurrentPower();

            if (rated == 0 || stored < rated)
            {
                // Not enough juice this tick — let VE's own OnSimTick run its
                // normal pause/recovery cycle (including its bouncer-based
                // re-check), instead of permanently parking the charger in
                // "Paused"/"Waiting" with no path back out.
                return false;
            }

            // ── Bind the stack so the IChargeableItem getters resolve ─────
            // ItemVEPowersuit is a singleton Item shared by all stacks in the
            // world, so its parameterless getters need to know WHICH stack is
            // being serviced right now. BindChargingStack sets a ThreadStatic
            // context for the duration of this block.
            using (ItemVEPowersuit.BindChargingStack(stack))
            {
                ulong current = suit.CurrentPower;
                ulong max     = suit.MaxPower;

                if (current >= max)
                {
                    // Suit is full — keep the charger paused.
                    accessor.SetStatePaused();
                    return true;
                }

                accessor.SetStateOn();

                // ── How much does the suit want this tick? ─────────────────
                // RatedPower(dt, isInsert:true) respects the per-second cap.
                ulong wantByItem = suit.RatedPower(dt, isInsert: true);

                // If the suit's rate is 0 (no cap), use the charger's full rate.
                ulong toOffer = wantByItem == 0 ? rated : Math.Min(rated, wantByItem);

                // ── Push power into the suit's EU store ───────────────────
                // ReceivePower returns the LEFTOVER that didn't fit.
                ulong leftover = suit.ReceivePower(toOffer, dt, simulate: false);
                ulong used     = toOffer - leftover;

                // ── Debit the charger ─────────────────────────────────────
                if (used > 0)
                    accessor.DebitPower(used);
            }

            return true; // we fully handled this tick
        }
    }
}