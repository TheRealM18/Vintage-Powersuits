using ProtoBuf;
using Vintagestory.API.MathTools;

namespace VEPowersuit.Network
{
    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class ToggleModulePacket
    {
        public string ModuleCode = "";
        // Explicit desired enabled-state. Avoids desync from blind server-side
        // flips when clicks arrive out of order.
        public bool DesiredOn = true;
    }

    /// <summary>
    /// Server -> client: the current installed+enabled state of every module on
    /// the worn suit, so the GUI can render buttons that reflect reality (and
    /// only show installed modules as toggleable).
    /// </summary>
    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class ModuleStatePacket
    {
        // Parallel arrays keep the protobuf simple and order-stable.
        public string[] Codes = System.Array.Empty<string>();
        public bool[] Installed = System.Array.Empty<bool>();
        public bool[] Enabled = System.Array.Empty<bool>();
    }

    /// <summary>Client -> server: open request, so the server replies with current ModuleStatePacket.</summary>
    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class RequestModuleStatePacket
    {
    }

    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class ToggleFlightPacket
    {
        public bool WantFlying;
    }

    /// <summary>Server -> client, so the HUD can show live energy without reading the slot every frame.</summary>
    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class EnergySyncPacket
    {
        public int Energy;
        public int MaxEnergy;
        public bool Flying;
    }

    /// <summary>Client -> server: request the installer block at Pos to install its module.</summary>
    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class InstallModulePacket
    {
        public int X;
        public int Y;
        public int Z;
    }
}
