using System.Text;
using VEPowersuit.Systems;
using VEPowersuit.Modules;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Util;

namespace VEPowersuit.Items
{
    public class ItemVEPowersuit : Item
    {
        public bool IsCore => Attributes?["isCore"]?.AsBool(false) ?? false;

        public override void OnLoaded(ICoreAPI api)
        {
            base.OnLoaded(api);
        }

        public override void OnCreatedByCrafting(ItemSlot[] allInputslots, ItemSlot outputSlot,
            IRecipeBase byRecipe)
        {
            base.OnCreatedByCrafting(allInputslots, outputSlot, byRecipe);

            var stack = outputSlot.Itemstack;
            if (stack == null) return;

            int max = Attributes?["maxEnergy"]?.AsInt(100000) ?? 100000;
            EnergyStore.SetMaxEnergy(stack, max);

            // Safely read defaultModules array — AsArray<string> can throw on
            // missing/non-array tokens depending on JsonObject implementation.
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
        }
    }
}