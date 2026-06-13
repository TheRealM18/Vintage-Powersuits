using System;
using Vintagestory.API.Common;

namespace VEPowersuit.Systems
{
    /// <summary>
    /// ============================================================
    ///  *** THE ONE FILE YOU MUST VERIFY MANUALLY ***
    /// ============================================================
    /// Vintage Engineering exposes its own energy interfaces. I do NOT have
    /// VE's exact API memorized, so this adapter is written against an
    /// ASSUMED interface. Open the VE .dll in your IDE (or dnSpy/ILSpy),
    /// find how VE charging stations transfer power into items, and wire
    /// the two methods below to the real calls.
    ///
    /// What you are looking for in the VE assembly, typically named something
    /// like VintageEngineering.Electrical.* :
    ///   - An interface implemented by items that can hold/receive power,
    ///     e.g. IElectricalItem / IEnergyStorageItem with a method like
    ///     ReceivePower(float watts, float dt, bool simulate).
    ///   - Or an ItemSlot/attribute convention the charging station writes to.
    ///
    /// Two integration strategies (pick one):
    ///
    ///  A) IMPLEMENT VE's interface on the armor item directly. Then VE's
    ///     charging station will push power in for free. This adapter just
    ///     reads the resulting energy back out for our own use.
    ///
    ///  B) SELF-CONTAINED. Keep all energy in our own attribute (this is what
    ///     the rest of the mod does already) and add a custom block / recipe
    ///     to charge it, OR convert from VE via VE's public API here.
    ///
    /// The rest of the mod (flight, modules, HUD, keybind, energy store) is
    /// written against the stable core VS API and does NOT depend on this file
    /// being correct — it will compile and run; the armor just won't pull
    /// power from VE until you finish this adapter.
    /// </summary>
    public static class VEPowerAdapter
    {
        // Set true once you've confirmed the VE calls below are correct.
        public const bool VEIntegrationWired = false;

        /// <summary>
        /// Try to pull up to maxEnergy units from a VE power source represented
        /// by the given slot/itemstack context. Return the amount actually moved.
        ///
        /// Replace the body with the real VE call. Example shape ONLY:
        ///
        ///   if (itemStack.Collectible is IElectricalItem ve)
        ///       return ve.ExtractPower(maxEnergy, dt, simulate: false);
        /// </summary>
        public static int TryDrawFromVE(IWorldAccessor world, ItemStack source, int maxEnergy)
        {
            if (!VEIntegrationWired) return 0;
            // TODO: real VE call here.
            return 0;
        }

        /// <summary>
        /// Hook for VE's charging station to push power INTO the armor.
        /// If you choose strategy (A), you implement VE's interface on
        /// ItemVEPowersuit and forward here.
        /// </summary>
        public static int ReceiveFromVE(ItemStack armor, int offered)
        {
            int cur = EnergyStore.GetEnergy(armor);
            int max = EnergyStore.GetMaxEnergy(armor);
            int room = Math.Max(0, max - cur);
            int taken = Math.Min(room, offered);
            EnergyStore.SetEnergy(armor, cur + taken);
            return taken;
        }
    }
}
