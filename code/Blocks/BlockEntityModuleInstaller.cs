using System.Diagnostics.CodeAnalysis;
using System.Text;
using VEPowersuit.Items;
using VEPowersuit.Modules;
using VEPowersuit.Systems;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace VEPowersuit.Blocks
{
    /// <summary>
    /// Block entity for the Module Installer. Holds two slots:
    ///   slot 0 = the power-armor piece to modify
    ///   slot 1 = a power-module item to install
    /// TryInstall() reads the module's code from the module item and sets the
    /// corresponding module flag on the armor via SuitModules, then consumes the
    /// module item.
    ///
    /// IMPORTANT (this is what was broken): a BlockEntityOpenableContainer only
    /// networks its slots while a player has the inventory OPEN on the SERVER.
    /// The previous version opened a dialog on the client but never opened the
    /// inventory server-side, so the GUI showed dead, un-syncing slots. The fix
    /// is to route the open/close through the base class's standard packet
    /// handling (EnumBlockEntityPacketId.Open/Close), which both opens the
    /// inventory server-side AND starts slot syncing. We let the base class do
    /// that work and only add our dialog on top.
    /// </summary>
    public class BlockEntityModuleInstaller : BlockEntityOpenableContainer
    {
        private readonly InventoryGeneric inventory;
        [AllowNull]
        private GuiDialogModuleInstaller clientDialog;

        public override InventoryBase Inventory => inventory;
        public override string InventoryClassName => "vepowersuit:moduleinstaller";

        public ItemSlot ArmorSlot => inventory[0];
        public ItemSlot ModuleSlot => inventory[1];

        public BlockEntityModuleInstaller()
        {
            inventory = new InventoryGeneric(2, null, null);
            inventory.SlotModified += OnSlotModified;
        }

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);
            inventory.LateInitialize(InventoryClassName + "-" + Pos, api);
        }

        private void OnSlotModified(int slotid)
        {
            // The dialog's slot grid redraws itself from the inventory's dirty-slot
            // tracking, so no manual refresh call is needed here.
            MarkDirty();
        }

        /// <summary>
        /// Attempts to install the module from the module slot onto the armor in
        /// the armor slot. Server-authoritative; call on server.
        /// </summary>
        public bool TryInstall(out string failReason)
        {
            failReason = string.Empty;
            var armor = ArmorSlot.Itemstack;
            var module = ModuleSlot.Itemstack;

            if (armor == null || armor.Collectible is not ItemVEPowersuit)
            {
                failReason = "noarmor";
                return false;
            }
            if (module == null)
            {
                failReason = "nomodule";
                return false;
            }

            string code = module.Collectible.Attributes?["moduleCode"].AsString(string.Empty) ?? string.Empty;
            if (code == string.Empty || ModuleRegistry.Get(code) == null)
            {
                failReason = "badmodule";
                return false;
            }
            if (SuitModules.IsInstalled(armor, code))
            {
                failReason = "duplicate";
                return false;
            }

            SuitModules.SetInstalled(armor, code, true);
            ModuleSlot.TakeOut(1);
            ArmorSlot.MarkDirty();
            ModuleSlot.MarkDirty();
            MarkDirty(true);
            return true;
        }

        /// <summary>
        /// Right-click. On the CLIENT we toggle the dialog and fire the standard
        /// Open/Close block-entity packets; the base class's OnReceivedClientPacket
        /// opens/closes the inventory server-side (which starts slot networking).
        /// On the SERVER, returning true simply acknowledges the interaction.
        /// </summary>
        public override bool OnPlayerRightClick(IPlayer byPlayer, BlockSelection blockSel)
        {
            if (Api == null) return false;

            if (Api.Side == EnumAppSide.Client)
            {
                ToggleDialog(byPlayer);
            }

            return true;
        }

        private void ToggleDialog(IPlayer byPlayer)
        {
            var capi = Api as ICoreClientAPI;
            if (capi == null) return;

            if (clientDialog == null)
            {
                clientDialog = new GuiDialogModuleInstaller(
                    Lang.Get("vepowersuit:installer-title"), inventory, Pos, capi,
                    () => VEPowersuitModSystem.SendInstall(capi, Pos));

                clientDialog.OnClosed += () =>
                {
                    clientDialog = null;
                    // Standard close packet: base class closes the inventory for us.
                    capi.Network.SendBlockEntityPacket(Pos, (int)EnumBlockEntityPacketId.Close);
                };

                if (clientDialog.TryOpen())
                {
                    // Standard open packet: base class opens the inventory server
                    // side and begins streaming slot contents to this client.
                    capi.Network.SendBlockEntityPacket(Pos, (int)EnumBlockEntityPacketId.Open);
                }
            }
            else
            {
                clientDialog.TryClose();
            }
        }

        // Let the base class handle Open/Close (it opens/closes the inventory for
        // the player and drives slot syncing). We only override to keep the hook
        // explicit; do NOT re-implement Open/Close here or the inventory opens twice.
        public override void OnReceivedClientPacket(IPlayer player, int packetid, byte[] data)
        {
            base.OnReceivedClientPacket(player, packetid, data);
        }

        public override void OnBlockRemoved()
        {
            base.OnBlockRemoved();
            clientDialog?.TryClose();
        }

        public override void OnBlockUnloaded()
        {
            base.OnBlockUnloaded();
            clientDialog?.TryClose();
        }

        public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
        {
            base.GetBlockInfo(forPlayer, dsc);
            dsc.AppendLine(Lang.Get("vepowersuit:installer-hint"));
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor world)
        {
            base.FromTreeAttributes(tree, world);
            inventory.FromTreeAttributes(tree.GetTreeAttribute("inventory"));
            // No manual dialog refresh: the slot grid redraws from the synced
            // inventory's dirty-slot list automatically.
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
            var inv = new TreeAttribute();
            inventory.ToTreeAttributes(inv);
            tree["inventory"] = inv;
        }
    }
}