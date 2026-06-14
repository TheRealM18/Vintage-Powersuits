using System.Collections.Generic;
using VEPowersuit.Items;
using VEPowersuit.Modules;
using VEPowersuit.Network;
using VEPowersuit.Systems;
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

        private ICoreServerAPI sapi;
        private ICoreClientAPI capi;
        private IServerNetworkChannel serverChannel;
        private IClientNetworkChannel clientChannel;

        // Static client-channel ref so block entities (which don't hold the mod
        // system instance) can send the install request through our own stable
        // network channel rather than the version-finicky block-entity packet API.
        private static IClientNetworkChannel staticClientChannel;

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

        // ---------------- SERVER ----------------
        public override void StartServerSide(ICoreServerAPI api)
        {
            sapi = api;
            serverChannel = api.Network.RegisterChannel(Channel)
                .RegisterMessageType<ToggleModulePacket>()
                .RegisterMessageType<ToggleFlightPacket>()
                .RegisterMessageType<EnergySyncPacket>()
                .RegisterMessageType<InstallModulePacket>()
                .SetMessageHandler<ToggleModulePacket>(OnToggleModule)
                .SetMessageHandler<ToggleFlightPacket>(OnToggleFlight)
                .SetMessageHandler<InstallModulePacket>(OnInstallModule);

            // 1Hz drain/maintenance tick.
            api.Event.RegisterGameTickListener(OnServerTick, 1000);
            api.Event.PlayerDisconnect += p => StopFlight(p);
        }

        private ItemSlot GetChestSlot(IPlayer player)
        {
            // Body-armor slot. The chestplate is flagged isCore in JSON.
            var inv = player.InventoryManager.GetOwnInventory(GlobalConstants.characterInvClassName);
            if (inv == null) return null;
            foreach (var slot in inv)
            {
                if (slot?.Itemstack?.Collectible is ItemVEPowersuit pa && pa.IsCore)
                    return slot;
            }
            return null;
        }

        private void OnServerTick(float dt)
        {
            foreach (IPlayer player in sapi.World.AllOnlinePlayers)
            {
                var slot = GetChestSlot(player);
                if (slot == null) { StopFlight(player); continue; }
                var stack = slot.Itemstack;

                bool flying = flyingPlayers.Contains(player.PlayerUID);

                // Drain per-tick modules.
                foreach (var kv in ModuleRegistry.All)
                {
                    var mod = kv.Value;
                    if (mod.EnergyPerTick <= 0) continue;
                    if (!EnergyStore.HasModule(stack, kv.Key)) continue;

                    // Flight only drains while actually flying.
                    if (kv.Key == ModuleRegistry.Flight && !flying) continue;
                    // Sprint assist only drains while sprinting.
                    if (kv.Key == ModuleRegistry.SprintAssist &&
                        !player.Entity.Controls.Sprint) continue;

                    if (!EnergyStore.TryConsume(stack, mod.EnergyPerTick))
                    {
                        // Out of power: kill the dependent capability.
                        if (kv.Key == ModuleRegistry.Flight) StopFlight(player);
                    }
                }

                // Apply sprint-assist movement bonus.
                ApplySprintAssist(player, stack);

                slot.MarkDirty();
                SyncEnergy(player, stack, flying);
            }
        }

        private void ApplySprintAssist(IPlayer player, ItemStack stack)
        {
            bool active = EnergyStore.HasModule(stack, ModuleRegistry.SprintAssist)
                          && player.Entity.Controls.Sprint
                          && EnergyStore.GetEnergy(stack) > 0;

            const string statCode = "vepowersuit_sprint";
            float bonus = active ? 0.40f : 0f; // +40% walk speed while sprinting
            player.Entity.Stats.Set("walkspeed", statCode, bonus, true);
        }

        private void OnToggleFlight(IServerPlayer player, ToggleFlightPacket packet)
        {
            var slot = GetChestSlot(player);
            if (slot == null) return;
            var stack = slot.Itemstack;

            if (!EnergyStore.HasModule(stack, ModuleRegistry.Flight)) return;

            if (packet.WantFlying && EnergyStore.GetEnergy(stack) > 0)
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
            bool now = !EnergyStore.HasModule(stack, packet.ModuleCode);
            EnergyStore.SetModule(stack, packet.ModuleCode, now);
            slot.MarkDirty();
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

            if (sapi.World.BlockAccessor.GetBlockEntity(pos)
                is Blocks.BlockEntityModuleInstaller be)
            {
                be.TryInstall(out _);
            }
        }

        private void SyncEnergy(IPlayer player, ItemStack stack, bool flying)
        {
            if (player is IServerPlayer sp)
                serverChannel.SendPacket(new EnergySyncPacket
                {
                    Energy = EnergyStore.GetEnergy(stack),
                    MaxEnergy = EnergyStore.GetMaxEnergy(stack),
                    Flying = flying
                }, sp);
        }

        // ---------------- CLIENT ----------------
        public int LastEnergy { get; private set; }
        public int LastMaxEnergy { get; private set; }
        public bool LastFlying { get; private set; }

        public override void StartClientSide(ICoreClientAPI api)
        {
            capi = api;
            clientChannel = api.Network.RegisterChannel(Channel)
                .RegisterMessageType<ToggleModulePacket>()
                .RegisterMessageType<ToggleFlightPacket>()
                .RegisterMessageType<EnergySyncPacket>()
                .RegisterMessageType<InstallModulePacket>()
                .SetMessageHandler<EnergySyncPacket>(OnEnergySync);
            staticClientChannel = clientChannel;

            api.Input.RegisterHotKey("vepowersuit_flight", Lang.Get("vepowersuit:hotkey-flight"),
                GlKeys.R, HotkeyType.CharacterControls);
            api.Input.SetHotKeyHandler("vepowersuit_flight", OnFlightHotkey);

            api.Input.RegisterHotKey("vepowersuit_gui", Lang.Get("vepowersuit:hotkey-gui"),
                GlKeys.U, HotkeyType.GUIOrOtherControls);
            api.Input.SetHotKeyHandler("vepowersuit_gui", OnGuiHotkey);
        }

        private bool OnFlightHotkey(KeyCombination comb)
        {
            // Toggle: ask server for the opposite of current state.
            clientChannel.SendPacket(new ToggleFlightPacket { WantFlying = !LastFlying });
            return true;
        }

        private bool OnGuiHotkey(KeyCombination comb)
        {
            new Gui.GuiDialogModules(capi, this, clientChannel).TryOpen();
            return true;
        }

        private void OnEnergySync(EnergySyncPacket p)
        {
            LastEnergy = p.Energy;
            LastMaxEnergy = p.MaxEnergy;
            LastFlying = p.Flying;
        }
    }
}