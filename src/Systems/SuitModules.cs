using Vintagestory.API.Common;

namespace VEPowersuit.Systems
{
    /// <summary>
    /// Tracks which suit modules are INSTALLED and which are ENABLED, stored on
    /// the stack. This is the only suit-specific state we keep — power itself is
    /// owned by Vintage Engineering's ItemChargable (stack attribute
    /// "currentpower"); VE has no concept of our modules, so this stays.
    ///
    /// "Installed" is set at the installer block and persists. "Enabled" is the
    /// GUI on/off switch and only matters for installed modules (an installed
    /// module with no explicit flag defaults to enabled).
    /// </summary>
    public static class SuitModules
    {
        private const string InstalledKey = "paModules";
        private const string EnabledKey = "paModulesEnabled";

        public static bool IsInstalled(ItemStack stack, string code)
        {
            var tree = stack?.Attributes?.GetTreeAttribute(InstalledKey);
            return tree != null && tree.GetBool(code, false);
        }

        public static void SetInstalled(ItemStack stack, string code, bool installed)
        {
            if (stack == null) return;
            var tree = stack.Attributes.GetOrAddTreeAttribute(InstalledKey);
            tree.SetBool(code, installed);
            if (!installed)
            {
                var en = stack.Attributes.GetTreeAttribute(EnabledKey);
                en?.RemoveAttribute(code);
            }
        }

        /// <summary>Installed module switched ON? False if not installed; defaults ON when installed.</summary>
        public static bool IsEnabled(ItemStack stack, string code)
        {
            if (!IsInstalled(stack, code)) return false;
            var tree = stack?.Attributes?.GetTreeAttribute(EnabledKey);
            return tree == null || !tree.HasAttribute(code) || tree.GetBool(code, true);
        }

        public static void SetEnabled(ItemStack stack, string code, bool enabled)
        {
            if (stack == null || !IsInstalled(stack, code)) return;
            var tree = stack.Attributes.GetOrAddTreeAttribute(EnabledKey);
            tree.SetBool(code, enabled);
        }
    }
}
