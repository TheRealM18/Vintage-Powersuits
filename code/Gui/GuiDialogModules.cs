using System.Collections.Generic;
using VEPowersuit.Modules;
using VEPowersuit.Network;
using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace VEPowersuit.Gui
{
    /// <summary>
    /// Toggle panel for the suit's modules, opened with the GUI hotkey (U).
    ///
    /// Installed-vs-enabled: installation is done at the installer block. This
    /// panel only switches an INSTALLED module ON or OFF. Modules that are not
    /// installed are shown greyed/disabled so the player can see what exists but
    /// can't toggle them on. Button pressed-state is driven by the live state
    /// the server sends (ModuleStatePacket), so a button "stays active" exactly
    /// when the module is actually enabled on the server.
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
            if (ModuleRegistry.All.Count == 0) return; // nothing to show yet

            double rowHeight = 32;
            double padding   = GuiStyle.ElementToDialogPadding;
            double innerW    = 240;
            double innerH    = ModuleRegistry.All.Count * rowHeight - (rowHeight - 25);

            ElementBounds bgBounds =
                ElementBounds.Fixed(0, 0, innerW + padding * 2, innerH + 40 + padding * 2)
                            .WithFixedPadding(padding);

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
                    on => OnToggle(code, on),
                    ElementBounds.Fixed(0, y, innerW, 25),
                    "btn-" + code);
                y += rowHeight;
            }

            composer.EndChildElements();
            SingleComposer = composer.Compose();

            RefreshStates();
        }

        /// <summary>
        /// Push the latest server-known install/enable state into the buttons so
        /// each button's pressed look matches reality. Called on compose and
        /// whenever a fresh ModuleStatePacket arrives.
        /// </summary>
        public void RefreshStates()
        {
            if (SingleComposer == null) return;

            foreach (var kv in ModuleRegistry.All)
            {
                string code = kv.Key;
                var btn = SingleComposer.GetToggleButton("btn-" + code);
                if (btn == null) continue;

                bool installed = false, enabled = false;
                if (mod.ModuleState.TryGetValue(code, out var st))
                {
                    installed = st.installed;
                    enabled = st.enabled;
                }

                // Ensure the element latches when clicked, is only interactive
                // when the module is installed, and visually reflects the
                // authoritative enabled state from the server.
                btn.Toggleable = true;
                btn.Enabled = installed;
                bool target = installed && enabled;
                btn.SetValue(target);   // sets On + redraws
            }
        }

        private void OnToggle(string code, bool wantOn)
        {
            // Send the EXPLICIT desired state (not a blind flip) so the server
            // can't desync if clicks arrive out of order. The server applies it
            // and echoes a ModuleStatePacket, which calls RefreshStates() and
            // settles the button on the authoritative value (reverting if the
            // module isn't installed).
            channel.SendPacket(new ToggleModulePacket { ModuleCode = code, DesiredOn = wantOn });
        }
    }
}
