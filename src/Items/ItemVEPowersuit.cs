using System.Text;
using VEPowersuit.Systems;
using VEPowersuit.Modules;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Util;

namespace VEPowersuit.Items
{
    /// <summary>
    /// A wearable electric armor piece that carries energy + modules.
    /// The chestplate is the "core" that the flight/sprint systems look for.
    ///
    /// NOTE: This inherits from plain Item, NOT ItemWearable. ItemWearable
    /// lives in Vintagestory.GameContent and is obsolete in current versions
    /// (replaced by the Wearable collectible behavior). The wearable mechanics
    /// — slot placement, armor protection, stat modifiers — come from the
    /// { "name": "Wearable" } entry in each piece's itemtype JSON, so the C#
    /// class only needs to add our custom energy/module logic on top of Item.
    /// </summary>
    public class ItemVEPowersuit : Item
    {
        public bool IsCore => Attributes?["isCore"]?.AsBool(false) ?? false;

        public override void OnLoaded(ICoreAPI api)
        {
            base.OnLoaded(api);
        }

        // Make sure freshly-crafted pieces know their max energy + default modules.
        public override void OnCreatedByCrafting(ItemSlot[] allInputslots, ItemSlot outputSlot,
            IRecipeBase byRecipe)
        {
            base.OnCreatedByCrafting(allInputslots, outputSlot, byRecipe);
            var stack = outputSlot.Itemstack;
            int max = Attributes?["maxEnergy"]?.AsInt(100000) ?? 100000;
            EnergyStore.SetMaxEnergy(stack, max);

            // Install any modules listed as defaults in the itemtype JSON.
            string[] defaults = Attributes?["defaultModules"]?.AsArray<string>(new string[0])
                                ?? new string[0];
            foreach (var code in defaults)
                EnergyStore.SetModule(stack, code, true);
        }

        public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc,
            IWorldAccessor world, bool withDebugInfo)
        {
            base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);
            var stack = inSlot.Itemstack;
            int e = EnergyStore.GetEnergy(stack);
            int max = EnergyStore.GetMaxEnergy(stack);

            dsc.AppendLine(Lang.Get("vepowersuit:energy-line", e, max));

            bool any = false;
            foreach (var kv in ModuleRegistry.All)
            {
                if (EnergyStore.HasModule(stack, kv.Key))
                {
                    if (!any) { dsc.AppendLine(Lang.Get("vepowersuit:installed-modules")); any = true; }
                    dsc.AppendLine("  - " + Lang.Get(kv.Value.DisplayLangKey));
                }
            }

            if (!VEPowerAdapter.VEIntegrationWired)
                dsc.AppendLine(Lang.Get("vepowersuit:ve-not-wired"));
        }
    }
}