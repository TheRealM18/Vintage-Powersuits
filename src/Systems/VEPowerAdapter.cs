using System;
using Vintagestory.API.Common;

namespace VEPowersuit.Systems
{
    /// <summary>
    /// Bridge between the suit's own EU energy store and Vintage Engineering's
    /// electrical API.
    ///
    /// VE (FlexibleGames/VintageEngineering) charges items two ways, decided by
    /// its LV/MV/HV charger block entity (see BELVCharger.OnSimTick):
    ///
    ///   1. INTERFACE ROUTE - the item's Collectible implements
    ///      VintageEngineering.Electrical.IChargeableItem. The charger reads
    ///      CurrentPower / MaxPower / RatedPower(dt) and calls
    ///      ReceivePower(offered, dt, simulate) on it.
    ///
    ///   2. DURABILITY ROUTE - the item carries the boolean attribute
    ///      "chargable": true. The charger then tops up the vanilla durability
    ///      attribute using powerperdurability, with NO interface needed.
    ///
    /// ItemVEPowersuit implements IChargeableItem (route 1). The methods below
    /// are the actual VE-facing power math, kept here so the energy semantics
    /// live in one place. Energy unit ("EU") == VE power unit; tune module drain
    /// in ModuleRegistry to taste.
    /// </summary>
    public static class VEPowerAdapter
    {
        // VE integration is now implemented against the real IChargeableItem
        // interface (FlexibleGames/VintageEngineering, 1.22.x). The tooltip
        // warning keys off this.
        public static readonly bool VEIntegrationWired = true;

        /// <summary>
        /// Power this stack can accept this tick, rated to its per-second cap.
        /// Mirrors the contract VE's charger expects from RatedPower(dt, isInsert:true):
        /// never larger than remaining capacity, never larger than maxPPS*dt.
        /// </summary>
        public static ulong RatedReceive(ItemStack stack, float dt)
        {
            if (stack == null) return 0;
            ulong room = (ulong)Math.Max(0, EnergyStore.GetMaxEnergy(stack) - EnergyStore.GetEnergy(stack));
            ulong pps = (ulong)Math.Max(0, EnergyStore.GetMaxPPS(stack));
            ulong tickCap = pps == 0 ? room : (ulong)Math.Round(pps * (double)dt);
            if (tickCap == 0) tickCap = room; // PPS of 0 == no per-tick limit
            return Math.Min(room, tickCap);
        }

        /// <summary>
        /// Push power offered by VE into the suit. Returns the LEFTOVER that did
        /// not fit (0 if all consumed) - exactly the contract VE's charger uses
        /// (it does: electricpower -= (offered - leftover)).
        /// </summary>
        public static ulong ReceiveFromVE(ItemStack stack, ulong powerOffered, float dt, bool simulate)
        {
            if (stack == null) return powerOffered;

            ulong canTake = RatedReceive(stack, dt);
            if (canTake == 0) return powerOffered;

            ulong taken = Math.Min(canTake, powerOffered);
            if (!simulate)
            {
                int cur = EnergyStore.GetEnergy(stack);
                long target = cur + (long)taken;
                if (target > int.MaxValue) target = int.MaxValue;
                EnergyStore.SetEnergy(stack, (int)target);
            }
            return powerOffered - taken;
        }

        /// <summary>
        /// Pull power OUT of the suit. Returns the UNFULFILLED remainder of
        /// powerWanted (0 if fully satisfied) - VE's ExtractPower contract.
        /// </summary>
        public static ulong ExtractToVE(ItemStack stack, ulong powerWanted, float dt, bool simulate)
        {
            if (stack == null) return powerWanted;

            ulong have = (ulong)Math.Max(0, EnergyStore.GetEnergy(stack));
            ulong pps = (ulong)Math.Max(0, EnergyStore.GetMaxPPS(stack));
            ulong tickCap = pps == 0 ? have : (ulong)Math.Round(pps * (double)dt);
            if (tickCap == 0) tickCap = have;

            ulong giveable = Math.Min(have, tickCap);
            ulong given = Math.Min(giveable, powerWanted);
            if (!simulate && given > 0)
                EnergyStore.SetEnergy(stack, EnergyStore.GetEnergy(stack) - (int)given);

            return powerWanted - given;
        }
    }
}
