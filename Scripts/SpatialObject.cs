using Unity.Mathematics;

namespace Insthync.SpatialPartitioningSystems
{
    public struct SpatialObject
    {
        public byte objectType;
        public uint objectId;
        public float3 position;
        public int objectIndex;
        public SpatialObjectShape shape;
        // Sphere
        public float radius;
        // Box
        public float3 extents;
    }
}