using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Insthync.SpatialPartitioningSystems
{
    [BurstCompile]
    public struct QueryBoxJob : IJob
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
                        if (!CellToObjects.TryGetFirstValue(flatIndex, out SpatialObject spatialObject, out var iterator))
                            continue;
                        do
                        {
                            // Avoid adding the same object multiple times
                            if (!addedObjects.Add(spatialObject.objectIndex))
                                continue;

                            switch (spatialObject.shape)
                            {
                                case SpatialObjectShape.Sphere:
                                    float3 position = spatialObject.position;
                                    float radius = spatialObject.radius;
                                    float3 closestPoint = math.clamp(spatialObject.position, queryMin, queryMax);
                                    float distSq = math.distancesq(spatialObject.position, closestPoint);
                                    if (distSq <= radius * radius)
                                    {
                                        Results.Add(spatialObject);
                                    }
                                    break;
                                case SpatialObjectShape.Box:
                                    // Calculate box min and max
                                    float3 boxMin = spatialObject.position - spatialObject.extents;
                                    float3 boxMax = spatialObject.position + spatialObject.extents;

                                    // AABB-AABB intersection check
                                    if (!(boxMax.x < queryMin.x || boxMin.x > queryMax.x ||
                                          boxMax.y < queryMin.y || boxMin.y > queryMax.y ||
                                          boxMax.z < queryMin.z || boxMin.z > queryMax.z))
                                    {
                                        Results.Add(spatialObject);
                                    }
                                    break;
                            }
                        } while (CellToObjects.TryGetNextValue(out spatialObject, ref iterator));
                    }
                }
            }

            addedObjects.Dispose();
        }
    }
}