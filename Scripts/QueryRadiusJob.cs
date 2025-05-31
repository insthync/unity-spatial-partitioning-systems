using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Insthync.SpatialPartitioningSystems
{
    [BurstCompile]
    public struct QueryRadiusJob : IJob
    {
        [ReadOnly] public NativeParallelMultiHashMap<int, SpatialObject> CellToObjects;
        public float3 QueryPosition;
        public float QueryRadius;
        public float CellSize;
        public float3 WorldMin;
        public int GridSizeX;
        public int GridSizeY;
        public int GridSizeZ;
        public NativeList<SpatialObject> Results;

        public void Execute()
        {
            float radiusSquared = QueryRadius * QueryRadius;
            int3 minCell = QueryFunctions.GetCellIndex(QueryPosition - new float3(QueryRadius), WorldMin, CellSize);
            int3 maxCell = QueryFunctions.GetCellIndex(QueryPosition + new float3(QueryRadius), WorldMin, CellSize);

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
                            if (!addedObjects.Contains(spatialObject.objectIndex))
                            {
                                float combinedRadius = QueryRadius + spatialObject.radius;
                                float combinedRadiusSq = combinedRadius * combinedRadius;

                                if (math.distancesq(QueryPosition, spatialObject.position) <= combinedRadiusSq)
                                {
                                    Results.Add(spatialObject);
                                    addedObjects.Add(spatialObject.objectIndex);
                                }
                            }
                        }
                        while (CellToObjects.TryGetNextValue(out spatialObject, ref iterator));
                    }
                }
            }

            addedObjects.Dispose();
        }
    }
}