using System.Collections.Generic;
using VEPowersuit.Items;
using VEPowersuit.Modules;
using VEPowersuit.Network;
using VEPowersuit.Systems;
using VintageEngineering.Electrical;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace VEPowersuit
{
    public class VEPowersuitModSystem : ModSystem
    {
        private const string Channel = "vepowersuit";

        private ICoreServerAPI? sapi;
        private ICoreClientAPI? capi;
        private IServerNetworkChannel? serverChannel;
        private IClientNetworkChannel? clientChannel;
        private Systems.NightVisionRenderer? nightVision;

        // Static client-channel ref so block entities (which don't hold the mod
        // system instance) can send the install request through our own stable
        // network channel rather than the version-finicky block-entity packet API.
        private static IClientNetworkChannel? staticClientChannel;

        /// <summary>Called from the installer block's GUI to request an install.</summary>
        public static void SendInstall(Vintagestory.API.Client.ICoreClientAPI capi,
            Vintagestory.API.MathTools.BlockPos pos)
        {
            staticClientChannel?.SendPacket(new InstallModulePacket { X = pos.X, Y = pos.Y, Z = pos.Z });
        }

        // Tracks players currently flying via power armor (server-authoritative).
        private readonly HashSet<string> flyingPlayers = new();

        public override void Start(ICoreAPI api)
        {
            base.Start(api);
            // Register the armor item class so itemtype JSON "class": "VEPowersuit" resolves.
            api.RegisterItemClass("ItemVEPowersuit", typeof(ItemVEPowersuit));

            // Module installer block + its block entity.
            api.RegisterBlockClass("VEPowersuitModuleInstaller", typeof(Blocks.BlockModuleInstaller));
            api.RegisterBlockEntityClass("VEPowersuitModuleInstaller", typeof(Blocks.BlockEntityModuleInstaller));
        }

        public override void Dispose()
        {
            base.Dispose();
        }

        // ---------------- SERVER ----------------
        public override void StartServerSide(ICoreServerAPI api)
        {
            sapi = api;
            serverChannel = api.Network.RegisterChannel(Channel)
                .RegisterMessageType<ToggleModulePacket>()
                .RegisterMessageType<ToggleFlightPacket>()
                .RegisterMessageType<EnergySyncPacket>()
                .RegisterMessageType<InstallModulePacket>()
                .RegisterMessageType<ModuleStatePacket>()
                .RegisterMessageType<RequestModuleStatePacket>()
                .SetMessageHandler<ToggleModulePacket>(OnToggleModule)
                .SetMessageHandler<ToggleFlightPacket>(OnToggleFlight)
                .SetMessageHandler<InstallModulePacket>(OnInstallModule)
                .SetMessageHandler<RequestModuleStatePacket>(OnRequestModuleState);

            // 1Hz drain/maintenance tick.
            api.Event.RegisterGameTickListener(OnServerTick, 1000);
            api.Event.PlayerDisconnect += p => StopFlight(p);

            // Attach the jump/fall behavior to each player when they join.
            api.Event.PlayerJoin += OnPlayerJoin;
        }

        private void OnPlayerJoin(IServerPlayer player)
        {
        }

        private ItemSlot? GetChestSlot(IPlayer player)
        {
            // Body-armor slot (the chestplate, flagged isCore in JSON). Shared
            // with the entity behavior and night-vision renderer via SuitHelper
            // so all three agree on what the "active suit" is.
            return SuitHelper.GetCoreSlot(player);
        }

        private void OnServerTick(float dt)
        {
            if (sapi == null) return;
            foreach (IPlayer player in sapi.World.AllOnlinePlayers)
            {
                ItemSlot? slot = GetChestSlot(player);
                if (slot == null) { StopFlight(player); continue; }
                ItemStack? stack = slot.Itemstack;
                if (stack == null) { StopFlight(player); continue; }
                // Seed a fresh stack (e.g. a creative-tab suit) so it's charged
                // and configured the first time it's worn.
                if (stack?.Collectible is ItemVEPowersuit suit0)
                    suit0.EnsureInitialized(stack);

                bool flying = flyingPlayers.Contains(player.PlayerUID);

                // Drain per-tick modules.
                foreach (var kv in ModuleRegistry.All)
                {
                    var mod = kv.Value;
                    if (mod.EnergyPerTick <= 0) continue;
                    // Must be installed AND switched on in the GUI.
                    if (!SuitModules.IsEnabled(stack, kv.Key)) continue;

                    // Flight only drains while actually flying.
                    if (kv.Key == ModuleRegistry.Flight && !flying) continue;
                    // Sprint assist only drains while sprinting.
                    if (kv.Key == ModuleRegistry.SprintAssist &&
                        !player.Entity.Controls.Sprint) continue;

                    IChargeableItem? suitpart = stack as IChargeableItem;
                    if (suitpart == null) continue;

                    ulong rated = suitpart?.RatedPower(stack, dt, false) ?? 0;
                    if (suitpart?.CurrentPower(stack) == 0 || suitpart?.CurrentPower(stack) < rated)
                    {
                        // suit is low on power
                        if (kv.Key == ModuleRegistry.Flight) StopFlight(player);
                    }
                    else
                    {
                        ulong? newpower = suitpart?.CurrentPower(stack) - rated;
                        suitpart?.SetPower(stack, newpower.GetValueOrDefault());
                    }
                }

                // If flight got switched off in the GUI (or uninstalled) while
                // airborne, drop the player out of flight.
                if (flying && !SuitModules.IsEnabled(stack, ModuleRegistry.Flight))
                {
                    StopFlight(player);
                    flying = false;
                }

                // Apply sprint-assist movement bonus.
                ApplySprintAssist(player, stack);

                slot.MarkDirty();
                SyncEnergy(player, stack, flying);
            }
        }

        private void ApplySprintAssist(IPlayer player, ItemStack? stack)
        {
            bool active = SuitModules.IsEnabled(stack, ModuleRegistry.SprintAssist)
                          && player.Entity.Controls.Sprint
                          && (stack as IChargeableItem)?.CurrentPower(stack) > 0;

            const string statCode = "vepowersuit_sprint";
            float bonus = active ? ModuleRegistry.SprintWalkSpeedBonus : 0f;
            player.Entity.Stats.Set("walkspeed", statCode, bonus, true);
        }

        private void OnToggleFlight(IServerPlayer player, ToggleFlightPacket packet)
        {
            var slot = GetChestSlot(player);
            if (slot == null) return;
            var stack = slot.Itemstack;

            // Flight must be installed AND switched on in the GUI to engage.
            if (!SuitModules.IsEnabled(stack, ModuleRegistry.Flight)) { StopFlight(player); return; }

            if (packet.WantFlying && (stack as IChargeableItem)?.CurrentPower(stack) > 0)
                StartFlight(player);
            else
                StopFlight(player);

            SyncEnergy(player, stack, flyingPlayers.Contains(player.PlayerUID));
        }

        private void StartFlight(IPlayer player)
        {
            flyingPlayers.Add(player.PlayerUID);
            var e = player.Entity;
            // NoClip stays off; we want collision flight, not spectator.
            e.Controls.IsFlying = true;
            e.Controls.NoClip = false;
            player.WorldData.FreeMove = true; // server-tracked free move
        }

        private void StopFlight(IPlayer player)
        {
            if (player == null) return;
            flyingPlayers.Remove(player.PlayerUID);
            var e = player.Entity;
            if (e != null)
            {
                e.Controls.IsFlying = false;
                player.WorldData.FreeMove = false;
            }
        }

        private void OnToggleModule(IServerPlayer player, ToggleModulePacket packet)
        {
            var slot = GetChestSlot(player);
            if (slot == null) return;
            var stack = slot.Itemstack;

            // GUI toggles ENABLED state of an already-installed module.
            // (Installation happens at the installer block, not here.)
            if (!SuitModules.IsInstalled(stack, packet.ModuleCode))
            {
                // Not installed: nothing to toggle; just resync so the client
                // GUI corrects itself.
                SendModuleState(player, stack);
                return;
            }

            bool now = !SuitModules.IsEnabled(stack, packet.ModuleCode);
            SuitModules.SetEnabled(stack, packet.ModuleCode, now);

            // If they just turned flight off, make sure they stop flying.
            if (packet.ModuleCode == ModuleRegistry.Flight && !now)
                StopFlight(player);

            slot.MarkDirty();
            SendModuleState(player, stack);
        }

        private void OnRequestModuleState(IServerPlayer player, RequestModuleStatePacket packet)
        {
            ItemSlot? slot = GetChestSlot(player);
            SendModuleState(player, slot?.Itemstack);
        }

        /// <summary>Send the installed+enabled state of all modules to the player's GUI.</summary>
        private void SendModuleState(IServerPlayer player, ItemStack? stack)
        {
            var codes = new List<string>();
            var installed = new List<bool>();
            var enabled = new List<bool>();

            foreach (var kv in ModuleRegistry.All)
            {
                codes.Add(kv.Key);
                bool inst = stack != null && SuitModules.IsInstalled(stack, kv.Key);
                installed.Add(inst);
                enabled.Add(inst && SuitModules.IsEnabled(stack, kv.Key));
            }

            serverChannel?.SendPacket(new ModuleStatePacket
            {
                Codes = codes.ToArray(),
                Installed = installed.ToArray(),
                Enabled = enabled.ToArray()
            }, player);
        }

        private void OnInstallModule(IServerPlayer player, InstallModulePacket packet)
        {
            var pos = new Vintagestory.API.MathTools.BlockPos(packet.X, packet.Y, packet.Z);
            // Basic sanity: only allow if the player is reasonably near the block.
            if (player.Entity != null)
            {
                var p = player.Entity.Pos;
                double dx = p.X - (packet.X + 0.5);
                double dy = p.Y - (packet.Y + 0.5);
                double dz = p.Z - (packet.Z + 0.5);
                if (dx * dx + dy * dy + dz * dz > 12 * 12) return;
            }

            if (sapi?.World.BlockAccessor.GetBlockEntity(pos)
                is Blocks.BlockEntityModuleInstaller be)
            {
                be.TryInstall(out _);
            }
        }

        private void SyncEnergy(IPlayer player, ItemStack? stack, bool flying)
        {            
            if (player is IServerPlayer sp)
                serverChannel?.SendPacket(new EnergySyncPacket
                {
                    Energy = (int)((stack as IChargeableItem)?.CurrentPower(stack) ?? 0),
                    MaxEnergy = (int)((stack as IChargeableItem)?.MaxPower ?? 0),
                    Flying = flying
                }, sp);
        }

        // ---------------- CLIENT ----------------
        public int LastEnergy { get; private set; }
        public int LastMaxEnergy { get; private set; }
        public bool LastFlying { get; private set; }

        // Latest module install/enable state from the server, keyed by code.
        public readonly Dictionary<string, (bool installed, bool enabled)> ModuleState = new();

        private Gui.GuiDialogModules? openModuleDialog;

        public override void StartClientSide(ICoreClientAPI api)
        {
            capi = api;
            clientChannel = api.Network.RegisterChannel(Channel)
                .RegisterMessageType<ToggleModulePacket>()
                .RegisterMessageType<ToggleFlightPacket>()
                .RegisterMessageType<EnergySyncPacket>()
                .RegisterMessageType<InstallModulePacket>()
                .RegisterMessageType<ModuleStatePacket>()
                .RegisterMessageType<RequestModuleStatePacket>()
                .SetMessageHandler<EnergySyncPacket>(OnEnergySync)
                .SetMessageHandler<ModuleStatePacket>(OnModuleState);
            staticClientChannel = clientChannel;

            api.Input.RegisterHotKey("vepowersuit_flight", Lang.Get("vepowersuit:hotkey-flight"),
                GlKeys.R, HotkeyType.CharacterControls);
            api.Input.SetHotKeyHandler("vepowersuit_flight", OnFlightHotkey);

            api.Input.RegisterHotKey("vepowersuit_gui", Lang.Get("vepowersuit:hotkey-gui"),
                GlKeys.U, HotkeyType.GUIOrOtherControls);
            api.Input.SetHotKeyHandler("vepowersuit_gui", OnGuiHotkey);

            // Client-side Night Vision driver. Runs before the main opaque pass
            // each frame so the brightness uniform is set before the world draws.
            nightVision = new NightVisionRenderer(api);
            api.Event.RegisterRenderer(nightVision, EnumRenderStage.Before, "vepowersuit-nightvision");
        }

        private bool OnFlightHotkey(KeyCombination comb)
        {
            // Toggle: ask server for the opposite of current state.
            clientChannel?.SendPacket(new ToggleFlightPacket { WantFlying = !LastFlying });
            return true;
        }

        private bool OnGuiHotkey(KeyCombination comb)
        {
            if (openModuleDialog != null && openModuleDialog.IsOpened())
            {
                openModuleDialog.TryClose();
                return true;
            }
            if (capi == null || clientChannel == null) return false;
            openModuleDialog = new Gui.GuiDialogModules(capi, this, clientChannel);
            openModuleDialog.TryOpen();
            // Ask the server for current module state so buttons render correctly.
            clientChannel.SendPacket(new RequestModuleStatePacket());
            return true;
        }

        private void OnModuleState(ModuleStatePacket p)
        {
            ModuleState.Clear();            
            int n = p.Codes?.Length ?? 0;            
            for (int i = 0; i < n; i++)
            {
                bool inst = p?.Installed != null && i < p.Installed.Length && p.Installed[i];
                bool en = p?.Enabled != null && i < p.Enabled.Length && p.Enabled[i];
                if (p == null || p.Codes == null || p.Codes[i] == null) continue;
                ModuleState[p.Codes[i]] = (inst, en);
            }
            // Refresh the GUI if it's open so buttons reflect the new state.
            openModuleDialog?.RefreshStates();
        }

        private void OnEnergySync(EnergySyncPacket p)
        {
            LastEnergy = p.Energy;
            LastMaxEnergy = p.MaxEnergy;
            LastFlying = p.Flying;
        }
    }
}