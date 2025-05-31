using Unity.Mathematics;

namespace Insthync.SpatialPartitioningSystems
{
    public struct SpatialObject
    {
        public byte objectType;
        public uint objectId;
        public float3 position;
        public float radius;
        public int objectIndex;
    }
}