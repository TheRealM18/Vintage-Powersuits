using System;
using System.Text;
using VEPowersuit.Systems;
using VEPowersuit.Modules;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using VintageEngineering.Electrical;

namespace VEPowersuit.Items
{
    /// <summary>
    /// Power-armor item. Extends VE's ItemChargable (the new base class for
    /// chargeable items) and lets that base own the power model. Power is stored
    /// on the STACK in attribute "currentpower" via the inherited
    /// SetPower(stack, ...).
    ///
    /// WHY WE RE-LIST IChargeableItem AND new-shadow a few members:
    /// ItemChargable.CurrentPower reads from the shared collectible JSON
    /// (this.Attributes["currentpower"]) while SetPower writes to the STACK, and
    /// none of its members are virtual. So per-stack charge can't be read back
    /// through the base as-is. By re-declaring IChargeableItem in our base list
    /// and providing `new` members that read the stack, C# builds a fresh
    /// interface map for this type: VE's charger (which calls through an
    /// IChargeableItem reference) dispatches to OUR stack-correct members.
    ///
    /// The interface's parameterless CurrentPower can't see a stack, so we cache
    /// the stack VE is currently servicing (set at the top of each stack-passing
    /// call) and read it there. The charger reads CurrentPower right next to the
    /// stack-passing calls on the same tick, so the context is always valid.
    ///
    /// JSON drives config: maxpower, maxpps, canreceivepower, canextractpower.
    /// Durability is cancelled (power-only). Modules + creative pre-charge live
    /// here; module state lives in SuitModules.
    /// </summary>
    public class ItemVEPowersuit : ItemChargable, IChargeableItem
    {
        public const string PowerKey = "currentpower";

        public bool IsCore => Attributes?["isCore"]?.AsBool(false) ?? false;

        // The stack VE is currently charging (set by each stack-passing call so
        // the parameterless CurrentPower getter the charger uses resolves to it).
        [ThreadStatic] private static ItemStack _ctx;

        // ---- stack-correct power readout (shadows the base) ----

        public new ulong CurrentPower => ReadPower(_ctx);

        private ulong ReadPower(ItemStack stack)
            => stack == null ? 0UL : (ulong)Math.Max(0L, stack.Attributes.GetLong(PowerKey, 0));

        // MaxPower / MaxPPS / CanReceivePower / CanExtractPower / SetPower /
        // CheatPower are inherited from ItemChargable (they read JSON or the
        // stack correctly already).

        // ---- re-implemented interface methods (stack-correct) ----

        public new ulong RatedPower(ItemStack stack, float dt, bool isInsert = false)
        {
            _ctx = stack;
            ulong rate = (ulong)Math.Round(MaxPPS * dt);
            if (isInsert)
            {
                if (!CanReceivePower) return 0;
                ulong room = MaxPower - ReadPower(stack);
                return room < rate ? room : rate;
            }
            if (!CanExtractPower) return 0;
            ulong cur = ReadPower(stack);
            if (cur == 0) return 0;
            return cur < rate ? cur : rate;
        }

        public new ulong ReceivePower(ItemStack stack, ulong powerOffered, float dt, bool simulate = false)
        {
            _ctx = stack;
            ulong cur = ReadPower(stack);
            if (cur >= MaxPower) return powerOffered;          // full, bounce
            if (simulate) return RatedPower(stack, dt, true);

            ulong pps = (ulong)Math.Round((MaxPPS * 1.05) * dt);
            if (pps == 0) pps = ulong.MaxValue; else pps += 2;

            ulong room = MaxPower - cur;
            if (pps > room) pps = room;

            if (pps >= powerOffered)
            {
                SetPower(stack, cur + powerOffered);
                return 0;
            }
            SetPower(stack, cur + pps);
            return powerOffered - pps;
        }

        public new ulong ExtractPower(ItemStack stack, ulong powerWanted, float dt, bool simulate = false)
        {
            _ctx = stack;
            ulong cur = ReadPower(stack);
            if (cur == 0 || !CanExtractPower) return powerWanted;
            if (simulate) return RatedPower(stack, dt, false);

            ulong pps = (ulong)Math.Round((MaxPPS * 1.05) * dt);
            if (pps == 0) pps = ulong.MaxValue;
            if (pps > cur) pps = cur;

            if (pps >= powerWanted)
            {
                SetPower(stack, cur - powerWanted);
                return 0;
            }
            SetPower(stack, cur - pps);
            return powerWanted - pps;
        }

        // ---- power-only: cancel durability loss ----

        public override void OnDamageItem(IWorldAccessor world, Entity byEntity,
            ItemSlot itemslot, ref int amount, ref EnumHandling bhHandling)
        {
            amount = 0;
            bhHandling = EnumHandling.PreventDefault;
        }

        // ---- crafting / creative pre-charge ----

        public override void OnCreatedByCrafting(ItemSlot[] allInputslots, ItemSlot outputSlot,
            IRecipeBase byRecipe)
        {
            base.OnCreatedByCrafting(allInputslots, outputSlot, byRecipe);
            EnsureInitialized(outputSlot?.Itemstack);
        }

        /// <summary>One-time per-stack seed (idempotent via "paInit").</summary>
        public void EnsureInitialized(ItemStack stack)
        {
            if (stack?.Attributes == null || stack.Attributes.GetBool("paInit", false)) return;
            stack.Attributes.SetBool("paInit", true);

            foreach (var code in ResolveDefaultModules())
            {
                if (string.IsNullOrEmpty(code)) continue;
                SuitModules.SetInstalled(stack, code, true);
            }

            if (ResolveFullChargeOnGet())
                SetPower(stack, MaxPower);   // inherited; writes stack "currentpower"
        }

        private string VariantType => Variant?["type"];

        private bool ResolveFullChargeOnGet()
        {
            var byType = Attributes?["fullChargeOnGetByType"];
            if (byType != null && byType.Exists && !byType.IsArray())
            {
                string v = VariantType;
                if (v != null && byType[v] != null && byType[v].Exists) return byType[v].AsBool(false);
                if (byType["*"] != null && byType["*"].Exists) return byType["*"].AsBool(false);
            }
            var plain = Attributes?["fullChargeOnGet"];
            return plain != null && plain.Exists && !plain.IsArray() && plain.AsBool(false);
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

        // ---- tooltip ----

        public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc,
            IWorldAccessor world, bool withDebugInfo)
        {
            base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);

            var stack = inSlot.Itemstack;
            if (stack == null) return;

            EnsureInitialized(stack);

            dsc.AppendLine(Lang.Get("vepowersuit:energy-line", (long)ReadPower(stack), (long)MaxPower));

            bool any = false;
            foreach (var kv in ModuleRegistry.All)
            {
                if (kv.Value == null || !SuitModules.IsInstalled(stack, kv.Key)) continue;
                if (!any) { dsc.AppendLine(Lang.Get("vepowersuit:installed-modules")); any = true; }
                string label = string.IsNullOrEmpty(kv.Value.DisplayLangKey)
                    ? kv.Key : Lang.Get(kv.Value.DisplayLangKey);
                dsc.AppendLine("  - " + label);
            }

            dsc.AppendLine(Lang.Get("vepowersuit:ve-charge-hint"));
        }
    }
}
