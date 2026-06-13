using VEPowersuit.Modules;
using VEPowersuit.Network;
using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace VEPowersuit.Gui
{
    /// <summary>
    /// Simple toggle panel for installed modules, opened with the GUI hotkey (U).
    /// Only modules already installed on the armor are shown as toggleable;
    /// installation itself is done via crafting/anvil recipes (see JSON).
    /// </summary>
    public class GuiDialogModules : GuiDialog
    {
        private readonly VEPowersuitModSystem mod;
        private readonly IClientNetworkChannel channel;

        public GuiDialogModules(ICoreClientAPI capi, VEPowersuitModSystem mod,
            IClientNetworkChannel channel) : base(capi)
        {
            this.mod = mod;
            this.channel = channel;
            ComposeDialog();
        }

        public override string ToggleKeyCombinationCode => "vepowersuit_gui";

        private void ComposeDialog()
        {
            var rows = ModuleRegistry.All.Count;
            ElementBounds bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
            ElementBounds dialogBounds = ElementStdBounds.AutosizedMainDialog;

            var composer = capi.Gui.CreateCompo("vepowersuitmodules", dialogBounds)
                .AddShadedDialogBG(bgBounds)
                .AddDialogTitleBar(Lang.Get("vepowersuit:gui-title"), () => TryClose())
                .BeginChildElements(bgBounds);

            double y = 30;
            foreach (var kv in ModuleRegistry.All)
            {
                string code = kv.Key;
                composer.AddToggleButton(
                    Lang.Get(kv.Value.DisplayLangKey),
                    CairoFont.WhiteSmallText(),
                    on => OnToggle(code),
                    ElementBounds.Fixed(0, y, 220, 25),
                    "btn-" + code);
                y += 32;
            }

            composer.EndChildElements();
            SingleComposer = composer.Compose();
        }

        private void OnToggle(string code)
        {
            channel.SendPacket(new ToggleModulePacket { ModuleCode = code });
        }
    }
}
