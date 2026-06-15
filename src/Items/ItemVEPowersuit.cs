using System;
using System.Text;
using VEPowersuit.Behaviors;
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
    /// a [ThreadStatic] context (via the charger Harmony patch) for the
    /// duration of that read/charge sequence. "chargable" is set to FALSE in the
    /// itemtype JSON, so VE never runs its durability-topup charge route for us;
    /// instead VEChargerBindPatch (gated on the CollectibleBehaviorPowerCharged
    /// behavior) fully takes over the charger tick and routes power through this
    /// IChargeableItem interface into the EU store. The EU store remains the
    /// single source of truth for the suit's power, and durability is left
    /// untouched (the behavior also cancels any durability loss).
    /// </summary>
    public class ItemVEPowersuit : Item, IChargeableItem
    {
        public bool IsCore => Attributes?["isCore"]?.AsBool(false) ?? false;

        /// <summary>
        /// The power-charged behavior on this piece, or null if not attached.
        /// When present, this piece is treated as power-only: charged through
        /// the EU store via the Harmony patch, with durability loss cancelled so
        /// it never drains. See <see cref="CollectibleBehaviorPowerCharged"/>.
        /// </summary>
        public CollectibleBehaviorPowerCharged PowerCharged
            => GetBehavior<CollectibleBehaviorPowerCharged>();

        /// <summary>True if this piece runs on EU instead of durability.</summary>
        public bool IsPowerOnly => PowerCharged?.IsPowerOnly ?? false;

        /// <summary>True if the charger Harmony patch should service this piece.</summary>
        public bool WantsPatchCharging => PowerCharged?.WantsPatchCharging ?? false;

        // The stack VE is currently charging on this thread. Bound by
        // BindChargingStack() (call site: see VEChargeContext below) so the
        // parameterless IChargeableItem getters resolve to the right stack.
        [ThreadStatic] private static ItemStack _veActiveStack;

        /// <summary>
        /// Bind the stack VE is about to charge so the IChargeableItem members
        /// below resolve against it. Returns a disposable scope that clears the
        /// binding. Called by the charger patch around VE's charger tick.
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

            // VE's charger (BELVCharger.OnSimTick) calls this as
            // RatedPower(dt, false) to learn how much power it may PUSH INTO the
            // suit this tick (it uses the result as "powertopush"). VE passes
            // isInsert:false there, so we must report the RECEIVE rating for
            // both cases — the suit only ever accepts power, it is not an
            // extractable source. Returning 0 for isInsert:false (the old
            // behavior) made VE compute powertopush = 0 and charge nothing.
            return VEPowerAdapter.RatedReceive(Active, dt);
        }

        public ulong ReceivePower(ulong powerOffered, float dt, bool simulate = false)
        {
            if (Active == null)
            {
                api?.Logger.VerboseDebug("[vepowersuit] ReceivePower called with no bound stack (offered={0})", powerOffered);
                return powerOffered;
            }

            int before = EnergyStore.GetEnergy(Active);
            ulong leftover = VEPowerAdapter.ReceiveFromVE(Active, powerOffered, dt, simulate);
            if (!simulate)
            {
                int after = EnergyStore.GetEnergy(Active);
                if (after != before)
                    api?.Logger.VerboseDebug("[vepowersuit] charged {0} EU ({1} -> {2} / {3}), offered={4}, leftover={5}",
                        after - before, before, after, EnergyStore.GetMaxEnergy(Active), powerOffered, leftover);
            }
            return leftover;
        }

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
            EnsureInitialized(outputSlot?.Itemstack);
        }

        /// <summary>
        /// Seed a stack's max-energy, max-PPS, and default modules if not done
        /// yet, and — for the creative variant (attribute
        /// <c>fullChargeOnGet</c> true) — fill it to max energy. Idempotent:
        /// keyed on whether the max-energy attribute has been written, so it
        /// runs once per stack (covers both crafted and creative-tab stacks).
        /// </summary>
        public void EnsureInitialized(ItemStack stack)
        {
            if (stack?.Attributes == null) return;

            bool firstTime = !stack.Attributes.HasAttribute("paMaxEnergy");
            if (!firstTime) return;

            int max = Attributes?["maxEnergy"]?.AsInt(100000) ?? 100000;
            EnergyStore.SetMaxEnergy(stack, max);

            int pps = Attributes?["maxPPS"]?.AsInt(2000) ?? 2000;
            EnergyStore.SetMaxPPS(stack, pps);

            // Default modules: variant-aware. Reads "defaultModules" if it's a
            // plain array (or a ByType that VS already resolved), else resolves
            // "defaultModulesByType" by this stack's variant ourselves.
            foreach (var code in ResolveDefaultModules())
            {
                if (string.IsNullOrEmpty(code)) continue;
                EnergyStore.SetModule(stack, code, true);
            }

            // Creative variant comes fully charged. Same variant-aware read.
            if (ResolveFullChargeOnGet())
                EnergyStore.SetEnergy(stack, EnergyStore.GetMaxEnergy(stack));
        }

        /// <summary>The value of the "type" variant group for this item (e.g. "creative").</summary>
        private string VariantType => Variant?["type"];

        private bool ResolveFullChargeOnGet()
        {
            // 1) Manual ByType resolution against this stack's variant (preferred,
            //    since custom nested attributes may not be auto-collapsed).
            var byType = Attributes?["fullChargeOnGetByType"];
            if (byType != null && byType.Exists && !byType.IsArray())
            {
                string v = VariantType;
                if (v != null && byType[v] != null && byType[v].Exists) return byType[v].AsBool(false);
                if (byType["*"] != null && byType["*"].Exists) return byType["*"].AsBool(false);
            }

            // 2) Plain key fallback.
            var plain = Attributes?["fullChargeOnGet"];
            return plain != null && plain.Exists && !plain.IsArray()
                ? plain.AsBool(false)
                : false;
        }

        private string[] ResolveDefaultModules()
        {
            var empty = new string[0];

            var byType = Attributes?["defaultModulesByType"];
            if (byType != null && byType.Exists && !byType.IsArray())
            {
                string v = VariantType;
                if (v != null && byType[v] != null && byType[v].Exists)
                    return byType[v].AsArray<string>() ?? empty;
                if (byType["*"] != null && byType["*"].Exists)
                    return byType["*"].AsArray<string>() ?? empty;
            }

            var plain = Attributes?["defaultModules"];
            if (plain != null && plain.Exists && plain.IsArray())
                return plain.AsArray<string>() ?? empty;

            return empty;
        }

        public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc,
            IWorldAccessor world, bool withDebugInfo)
        {
            base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);

            var stack = inSlot.Itemstack;
            if (stack == null) return;

            // Make sure a freshly-obtained stack (incl. creative-tab) is seeded.
            EnsureInitialized(stack);

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
            else if (IsPowerOnly)
                dsc.AppendLine(Lang.Get("vepowersuit:ve-charge-hint-poweronly"));
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