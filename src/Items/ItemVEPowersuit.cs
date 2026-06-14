using System;
using System.Text;
using VEPowersuit.Systems;
using VEPowersuit.Modules;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Util;
using VintageEngineering.Electrical;

namespace VEPowersuit.Items
{
    /// <summary>
    /// Power-armor item. Implements Vintage Engineering's IChargeableItem so VE
    /// chargers (LV/MV/HV) can push power straight into the suit's energy store.
    ///
    /// PER-STACK NOTE: IChargeableItem's getters carry no ItemStack, but a single
    /// Item instance is shared by every stack in the world. VE's charger block
    /// entity (BELVCharger.OnSimTick) reads CurrentPower / MaxPower / RatedPower
    /// and then calls ReceivePower for ONE slot, synchronously, within a single
    /// tick. We make that correct by binding the stack VE is about to service to
    /// a [ThreadStatic] context (via the VEChargerPatch Harmony patch) for the
    /// duration of that read/charge sequence. "chargable" is set to true in the
    /// itemtype JSON (as a flag), but VEChargerPatch fully takes over the charger
    /// tick for our suit and routes power through this IChargeableItem interface
    /// into the EU store — so VE's durability-charging path is never used for us.
    /// The EU store remains the single source of truth for the suit's power.
    /// </summary>
    public class ItemVEPowersuit : Item, IChargeableItem
    {
        public bool IsCore => Attributes?["isCore"]?.AsBool(false) ?? false;

        // The stack VE is currently charging on this thread. Bound by
        // BindChargingStack() (call site: see VEChargeContext below) so the
        // parameterless IChargeableItem getters resolve to the right stack.
        [ThreadStatic] private static ItemStack _veActiveStack;

        /// <summary>
        /// Bind the stack VE is about to charge so the IChargeableItem members
        /// below resolve against it. Returns a disposable scope that clears the
        /// binding. Called by VEChargerPatch around VE's charger tick.
        /// </summary>
        public static VEChargeContext BindChargingStack(ItemStack stack)
            => new VEChargeContext(stack, prev => _veActiveStack = prev, _veActiveStack, s => _veActiveStack = s);

        private static ItemStack Active => _veActiveStack;

        public override void OnLoaded(ICoreAPI api)
        {
            base.OnLoaded(api);
        }

        // ---------------- IChargeableItem ----------------

        public ulong MaxPower
            => Active == null ? 0UL : (ulong)Math.Max(0, EnergyStore.GetMaxEnergy(Active));

        public ulong CurrentPower
            => Active == null ? 0UL : (ulong)Math.Max(0, EnergyStore.GetEnergy(Active));

        public ulong MaxPPS
            => Active == null ? 0UL : (ulong)Math.Max(0, EnergyStore.GetMaxPPS(Active));

        // The suit only consumes power for its own modules; it does not feed the
        // VE grid back. Flip CanExtractPower to true (and the JSON) if you want a
        // discharge station to pull from it.
        public bool CanExtractPower => false;
        public bool CanReceivePower => true;

        public ulong RatedPower(float dt, bool isInsert = false)
        {
            if (Active == null) return 0;
            return isInsert
                ? VEPowerAdapter.RatedReceive(Active, dt)
                : 0; // not an extractable source
        }

        public ulong ReceivePower(ulong powerOffered, float dt, bool simulate = false)
            => Active == null
                ? powerOffered
                : VEPowerAdapter.ReceiveFromVE(Active, powerOffered, dt, simulate);

        public ulong ExtractPower(ulong powerWanted, float dt, bool simulate = false)
            => Active == null
                ? powerWanted
                : VEPowerAdapter.ExtractToVE(Active, powerWanted, dt, simulate);

        public void CheatPower(bool drain = false)
        {
            if (Active == null) return;
            EnergyStore.SetEnergy(Active, drain ? 0 : EnergyStore.GetMaxEnergy(Active));
        }

        // ---------------- crafting / tooltips ----------------

        public override void OnCreatedByCrafting(ItemSlot[] allInputslots, ItemSlot outputSlot,
            IRecipeBase byRecipe)
        {
            base.OnCreatedByCrafting(allInputslots, outputSlot, byRecipe);

            var stack = outputSlot.Itemstack;
            if (stack == null) return;

            int max = Attributes?["maxEnergy"]?.AsInt(100000) ?? 100000;
            EnergyStore.SetMaxEnergy(stack, max);

            int pps = Attributes?["maxPPS"]?.AsInt(2000) ?? 2000;
            EnergyStore.SetMaxPPS(stack, pps);

            var defaultsToken = Attributes?["defaultModules"];
            string[] defaults = defaultsToken != null && defaultsToken.Exists
                ? (defaultsToken.AsArray<string>() ?? new string[0])
                : new string[0];

            foreach (var code in defaults)
            {
                if (string.IsNullOrEmpty(code)) continue;
                EnergyStore.SetModule(stack, code, true);
            }
        }

        public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc,
            IWorldAccessor world, bool withDebugInfo)
        {
            base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);

            var stack = inSlot.Itemstack;
            if (stack == null) return;

            int e = EnergyStore.GetEnergy(stack);
            int max = EnergyStore.GetMaxEnergy(stack);

            dsc.AppendLine(Lang.Get("vepowersuit:energy-line", e, max));

            bool any = false;
            foreach (var kv in ModuleRegistry.All)
            {
                if (kv.Value == null) continue;

                if (EnergyStore.HasModule(stack, kv.Key))
                {
                    if (!any)
                    {
                        dsc.AppendLine(Lang.Get("vepowersuit:installed-modules"));
                        any = true;
                    }

                    string label = string.IsNullOrEmpty(kv.Value.DisplayLangKey)
                        ? kv.Key
                        : Lang.Get(kv.Value.DisplayLangKey);

                    dsc.AppendLine("  - " + label);
                }
            }

            if (!VEPowerAdapter.VEIntegrationWired)
                dsc.AppendLine(Lang.Get("vepowersuit:ve-not-wired"));
            else
                dsc.AppendLine(Lang.Get("vepowersuit:ve-charge-hint"));
        }
    }

    /// <summary>
    /// RAII-style scope that binds an ItemStack as the active VE-charging stack
    /// and restores the previous binding on Dispose. Use with `using`.
    /// </summary>
    public readonly struct VEChargeContext : IDisposable
    {
        private readonly ItemStack _previous;
        private readonly Action<ItemStack> _restore;

        public VEChargeContext(ItemStack stack, Action<ItemStack> restore,
            ItemStack previous, Action<ItemStack> set)
        {
            _previous = previous;
            _restore = restore;
            set(stack);
        }

        public void Dispose() => _restore?.Invoke(_previous);
    }
}