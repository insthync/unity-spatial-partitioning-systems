using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Insthync.SpatialPartitioningSystems
{
    [BurstCompile]
    public struct QuerySquareJob : IJob
    {
        [ReadOnly] public NativeParallelMultiHashMap<int, SpatialObject> CellToObjects;
        public float3 QueryCenter;
        public float3 QueryExtents;
        public float CellSize;
        public float3 WorldMin;
        public int GridSizeX;
        public int GridSizeY;
        public int GridSizeZ;
        public NativeList<SpatialObject> Results;

        public void Execute()
        {
            float3 queryMin = QueryCenter - QueryExtents;
            float3 queryMax = QueryCenter + QueryExtents;

            int3 minCell = QueryFunctions.GetCellIndex(queryMin, WorldMin, CellSize);
            int3 maxCell = QueryFunctions.GetCellIndex(queryMax, WorldMin, CellSize);

            // Clamp to grid bounds
            minCell = math.max(minCell, 0);
            maxCell = math.min(maxCell, new int3(GridSizeX - 1, GridSizeY - 1, GridSizeZ - 1));

            var addedObjects = new NativeHashSet<int>(100, Allocator.Temp);

            for (int z = minCell.z; z <= maxCell.z; z++)
            {
                for (int y = minCell.y; y <= maxCell.y; y++)
                {
                    for (int x = minCell.x; x <= maxCell.x; x++)
                    {
                        int flatIndex = QueryFunctions.GetFlatIndex(new int3(x, y, z), GridSizeX, GridSizeY);
                        if (!CellToObjects.TryGetFirstValue(flatIndex, out SpatialObject spatialObject, out var it))
                            continue;
                        do
                        {
                            // Avoid adding the same object multiple times
                            if (addedObjects.Contains(spatialObject.objectIndex))
                                continue;

                            // Check if the object is inside the AABB, expanded by its radius
                            float3 position = spatialObject.position;
                            float radius = spatialObject.radius;
                            if (math.abs(position.x - QueryCenter.x) <= QueryExtents.x + radius &&
                                math.abs(position.y - QueryCenter.y) <= QueryExtents.y + radius &&
                                math.abs(position.z - QueryCenter.z) <= QueryExtents.z + radius)
                            {
                                Results.Add(spatialObject);
                                addedObjects.Add(spatialObject.objectIndex);
                            }
                        } while (CellToObjects.TryGetNextValue(out spatialObject, ref it));
                    }
                }
            }

            addedObjects.Dispose();
        }
    }
}