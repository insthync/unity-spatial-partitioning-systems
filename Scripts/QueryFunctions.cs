using Unity.Burst;
using Unity.Mathematics;

namespace Insthync.SpatialPartitioningSystems
{
    public static class QueryFunctions
    {
        [BurstCompile]
        public static int3 GetCellIndex(float3 position, float3 worldMin, float cellSize)
        {
            float3 relative = position - worldMin;
            return new int3(
                (int)(relative.x / cellSize),
                (int)(relative.y / cellSize),
                (int)(relative.z / cellSize)
            );
        }

        [BurstCompile]
        public static int GetFlatIndex(int3 index, int gridSizeX, int gridSizeY)
        {
            return index.x + gridSizeX * (index.y + gridSizeY * index.z);
        }
    }
}