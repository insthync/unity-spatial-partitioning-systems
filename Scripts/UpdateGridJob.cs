using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Insthync.SpatialPartitioningSystems
{
    [BurstCompile]
    public struct UpdateGridJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<SpatialObject> Objects;
        public NativeParallelMultiHashMap<int, SpatialObject>.ParallelWriter CellToObjects;
        public float CellSize;
        public float3 WorldMin;
        public int GridSizeX;
        public int GridSizeY;
        public int GridSizeZ;

        public void Execute(int index)
        {
            SpatialObject obj = Objects[index];

            // Calculate the cells this object could overlap with
            int3 minCell = GetCellIndex(obj.position - new float3(obj.radius));
            int3 maxCell = GetCellIndex(obj.position + new float3(obj.radius));

            // Clamp to grid bounds
            minCell = math.max(minCell, 0);
            maxCell = math.min(maxCell, new int3(GridSizeX - 1, GridSizeY - 1, GridSizeZ - 1));

            // Add object to all cells it overlaps
            for (int z = minCell.z; z <= maxCell.z; z++)
            {
                for (int y = minCell.y; y <= maxCell.y; y++)
                {
                    for (int x = minCell.x; x <= maxCell.x; x++)
                    {
                        int flatIndex = GetFlatIndex(new int3(x, y, z));
                        CellToObjects.Add(flatIndex, obj);
                    }
                }
            }
        }

        private int3 GetCellIndex(float3 position)
        {
            float3 relative = position - WorldMin;
            return new int3(
                (int)(relative.x / CellSize),
                (int)(relative.y / CellSize),
                (int)(relative.z / CellSize)
            );
        }

        private int GetFlatIndex(int3 index)
        {
            return index.x + GridSizeX * (index.y + GridSizeY * index.z);
        }
    }
}