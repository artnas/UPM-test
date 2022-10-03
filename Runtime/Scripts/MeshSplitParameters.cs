/* https://github.com/artnas/Unity-Plane-Mesh-Splitter */

using System;
using Unity.Mathematics;
using UnityEngine;

namespace MeshSplit.Scripts
{
    [Serializable]
    public class MeshSplitParameters
    {
        [Range(0.1f, 256)]
        public float GridSize = 16;
        public bool3 SplitAxes = new bool3(true, true, true);

        [Header("Parent attributes.")]
        public bool UseParentLayer = true;
        public bool UseParentStaticFlag = true;
        public bool UseParentMeshRendererSettings = true;

        [Header("Collisions.")] 
        public bool GenerateColliders;
        public bool UseConvexColliders;
    }
}