using Unity.Jobs;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Burst;
using UnityEngine;
using System.Collections.Generic;

namespace Insthync.SpatialPartitioningSystems
{
    public class JobifiedGridSpatialPartitioningSystem : System.IDisposable
    {
        private NativeArray<SpatialObject> _spatialObjects;
        private NativeParallelMultiHashMap<int, SpatialObject> _cellToObjects;

        private readonly int _gridSizeX;
        private readonly int _gridSizeY;
        private readonly int _gridSizeZ;
        private readonly float _cellSize;
        private readonly float3 _worldMin;

        public JobifiedGridSpatialPartitioningSystem(Bounds bounds, float cellSize, int maxObjects)
        {
            _cellSize = cellSize;
            _worldMin = bounds.min;

            _gridSizeX = Mathf.CeilToInt(bounds.size.x / cellSize);
            _gridSizeY = Mathf.CeilToInt(bounds.size.y / cellSize);
            _gridSizeZ = Mathf.CeilToInt(bounds.size.z / cellSize);

            int totalCells = _gridSizeX * _gridSizeY * _gridSizeZ;
            _cellToObjects = new NativeParallelMultiHashMap<int, SpatialObject>(maxObjects * 8, Allocator.Persistent); // Multiplied by 8 because objects can span multiple cells
        }

        public void Dispose()
        {
            if (_spatialObjects.IsCreated)
                _spatialObjects.Dispose();

            if (_cellToObjects.IsCreated)
                _cellToObjects.Dispose();
        }

        ~JobifiedGridSpatialPartitioningSystem()
        {
            Dispose();
        }

        [BurstCompile]
        private struct UpdateGridJob : IJobParallelFor
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

        [BurstCompile]
        private struct QueryRadiusJob : IJob
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

        public void UpdateGrid(List<SpatialObject> spatialObjects)
        {
            // Convert to SpatialObjects
            if (_spatialObjects.IsCreated) _spatialObjects.Dispose();
            _spatialObjects = new NativeArray<SpatialObject>(spatialObjects.Count, Allocator.TempJob);

            for (int i = 0; i < spatialObjects.Count; i++)
            {
                SpatialObject spatialObject = spatialObjects[i];
                spatialObject.objectIndex = i;
                _spatialObjects[i] = spatialObject;
            }

            // Clear previous grid data
            _cellToObjects.Clear();

            // Create and schedule update job
            var updateJob = new UpdateGridJob
            {
                Objects = _spatialObjects,
                CellToObjects = _cellToObjects.AsParallelWriter(),
                CellSize = _cellSize,
                WorldMin = _worldMin,
                GridSizeX = _gridSizeX,
                GridSizeY = _gridSizeY,
                GridSizeZ = _gridSizeZ
            };

            var handle = updateJob.Schedule(_spatialObjects.Length, 64);
            handle.Complete();
        }

        public NativeList<SpatialObject> QueryRadius(Vector3 position, float radius)
        {
            var results = new NativeList<SpatialObject>(Allocator.TempJob);

            var queryJob = new QueryRadiusJob
            {
                CellToObjects = _cellToObjects,
                QueryPosition = position,
                QueryRadius = radius,
                CellSize = _cellSize,
                WorldMin = _worldMin,
                GridSizeX = _gridSizeX,
                GridSizeY = _gridSizeY,
                GridSizeZ = _gridSizeZ,
                Results = results
            };

            queryJob.Run();
            return results;
        }
    }
}