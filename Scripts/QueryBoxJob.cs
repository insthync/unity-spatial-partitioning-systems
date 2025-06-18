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
        public bool DisableXAxis;
        public bool DisableYAxis;
        public bool DisableZAxis;
        public NativeList<SpatialObject> Results;

        public void Execute()
        {
            QueryCenter = new float3(
                DisableXAxis ? 0 : QueryCenter.x,
                DisableYAxis ? 0 : QueryCenter.y,
                DisableZAxis ? 0 : QueryCenter.z);
            QueryExtents = new float3(
                DisableXAxis ? 0 : QueryExtents.x,
                DisableYAxis ? 0 : QueryExtents.y,
                DisableZAxis ? 0 : QueryExtents.z);
            float3 queryMin = QueryCenter - QueryExtents;
            float3 queryMax = QueryCenter + QueryExtents;

            int3 minCell = QueryFunctions.GetCellIndex(queryMin, WorldMin, CellSize, DisableXAxis, DisableYAxis, DisableZAxis);
            int3 maxCell = QueryFunctions.GetCellIndex(queryMax, WorldMin, CellSize, DisableXAxis, DisableYAxis, DisableZAxis);

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

                            // Point-in-box test
                            if (spatialObject.position.x >= queryMin.x && spatialObject.position.x <= queryMax.x &&
                                spatialObject.position.y >= queryMin.y && spatialObject.position.y <= queryMax.y &&
                                spatialObject.position.z >= queryMin.z && spatialObject.position.z <= queryMax.z)
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