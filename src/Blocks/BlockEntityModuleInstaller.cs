using System.Text;
using VEPowersuit.Items;
using VEPowersuit.Modules;
using VEPowersuit.Systems;
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
    /// Calling Install() reads the module's code from the module item and sets
    /// the corresponding module flag on the armor via EnergyStore, then consumes
    /// the module item.
    /// </summary>
    public class BlockEntityModuleInstaller : BlockEntityOpenableContainer
    {
        private readonly InventoryGeneric inventory;
        private GuiDialogModuleInstaller clientDialog;

        public override InventoryBase Inventory => inventory;
        public override string InventoryClassName => "vepowersuit:moduleinstaller";

        public ItemSlot ArmorSlot => inventory[0];
        public ItemSlot ModuleSlot => inventory[1];

        public BlockEntityModuleInstaller()
        {
            inventory = new InventoryGeneric(2, null, null);
        }

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);
            inventory.LateInitialize(InventoryClassName + "-" + Pos, api);
        }

        /// <summary>
        /// Attempts to install the module from the module slot onto the armor in
        /// the armor slot. Returns a human-readable status (already localized
        /// upstream where shown). Server-authoritative; call on server.
        /// </summary>
        public bool TryInstall(out string failReason)
        {
            failReason = null;
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

            string code = module.Collectible.Attributes?["moduleCode"]?.AsString(null);
            if (code == null || ModuleRegistry.Get(code) == null)
            {
                failReason = "badmodule";
                return false;
            }
            if (EnergyStore.HasModule(armor, code))
            {
                failReason = "duplicate";
                return false;
            }

            EnergyStore.SetModule(armor, code, true);
            ModuleSlot.TakeOut(1);
            ArmorSlot.MarkDirty();
            ModuleSlot.MarkDirty();
            MarkDirty(true);
            return true;
        }

        public override bool OnPlayerRightClick(IPlayer byPlayer, BlockSelection blockSel)
        {
            if (Api.Side == EnumAppSide.Client)
            {
                if (clientDialog == null)
                {
                    var capi = Api as Vintagestory.API.Client.ICoreClientAPI;
                    clientDialog = new GuiDialogModuleInstaller(
                        Lang.Get("vepowersuit:installer-title"), inventory, Pos, capi,
                        () => VEPowersuitModSystem.SendInstall(capi, Pos));
                    clientDialog.OnClosed += () => clientDialog = null;
                }
                clientDialog.TryOpen();
            }
            return true;
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
