/* https://github.com/artnas/Unity-Plane-Mesh-Splitter */

using System;
using System.Collections.Generic;
using System.Linq;
using MeshSplit.Scripts.Utilities;
using UnityEngine;
using UnityEngine.Rendering;

namespace MeshSplit.Scripts
{
    public class MeshSplitter
    {
        private static readonly MeshUpdateFlags MeshUpdateFlags = MeshUpdateFlags.DontNotifyMeshUsers 
                                                                  | MeshUpdateFlags.DontValidateIndices 
                                                                  | MeshUpdateFlags.DontRecalculateBounds 
                                                                  | MeshUpdateFlags.DontResetBoneBounds;
        
        private readonly MeshSplitParameters _parameters;
        private Mesh _sourceMesh;
        private readonly bool _verbose;

        private Dictionary<Vector3Int, List<int>> _pointIndicesMap;
        
        private byte[] _vertexData;
        private VertexAttributeDescriptor[] _sourceMeshVertexAttributes;

        public MeshSplitter(MeshSplitParameters parameters, bool verbose)
        {
            _parameters = parameters;
            _verbose = verbose;
        }

        public List<(Vector3Int gridPoint, Mesh mesh)> Split(Mesh mesh)
        {
            SetMesh(mesh);

            if (_verbose) PerformanceMonitor.Start("CreatePointIndicesMap");
            CreatePointIndicesMap();
            if (_verbose) PerformanceMonitor.Stop("CreatePointIndicesMap");

            if (_verbose) PerformanceMonitor.Start("CreateChildMeshes");
            var childMeshes = CreateChildMeshes();
            if (_verbose) PerformanceMonitor.Stop("CreateChildMeshes");
            
            return childMeshes;
        }

        private void SetMesh(Mesh mesh)
        {
            _sourceMesh = mesh;
            
            // get raw mesh vertex data
            var buffer = _sourceMesh.GetVertexBuffer(0);
            _vertexData = new byte[_sourceMesh.GetVertexBufferStride(0) * _sourceMesh.vertexCount];
            buffer.GetData(_vertexData);
            buffer.Dispose();
            
            // get mesh vertex attributes
            _sourceMeshVertexAttributes = _sourceMesh.GetVertexAttributes();
        }

        private void CreatePointIndicesMap()
        {
            // Create a list of triangle indices from our mesh for every grid node
            _pointIndicesMap = new Dictionary<Vector3Int, List<int>>();

            var meshIndices = _sourceMesh.triangles;
            var meshVertices = _sourceMesh.vertices;

            for (var i = 0; i < meshIndices.Length; i += 3)
            {
                // middle of the current triangle (average of its 3 verts).
                var triangleCenter = (meshVertices[meshIndices[i]] + meshVertices[meshIndices[i + 1]] + meshVertices[meshIndices[i + 2]]) / 3;
                
                // calculate coordinates of the closest grid node.
                // ignore an axis (set it to 0) if its not enabled
                var gridPos = new Vector3Int(
                    _parameters.SplitAxes.x ? Mathf.FloorToInt(Mathf.Floor(triangleCenter.x / _parameters.GridSize) * _parameters.GridSize * MeshSplitController.GridSizeMultiplier) : 0,
                    _parameters.SplitAxes.y ? Mathf.FloorToInt(Mathf.Floor(triangleCenter.y / _parameters.GridSize) * _parameters.GridSize * MeshSplitController.GridSizeMultiplier) : 0,
                    _parameters.SplitAxes.z ? Mathf.FloorToInt(Mathf.Floor(triangleCenter.z / _parameters.GridSize) * _parameters.GridSize * MeshSplitController.GridSizeMultiplier) : 0
                );

                if (!_pointIndicesMap.TryGetValue(gridPos, out var indicesList))
                {
                    indicesList = new List<int>();
                    _pointIndicesMap.TryAdd(gridPos, indicesList);
                }
                
                // add these triangle indices to the list
                indicesList.Add(meshIndices[i]);
                indicesList.Add(meshIndices[i + 1]);
                indicesList.Add(meshIndices[i + 2]);
            }
        }

        private List<(Vector3Int gridPoint, Mesh mesh)> CreateChildMeshes()
        {
            var subMeshBuilder = new SubMeshBuilder(_pointIndicesMap, _vertexData, _sourceMesh.GetVertexBufferStride(0), _sourceMeshVertexAttributes);
            var meshDataArray = subMeshBuilder.Build(_sourceMesh);

            var meshes = new List<Mesh>(meshDataArray.Length);
            var gridPoints = _pointIndicesMap.Keys.ToArray();
            
            // create a new mesh for each grid point
            foreach (var gridPoint in gridPoints)
            {
                meshes.Add(new Mesh
                {
                    name = $"SubMesh {gridPoint}",
                });
            }

            // write each mesh data to its corresponding mesh
            Mesh.ApplyAndDisposeWritableMeshData(meshDataArray, meshes, MeshUpdateFlags);

            // recalculate bounds
            foreach (var mesh in meshes)
            {
                mesh.RecalculateBounds(MeshUpdateFlags);
            }

            return new List<(Vector3Int gridPoint, Mesh mesh)>(gridPoints.Zip(meshes, (point, mesh) => (point, mesh)));
        }
    }
}