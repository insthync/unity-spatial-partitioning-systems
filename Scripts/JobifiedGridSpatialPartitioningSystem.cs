using Unity.Jobs;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Burst;
using UnityEngine;
using System;

namespace Insthync.SpatialPartitioningSystems
{
    [BurstCompile]
    public class JobifiedGridSpatialPartitioningSystem : System.IDisposable
    {
        private NativeList<SpatialObject> _spatialObjects;
        private NativeParallelMultiHashMap<int, SpatialObject> _cellToObjects;

        private readonly int _gridSizeX;
        private readonly int _gridSizeY;
        private readonly int _gridSizeZ;
        private readonly bool _disableXAxis;
        private readonly bool _disableYAxis;
        private readonly bool _disableZAxis;
        private readonly float _cellSize;
        private readonly float3 _worldMin;
        private JobHandle _updateHandle;
        private JobHandle _jobHandle;

        public JobifiedGridSpatialPartitioningSystem(Bounds bounds, float cellSize, int maxObjects, bool disableXAxis, bool disableYAxis, bool disableZAxis)
        {
            if (cellSize <= 0f || float.IsNaN(cellSize) || float.IsInfinity(cellSize))
                throw new ArgumentOutOfRangeException(nameof(cellSize));

            _cellSize = cellSize;

            _disableXAxis = disableXAxis;
            _disableYAxis = disableYAxis;
            _disableZAxis = disableZAxis;

            _gridSizeX = disableXAxis ? 1 : Mathf.Max(1, Mathf.CeilToInt(bounds.size.x / cellSize));
            _gridSizeY = disableYAxis ? 1 : Mathf.Max(1, Mathf.CeilToInt(bounds.size.y / cellSize));
            _gridSizeZ = disableZAxis ? 1 : Mathf.Max(1, Mathf.CeilToInt(bounds.size.z / cellSize));

            _worldMin = new float3(
                disableXAxis ? 0 : bounds.min.x,
                disableYAxis ? 0 : bounds.min.y,
                disableZAxis ? 0 : bounds.min.z);

            _spatialObjects = new NativeList<SpatialObject>(1024, Allocator.Persistent);
            _cellToObjects = new NativeParallelMultiHashMap<int, SpatialObject>(Mathf.Max(1, maxObjects), Allocator.Persistent);
        }

        public void Dispose()
        {
            Complete();
            if (_spatialObjects.IsCreated)
                _spatialObjects.Dispose();

            if (_cellToObjects.IsCreated)
                _cellToObjects.Dispose();
        }

        public void ClearObjects()
        {
            Complete();
            _spatialObjects.Clear();
        }

        public void AddObjectToGrid(SpatialObject spatialObject)
        {
            int index = _spatialObjects.Length;
            float3 position = spatialObject.position;
            if (_disableXAxis)
                position.x = 0f;
            if (_disableYAxis)
                position.y = 0f;
            if (_disableZAxis)
                position.z = 0f;
            spatialObject.position = position;
            spatialObject.objectIndex = index;
            _spatialObjects.Add(spatialObject);
        }

        public JobHandle UpdateGrid()
        {
            Complete();
            // Clear previous grid data
            _cellToObjects.Clear();
            if (_spatialObjects.Length > _cellToObjects.Capacity)
            {
                int capacity = _cellToObjects.Capacity;
                _cellToObjects.Capacity = capacity <= int.MaxValue / 2
                    ? Math.Max(_spatialObjects.Length, capacity * 2)
                    : _spatialObjects.Length;
            }

            // Create and schedule update job
            var updateJob = new UpdateGridJob
            {
                Objects = _spatialObjects,
                CellToObjects = _cellToObjects.AsParallelWriter(),
                CellSize = _cellSize,
                WorldMin = _worldMin,
                GridSizeX = _gridSizeX,
                GridSizeY = _gridSizeY,
                GridSizeZ = _gridSizeZ,
                DisableXAxis = _disableXAxis,
                DisableYAxis = _disableYAxis,
                DisableZAxis = _disableZAxis
            };

            _updateHandle = updateJob.Schedule(_spatialObjects.Length, 64);
            _jobHandle = _updateHandle;
            return _jobHandle;
        }

        public void Complete()
        {
            _jobHandle.Complete();
        }

        public JobHandle QuerySphere(Vector3 position, float radius, NativeList<SpatialObject> results)
        {
            var queryJob = new QuerySphereJob
            {
                CellToObjects = _cellToObjects,
                QueryCenter = position,
                QueryRadius = radius,
                CellSize = _cellSize,
                WorldMin = _worldMin,
                GridSizeX = _gridSizeX,
                GridSizeY = _gridSizeY,
                GridSizeZ = _gridSizeZ,
                DisableXAxis = _disableXAxis,
                DisableYAxis = _disableYAxis,
                DisableZAxis = _disableZAxis,
                Results = results,
            };

            JobHandle queryHandle = queryJob.Schedule(_updateHandle);
            _jobHandle = JobHandle.CombineDependencies(_jobHandle, queryHandle);
            return _jobHandle;
        }

        public JobHandle QueryBox(Vector3 center, Vector3 extents, NativeList<SpatialObject> results)
        {
            var queryJob = new QueryBoxJob
            {
                CellToObjects = _cellToObjects,
                QueryCenter = center,
                QueryExtents = extents,
                CellSize = _cellSize,
                WorldMin = _worldMin,
                GridSizeX = _gridSizeX,
                GridSizeY = _gridSizeY,
                GridSizeZ = _gridSizeZ,
                DisableXAxis = _disableXAxis,
                DisableYAxis = _disableYAxis,
                DisableZAxis = _disableZAxis,
                Results = results,
            };

            JobHandle queryHandle = queryJob.Schedule(_updateHandle);
            _jobHandle = JobHandle.CombineDependencies(_jobHandle, queryHandle);
            return _jobHandle;
        }
    }
}
