using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace MeshSplit.Scripts
{
    public class SubMeshBuilder
    {
        private readonly Dictionary<Vector3Int, List<int>> _pointIndices;
        private readonly byte[] _vertexData;
        private readonly int _vertexBufferStride;
        private readonly VertexAttributeDescriptor[] _vertexAttributeDescriptors;

        public SubMeshBuilder(Dictionary<Vector3Int, List<int>> pointIndices, byte[] vertexData, int vertexBufferStride, VertexAttributeDescriptor[] vertexAttributeDescriptors)
        {
            _pointIndices = pointIndices;
            _vertexData = vertexData;
            _vertexBufferStride = vertexBufferStride;
            _vertexAttributeDescriptors = vertexAttributeDescriptors;
        }

        private (NativeList<int> allIndices, NativeList<int2> indexRangesArray) FlattenPointIndices()
        {
            var allIndices = new NativeList<int>(100, Allocator.Persistent);
            var ranges = new NativeList<int2>(100, Allocator.Persistent);
            
            foreach (var entry in _pointIndices)
            {
                var gridPointIndices = new NativeArray<int>(entry.Value.ToArray(), Allocator.Temp);
                
                ranges.Add(new int2(allIndices.Length, gridPointIndices.Length));
                
                allIndices.AddRange(gridPointIndices);

                gridPointIndices.Dispose();
            }

            return (allIndices, ranges);
        }
        
        public Mesh.MeshDataArray Build(Mesh mesh)
        {
            var gridPoints = new NativeArray<Vector3Int>(_pointIndices.Keys.ToArray(), Allocator.TempJob);

            (NativeList<int> allIndices, NativeList<int2> indexRangesArray) = FlattenPointIndices();
            
            var meshDataArray = Mesh.AllocateWritableMeshData(_pointIndices.Count);

            var sourceMeshDataArray = Mesh.AcquireReadOnlyMeshData(mesh);

            var vertexData = new NativeArray<byte>(_vertexData, Allocator.TempJob);
            var vertexAttributes =
                new NativeArray<VertexAttributeDescriptor>(_vertexAttributeDescriptors, Allocator.TempJob);

            JobHandle? jobHandle = null;
            
            var workSlice = sourceMeshDataArray.Length / (Mathf.Clamp(Environment.ProcessorCount, 1, 8));

            for (var i = 0; i < sourceMeshDataArray.Length; i++)
            {
                var buildJob = new BuildSubMeshJob()
                {
                    AllIndices = allIndices,
                    IndexRanges = indexRangesArray,
                    VertexData = vertexData,
                    VertexStride = _vertexBufferStride,
                    VertexAttributeDescriptors = vertexAttributes,
                    SourceSubMeshIndex = i,
                    TargetMeshDataArray = meshDataArray
                };

                // schedule job
                jobHandle = jobHandle.HasValue 
                    ? buildJob.Schedule(gridPoints.Length, workSlice, jobHandle.Value) 
                    : buildJob.Schedule(gridPoints.Length, workSlice);
            }
            
            jobHandle?.Complete();

            // dispose
            vertexData.Dispose();
            vertexAttributes.Dispose();
            allIndices.Dispose();
            indexRangesArray.Dispose();
            gridPoints.Dispose();
            sourceMeshDataArray.Dispose();

            return meshDataArray;
        }

        [BurstCompile]
        private unsafe struct BuildSubMeshJob : IJobParallelFor
        {
            private static readonly MeshUpdateFlags MeshUpdateFlags = MeshUpdateFlags.DontNotifyMeshUsers 
                                                                      | MeshUpdateFlags.DontValidateIndices 
                                                                      | MeshUpdateFlags.DontResetBoneBounds;
            public int SourceSubMeshIndex;
            [NativeDisableParallelForRestriction]
            public Mesh.MeshDataArray TargetMeshDataArray;

            [NativeDisableContainerSafetyRestriction]
            public NativeList<int> AllIndices;
            [NativeDisableContainerSafetyRestriction]
            public NativeList<int2> IndexRanges;
            [NativeDisableContainerSafetyRestriction]
            public NativeArray<byte> VertexData;
            [NativeDisableContainerSafetyRestriction]
            public NativeArray<VertexAttributeDescriptor> VertexAttributeDescriptors;
            [ReadOnly]
            public int VertexStride;

            public void Execute(int index)
            {
                var writableMeshData = TargetMeshDataArray[index];

                var indexOffset = IndexRanges[index].x;
                var vertexCount = IndexRanges[index].y;
                
                // var indices = new NativeList<uint>(100, Allocator.Temp);
                var indices = new NativeArray<uint>(vertexCount * 3, Allocator.Temp);
                
                var vertexData = new NativeArray<byte>(VertexStride * vertexCount, Allocator.Temp);

                var vertexIndex = 0;

                // iterate triangle indices in pairs of 3
                for (int i = 0; i < vertexCount; i += 3)
                {
                    // indices of the triangle
                    var a = indexOffset + i;
                    var b = indexOffset + i + 1;
                    var c = indexOffset + i + 2;

                    AddVertex(vertexData, a, vertexIndex++);
                    AddVertex(vertexData, b, vertexIndex++);
                    AddVertex(vertexData, c, vertexIndex++);
                    
                    indices[i] = (uint)i;
                    indices[i+1] = (uint)(i+1);
                    indices[i+2] = (uint)(i+2);
                }
                
                // apply vertex data
                writableMeshData.SetVertexBufferParams(vertexCount, VertexAttributeDescriptors);
                var writableMeshVertexData = writableMeshData.GetVertexData<byte>();
                writableMeshVertexData.CopyFrom(vertexData);
                
                // automatically use 16 or 32 bit indexing depending on the vertex count
                var indexFormat = indices.Length >= ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16;                

                // apply index data
                writableMeshData.SetIndexBufferParams(indices.Length, indexFormat);

                switch (indexFormat)
                {
                    case IndexFormat.UInt16:
                    {
                        // convert 32 bit indices to 16 bit
                        var indexData = writableMeshData.GetIndexData<ushort>();
                        var indices16 = new NativeArray<ushort>(indices.Length, Allocator.Temp);
                        for (var i = 0; i < indices.Length; i++)
                        {
                            indices16[i] = (ushort)indices[i];
                        }
                        indexData.CopyFrom(indices16);
                        indices16.Dispose();
                        break;
                    }
                    case IndexFormat.UInt32:
                    {
                        writableMeshData.GetIndexData<uint>().CopyFrom(indices);
                        break;
                    }
                }

                // writableMeshData.subMeshCount = SourceSubMeshIndex + 1;
                writableMeshData.subMeshCount = 1;
                writableMeshData.SetSubMesh(0, new SubMeshDescriptor(0, indices.Length), MeshUpdateFlags);

                // dispose
                indices.Dispose();
                vertexData.Dispose();
            }

            private void AddVertex(NativeArray<byte> targetVertexData, int sourceVertexIndex, int targetVertexIndex)
            {
                var sourceIndex = AllIndices[sourceVertexIndex];

                var sourcePtr = (void*)IntPtr.Add((IntPtr)VertexData.GetUnsafePtr(), sourceIndex * VertexStride);
                var targetPtr = (void*)IntPtr.Add((IntPtr)targetVertexData.GetUnsafePtr(), targetVertexIndex * VertexStride);

                UnsafeUtility.MemCpy(targetPtr, sourcePtr, VertexStride);
            }
        }
    }
}