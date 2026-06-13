using ProtoBuf;
using Vintagestory.API.MathTools;

namespace VEPowersuit.Network
{
    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class ToggleModulePacket
    {
        public string ModuleCode = "";
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
