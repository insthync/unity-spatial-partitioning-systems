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
            int3 minCell = QueryFunctions.GetCellIndex(obj.position - new float3(obj.radius), WorldMin, CellSize);
            int3 maxCell = QueryFunctions.GetCellIndex(obj.position + new float3(obj.radius), WorldMin, CellSize);

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
                        CellToObjects.Add(QueryFunctions.GetFlatIndex(new int3(x, y, z), GridSizeX, GridSizeY), obj);
                    }
                }
            }
        }
    }
}