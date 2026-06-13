using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace VEPowersuit.Blocks
{
    /// <summary>
    /// GUI for the Module Installer. Extends GuiDialogBlockEntityInventory (verified
    /// constructor: title, inventory, blockEntityPos, cols, capi) so ALL slot
    /// networking is handled by the base class — this mod never calls the
    /// version-finicky SendBlockEntityPacket overloads itself.
    ///
    /// Install is triggered automatically when the dialog closes (both slots are
    /// checked server-side), routed through the mod's own stable network channel
    /// via the supplied action. This avoids a custom button, which the inventory
    /// base does not compose for us.
    /// </summary>
    public class GuiDialogModuleInstaller : GuiDialogBlockEntityInventory
    {
        private readonly System.Action onClose;

        public GuiDialogModuleInstaller(string title, InventoryBase inventory, BlockPos pos,
            ICoreClientAPI capi, System.Action onClose)
            : base(title, inventory, pos, 2, capi)
        {
            this.onClose = onClose;
        }

        public override void OnGuiClosed()
        {
            base.OnGuiClosed();
            onClose?.Invoke();
        }
    }
}