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
        public float3 QueryCenter;
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
            QueryCenter = new float3(
                DisableXAxis ? 0 : QueryCenter.x,
                DisableYAxis ? 0 : QueryCenter.y,
                DisableZAxis ? 0 : QueryCenter.z);
            if (QueryRadius < 0f)
                return;
            float radiusSquared = QueryRadius * QueryRadius;
            float3 queryExtentVec = new float3(QueryRadius);
            float3 queryMin = QueryCenter - queryExtentVec;
            float3 queryMax = QueryCenter + queryExtentVec;
            int3 minCell = QueryFunctions.GetCellIndex(queryMin, WorldMin, CellSize, DisableXAxis, DisableYAxis, DisableZAxis);
            int3 maxCell = QueryFunctions.GetCellIndex(queryMax, WorldMin, CellSize, DisableXAxis, DisableYAxis, DisableZAxis);

            // Objects outside the configured bounds are stored in the nearest edge cell.
            int3 lastCell = new int3(GridSizeX - 1, GridSizeY - 1, GridSizeZ - 1);
            minCell = math.clamp(minCell, int3.zero, lastCell);
            maxCell = math.clamp(maxCell, int3.zero, lastCell);

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
                            // Each object is stored in exactly one cell.
                            if (math.distancesq(QueryCenter, spatialObject.position) <= radiusSquared)
                            {
                                Results.Add(spatialObject);
                            }
                        }
                        while (CellToObjects.TryGetNextValue(out spatialObject, ref iterator));
                    }
                }
            }
        }
    }
}
