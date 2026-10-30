// Unity 6000.1-6000.3 engine types (compile checks only) that are missing from the 6000.0.58 player assemblies this
// toolchain compiles against, but that SRP 17.3 (Core RP / GPUDriven / URP) uses in its DECLARATIONS (field types,
// member signatures), so they cannot be handled by stripping method bodies (UccTool BodyStripper).
// Provided here as NEW types with the public/internal surface copied from UnityCsReference 6000.3.25f1:
//   Runtime/Export/Scripting/UnityEngineObject.bindings.cs   (UnityEngine.EntityId; the conversions to/from the
//                                                            [Obsolete] UnityEngine.InstanceID are left out)
//   Runtime/Export/Graphics/GraphicsEnums.cs                 (UnityEngine.Rendering.ShadingRate* enums)
//   Runtime/Export/Rendering/GPUDrivenRendering.bindings.cs  (internal GPU-driven structs, visible to GPUDriven only)
// Only add types here, never members of existing engine types (setup fails on duplicates).
using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

[assembly: InternalsVisibleTo("Unity.RenderPipelines.GPUDriven.Runtime")]

namespace UnityEngine
{
    [StructLayout(LayoutKind.Sequential, Size = 4)]
    [Serializable]
    public struct EntityId : IEquatable<EntityId>, IComparable<EntityId>
    {
        [SerializeField]
        int m_Data;

        public static EntityId None => default;
        public override bool Equals(object obj) => obj is EntityId other && Equals(other);
        public bool Equals(EntityId other) => m_Data == other.m_Data;
        public int CompareTo(EntityId other) => m_Data.CompareTo(other.m_Data);
        public static bool operator ==(EntityId left, EntityId right) => left.Equals(right);
        public static bool operator !=(EntityId left, EntityId right) => !left.Equals(right);
        public static bool operator <(EntityId left, EntityId right) => left.m_Data < right.m_Data;
        public static bool operator >(EntityId left, EntityId right) => left.m_Data > right.m_Data;
        public static bool operator <=(EntityId left, EntityId right) => left.m_Data <= right.m_Data;
        public static bool operator >=(EntityId left, EntityId right) => left.m_Data >= right.m_Data;
        public override int GetHashCode() => m_Data;
        public bool IsValid() => this != None;
        public bool Equals(int other) => m_Data == other;
        public static implicit operator int(EntityId entityId) => entityId.m_Data;
        public static implicit operator EntityId(int intValue) => new EntityId { m_Data = intValue };
        public override string ToString() => m_Data.ToString();
        public string ToString(string format) => m_Data.ToString(format);
    }
}

namespace UnityEngine.Rendering
{
    public enum ShadingRateFragmentSize
    {
        FragmentSize1x1,
        FragmentSize1x2,
        FragmentSize2x1,
        FragmentSize2x2,
        FragmentSize1x4,
        FragmentSize4x1,
        FragmentSize2x4,
        FragmentSize4x2,
        FragmentSize4x4,
    }

    public enum ShadingRateCombinerStage
    {
        Primitive,
        Fragment,
    }

    public enum ShadingRateCombiner
    {
        Keep,
        Override,
        Min,
        Max,
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct GPUDrivenRendererMeshLodData
    {
        public int forceLod;
        public float lodSelectionBias;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct GPUDrivenMeshLodInfo
    {
        public int levelCount;
        public float lodSlope;
        public float lodBias;
        public bool lodSelectionActive => levelCount > 1;
    }
}
