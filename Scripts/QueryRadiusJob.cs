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
            int3 minCell = GetCellIndex(QueryPosition - new float3(QueryRadius));
            int3 maxCell = GetCellIndex(QueryPosition + new float3(QueryRadius));

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
                        int flatIndex = GetFlatIndex(new int3(x, y, z));

                        if (CellToObjects.TryGetFirstValue(flatIndex, out SpatialObject spatialObject, out var iterator))
                        {
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
            }

            addedObjects.Dispose();
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