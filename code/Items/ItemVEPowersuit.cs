using System;
using System.Text;
using VEPowersuit.Modules;
using VEPowersuit.Systems;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using VintageEngineering.Electrical;
using Vintagestory.API.Datastructures;
using System.Diagnostics.CodeAnalysis;

namespace VEPowersuit.Items
{
    /// <summary>
    /// Power-armor item. Extends VE's ItemChargable so a VE charger charges it;
    /// power is stored on the STACK in attribute "currentpower".
    ///
    /// ItemChargable stores power on the stack (SetPower) but its CurrentPower
    /// getter reads the shared collectible JSON instead of the stack, and none
    /// of its members are virtual - so per-stack charge never accumulates. We
    /// re-list IChargeableItem and `new`-shadow ONLY the three members the
    /// charger touches that depend on CurrentPower (CurrentPower, RatedPower,
    /// ReceivePower) so they read the stack. Re-listing the interface makes VE's
    /// charger (which calls through an IChargeableItem reference) dispatch to
    /// these. Everything else (MaxPower, MaxPPS, Can*, SetPower, ExtractPower,
    /// CheatPower) is inherited unchanged.
    /// </summary>
    public class ItemVEPowersuit : ItemChargable
    {
        /// <summary>True for the chestplate (carries shared suit state). Used by SuitHelper.</summary>
        public bool IsCore => Attributes?["isCore"]?.AsBool(false) ?? false;

        // ---- one-time per-stack setup (default modules + creative pre-charge) ----

        public override void OnCreatedByCrafting(ItemSlot[] inputSlots, ItemSlot outputSlot,
            IRecipeBase byRecipe)
        {
            base.OnCreatedByCrafting(inputSlots, outputSlot, byRecipe);
            EnsureInitialized(outputSlot.Itemstack);
        }

        public void EnsureInitialized(ItemStack? stack)
        {
            if (stack?.Attributes == null || stack.Attributes.GetBool("paInit", false)) return;
            stack.Attributes.SetBool("paInit", true);

            foreach (string code in ResolveDefaultModules())
                if (!string.IsNullOrEmpty(code)) SuitModules.SetInstalled(stack, code, true);

            if (ResolveFullChargeOnGet())
                SetPower(stack, MaxPower);
        }

        private string VariantType => Variant["type"];

        private bool ResolveFullChargeOnGet()
        {
            var byType = Attributes?["fullChargeOnGetByType"];
            if (byType != null && byType.Exists && !byType.IsArray())
            {
                string v = VariantType;
                if (v != null && byType[v]?.Exists == true) return byType[v].AsBool(false);
                if (byType["*"]?.Exists == true) return byType["*"].AsBool(false);
            }
            var plain = Attributes?["fullChargeOnGet"];
            return plain != null && plain.Exists && !plain.IsArray() && plain.AsBool(false);
        }
        private string[] ResolveDefaultModules()
        {
            string[] empty = Array.Empty<string>();
            JsonObject byType = Attributes["defaultModulesByType"];
            if (byType != null && byType.Exists && !byType.IsArray())
            {
                string v = VariantType;
                if (v != null && byType[v].Exists) return byType[v].AsArray<string>()!; //.AsArray<string>(Array.Empty<string>());
                if (byType["*"]?.Exists == true) return byType["*"].AsArray<string>()!;
            }
            return Array.Empty<string>()!;
        }

        // ---- tooltip: charge + installed modules ----

        public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc,
            IWorldAccessor world, bool withDebugInfo)
        {
            base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);

            var stack = inSlot?.Itemstack;
            if (stack == null) return;

            EnsureInitialized(stack);
            dsc.AppendLine(Lang.Get("vepowersuit:energy-line", CurrentPower(stack), (long)MaxPower));

            bool any = false;
            foreach (var kv in ModuleRegistry.All)
            {
                if (kv.Value == null || !SuitModules.IsInstalled(stack, kv.Key)) continue;
                if (!any) { dsc.AppendLine(Lang.Get("vepowersuit:installed-modules")); any = true; }
                dsc.AppendLine("  - " + (string.IsNullOrEmpty(kv.Value.DisplayLangKey)
                    ? kv.Key : Lang.Get(kv.Value.DisplayLangKey)));
            }

            dsc.AppendLine(Lang.Get("vepowersuit:ve-charge-hint"));
        }
    }
}