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