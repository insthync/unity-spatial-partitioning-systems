using Unity.Jobs;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Burst;
using UnityEngine;
using System.Collections.Generic;

namespace Insthync.SpatialPartitioningSystems
{
    public class JobifiedGridSpatialPartitioningSystem
    {
        private NativeArray<float3> _objectPositions;
        private NativeParallelMultiHashMap<int, int> _cellToObjects; // Maps cell index to object indices

        private int _gridSizeX;
        private int _gridSizeY;
        private int _gridSizeZ;
        private float _cellSize;
        private float3 _worldMin;

        public JobifiedGridSpatialPartitioningSystem(Bounds bounds, float cellSize, int maxObjects)
        {
            _cellSize = cellSize;
            _worldMin = bounds.min;

            _gridSizeX = Mathf.CeilToInt(bounds.size.x / cellSize);
            _gridSizeY = Mathf.CeilToInt(bounds.size.y / cellSize);
            _gridSizeZ = Mathf.CeilToInt(bounds.size.z / cellSize);

            int totalCells = _gridSizeX * _gridSizeY * _gridSizeZ;
            _cellToObjects = new NativeParallelMultiHashMap<int, int>(maxObjects, Allocator.Persistent);
        }

        ~JobifiedGridSpatialPartitioningSystem()
        {
            if (_objectPositions.IsCreated)
                _objectPositions.Dispose();

            if (_cellToObjects.IsCreated)
                _cellToObjects.Dispose();
        }

        [BurstCompile]
        private struct UpdateGridJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<float3> Positions;
            public NativeParallelMultiHashMap<int, int>.ParallelWriter CellToObjects;
            public float CellSize;
            public float3 WorldMin;
            public int GridSizeX;
            public int GridSizeY;
            public int GridSizeZ;

            public void Execute(int index)
            {
                float3 position = Positions[index];
                int3 cellIndex = GetCellIndex(position);

                if (IsValidCellIndex(cellIndex))
                {
                    int flatIndex = GetFlatIndex(cellIndex);
                    CellToObjects.Add(flatIndex, index);
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

            private bool IsValidCellIndex(int3 index)
            {
                return index.x >= 0 && index.x < GridSizeX &&
                       index.y >= 0 && index.y < GridSizeY &&
                       index.z >= 0 && index.z < GridSizeZ;
            }

            private int GetFlatIndex(int3 index)
            {
                return index.x + GridSizeX * (index.y + GridSizeY * index.z);
            }
        }

        [BurstCompile]
        private struct QueryRadiusJob : IJob
        {
            [ReadOnly] public NativeParallelMultiHashMap<int, int> CellToObjects;
            [ReadOnly] public NativeArray<float3> Positions;
            public float3 QueryPosition;
            public float QueryRadius;
            public float CellSize;
            public float3 WorldMin;
            public int GridSizeX;
            public int GridSizeY;
            public int GridSizeZ;
            public NativeList<int> Results;

            public void Execute()
            {
                float radiusSquared = QueryRadius * QueryRadius;
                int3 minCell = GetCellIndex(QueryPosition - new float3(QueryRadius));
                int3 maxCell = GetCellIndex(QueryPosition + new float3(QueryRadius));

                // Clamp to grid bounds
                minCell = math.max(minCell, 0);
                maxCell = math.min(maxCell, new int3(GridSizeX - 1, GridSizeY - 1, GridSizeZ - 1));

                for (int z = minCell.z; z <= maxCell.z; ++z)
                {
                    for (int y = minCell.y; y <= maxCell.y; ++y)
                    {
                        for (int x = minCell.x; x <= maxCell.x; ++x)
                        {
                            int flatIndex = GetFlatIndex(new int3(x, y, z));

                            NativeParallelMultiHashMapIterator<int> iterator;
                            int objectIndex;

                            if (CellToObjects.TryGetFirstValue(flatIndex, out objectIndex, out iterator))
                            {
                                do
                                {
                                    float3 objectPosition = Positions[objectIndex];
                                    if (math.distancesq(QueryPosition, objectPosition) <= radiusSquared)
                                    {
                                        Results.Add(objectIndex);
                                    }
                                }
                                while (CellToObjects.TryGetNextValue(out objectIndex, ref iterator));
                            }
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

        public void UpdateGrid(List<Vector3> positions)
        {
            // Convert positions to NativeArray
            if (_objectPositions.IsCreated) _objectPositions.Dispose();
            _objectPositions = new NativeArray<float3>(positions.Count, Allocator.TempJob);

            for (int i = 0; i < positions.Count; i++)
            {
                _objectPositions[i] = positions[i];
            }

            // Clear previous grid data
            _cellToObjects.Clear();

            // Create and schedule update job
            var updateJob = new UpdateGridJob
            {
                Positions = _objectPositions,
                CellToObjects = _cellToObjects.AsParallelWriter(),
                CellSize = _cellSize,
                WorldMin = _worldMin,
                GridSizeX = _gridSizeX,
                GridSizeY = _gridSizeY,
                GridSizeZ = _gridSizeZ
            };

            var handle = updateJob.Schedule(positions.Count, 64);
            handle.Complete();
        }

        public NativeList<int> QueryRadius(Vector3 position, float radius)
        {
            var results = new NativeList<int>(Allocator.TempJob);

            var queryJob = new QueryRadiusJob
            {
                CellToObjects = _cellToObjects,
                Positions = _objectPositions,
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
