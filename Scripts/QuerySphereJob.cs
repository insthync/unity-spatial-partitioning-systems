using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Insthync.SpatialPartitioningSystems
{
    [BurstCompile]
    public struct QuerySphereJob : IJob
    {
        [ReadOnly] public NativeParallelMultiHashMap<int, SpatialObject> CellToObjects;
        public float3 QueryPosition;
        public float QueryRadius;
        public float CellSize;
        public float3 WorldMin;
        public int GridSizeX;
        public int GridSizeY;
        public int GridSizeZ;
        public bool DisableXAxis;
        public bool DisableYAxis;
        public bool DisableZAxis;
        public NativeList<SpatialObject> Results;

        public void Execute()
        {
            QueryPosition = new float3(
                DisableXAxis ? 0 : QueryPosition.x,
                DisableYAxis ? 0 : QueryPosition.y,
                DisableZAxis ? 0 : QueryPosition.z);
            float radiusSquared = QueryRadius * QueryRadius;
            float3 queryExtentVec = new float3(QueryRadius);
            int3 minCell = QueryFunctions.GetCellIndex(QueryPosition - queryExtentVec, WorldMin, CellSize, DisableXAxis, DisableYAxis, DisableZAxis);
            int3 maxCell = QueryFunctions.GetCellIndex(QueryPosition + queryExtentVec, WorldMin, CellSize, DisableXAxis, DisableYAxis, DisableZAxis);

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

                            // Check if the object is inside the query radius, expanded by its radius
                            if (math.distancesq(QueryPosition, spatialObject.position) <= radiusSquared)
                            {
                                Results.Add(spatialObject);
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