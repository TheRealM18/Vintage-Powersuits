using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace VEPowersuit.Blocks
{
    /// <summary>
    /// GUI for the Module Installer. Extends GuiDialogBlockEntityInventory
    /// (constructor: dialogTitle, inventory, blockEntityPos, cols, capi) so ALL
    /// slot networking is handled by the base class — this mod never calls the
    /// version-finicky SendBlockEntityPacket slot overloads itself.
    ///
    /// Install is triggered when the dialog closes (both slots are validated
    /// server-side), routed through the mod's own stable network channel via the
    /// supplied onClose action.
    ///
    /// NOTE: the close hook lives ONLY here, in OnGuiClosed, to guarantee a single
    /// fire. The block entity subscribes to the base GuiDialog.OnClosed event for
    /// its own cleanup (clearing its dialog reference + sending the Close packet);
    /// those are separate concerns and do not double-trigger the install, because
    /// the install action passed in here is invoked exactly once, from here.
    /// </summary>
    public class GuiDialogModuleInstaller : GuiDialogBlockEntityInventory
    {
        private readonly System.Action onCloseInstall;
        private bool installFired;

        public GuiDialogModuleInstaller(string title, InventoryBase inventory, BlockPos pos,
            ICoreClientAPI capi, System.Action onCloseInstall)
            : base(title, inventory, pos, 2, capi)
        {
            this.onCloseInstall = onCloseInstall;
        }

        public override void OnGuiClosed()
        {
            base.OnGuiClosed();

            // Guard against any double close so the install request fires once.
            if (!installFired)
            {
                installFired = true;
                onCloseInstall?.Invoke();
            }
        }
    }
}