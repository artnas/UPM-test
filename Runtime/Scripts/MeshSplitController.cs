/* https://github.com/artnas/Unity-Plane-Mesh-Splitter */

using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;
using UnityEngine;

namespace MeshSplit.Scripts
{
    public class MeshSplitController : MonoBehaviour
    {
        /// <summary>
        /// Multiply grid size internally to still use Vector3Int even if the grid size is smaller than 1 (ex: 0.1)
        /// </summary>
        public static readonly int GridSizeMultiplier = 100;
        
        /// <summary>
        /// Multiply grid size internally to still use Vector3Int even if the grid size is smaller than 1 (ex: 0.1)
        /// </summary>
        private const int GizmosDisplayLimit = 100000;
        
        public bool Verbose;

        public MeshSplitParameters Parameters;
        public bool DrawGridGizmosWhenSelected;

        private Mesh _baseMesh;
        private MeshRenderer _baseRenderer;

        // generated children are kept here, so the script knows what to delete on Split() or Clear()
        [HideInInspector] [SerializeField]
        private List<GameObject> Children = new List<GameObject>();

        public void Split()
        {
            DestroyChildren();

            if (GetUsedAxisCount() < 1)
            {
                throw new Exception("You have to choose at least 1 axis.");
            }

            var meshFilter = GetComponent<MeshFilter>();
            if (meshFilter)
            {
                _baseMesh = meshFilter.sharedMesh;
            }
            else
            {
                throw new Exception("MeshFilter component is required.");
            }

            if (_baseRenderer || TryGetComponent(out _baseRenderer))
            {
                _baseRenderer.enabled = false;
            }

            CreateChildren();
        }

        private void CreateChildren()
        {
            var meshSplitter = new MeshSplitter(Parameters, Verbose);
            var subMeshData = meshSplitter.Split(_baseMesh);
            
            // sort the children
            subMeshData.Sort(delegate((Vector3Int gridPoint, Mesh mesh) a, (Vector3Int gridPoint, Mesh mesh) b)
            {
                for (int i = 0; i < 3; i++)
                {
                    var compare = a.gridPoint[i].CompareTo(b.gridPoint[i]);

                    if (compare != 0)
                    {
                        return compare;
                    }
                }

                return 0;
            });

            foreach (var (gridPoint, mesh) in subMeshData)
            {
                if (mesh.vertexCount > 0)
                    CreateChild(gridPoint, mesh);
            }
        }

        private void CreateChild(Vector3Int gridPoint, Mesh mesh)
        {
            // divide by multiplier and round to at moast 2 decimal places
            var pointString = $"({(float)gridPoint.x / GridSizeMultiplier:0.##}, {(float)gridPoint.y / GridSizeMultiplier:0.##}, {(float)gridPoint.z / GridSizeMultiplier:0.##})";
            var newGameObject = new GameObject
            {
                name = $"SubMesh {pointString}"
            };

            newGameObject.transform.SetParent(transform, false);
            if (Parameters.UseParentLayer)
            {
                newGameObject.layer = gameObject.layer;
            }
            if (Parameters.UseParentStaticFlag)
            {
                newGameObject.isStatic = gameObject.isStatic;
            }
            
            // assign the new mesh to this submeshes mesh filter
            var newMeshFilter = newGameObject.AddComponent<MeshFilter>();
            newMeshFilter.sharedMesh = mesh;

            var newMeshRenderer = newGameObject.AddComponent<MeshRenderer>();
            if (Parameters.UseParentMeshRendererSettings && _baseRenderer)
            {
                newMeshRenderer.sharedMaterial = _baseRenderer.sharedMaterial;
                newMeshRenderer.sortingOrder = _baseRenderer.sortingOrder;
                newMeshRenderer.sortingLayerID = _baseRenderer.sortingLayerID;
                newMeshRenderer.shadowCastingMode = _baseRenderer.shadowCastingMode;
                newMeshRenderer.receiveShadows = _baseRenderer.receiveShadows;
                newMeshRenderer.receiveGI = _baseRenderer.receiveGI;
                newMeshRenderer.lightProbeUsage = _baseRenderer.lightProbeUsage;
                newMeshRenderer.rayTracingMode = _baseRenderer.rayTracingMode;
                newMeshRenderer.reflectionProbeUsage = _baseRenderer.reflectionProbeUsage;
                newMeshRenderer.staticShadowCaster = _baseRenderer.staticShadowCaster;
                newMeshRenderer.motionVectorGenerationMode = _baseRenderer.motionVectorGenerationMode;
                newMeshRenderer.allowOcclusionWhenDynamic = _baseRenderer.allowOcclusionWhenDynamic;
            }

            if (Parameters.GenerateColliders)
            {
                var meshCollider = newGameObject.AddComponent<MeshCollider>();
                meshCollider.convex = Parameters.UseConvexColliders;
                meshCollider.sharedMesh = mesh;
            }
            
            Children.Add(newGameObject);
        }

        private int GetUsedAxisCount()
        {
            return (Parameters.SplitAxes.x ? 1 : 0) + (Parameters.SplitAxes.y ? 1 : 0) + (Parameters.SplitAxes.z ? 1 : 0);
        }

        public void Clear()
        {
            DestroyChildren();
            
            // reenable renderer
            if (_baseRenderer || TryGetComponent(out _baseRenderer))
            {
                _baseRenderer.enabled = true;
            }
        }

        private void DestroyChildren()
        {
            // find child submeshes which are not in child list
            var childCount = transform.childCount;
            if (childCount != Children.Count)
            {
                var unassignedSubMeshes = GetComponentsInChildren<MeshRenderer>()
                    .Where(child => child.name.Contains("SubMesh") && !Children.Contains(child.gameObject));

                var count = 0;

                foreach (var subMesh in unassignedSubMeshes)
                {
                    Children.Add(subMesh.gameObject);
                    count++;
                }
                
                if (Verbose) Debug.Log($"found {count} unassigned submeshes");
            }

            foreach (var t in Children)
            {
                // destroy mesh
                DestroyImmediate(t.GetComponent<MeshFilter>().sharedMesh);
                DestroyImmediate(t);
            }
            
            if (Verbose) Debug.Log($"destroyed {Children.Count} submeshes");

            Children.Clear();
        }

        private void OnDrawGizmosSelected()
        {
            if (!DrawGridGizmosWhenSelected || !TryGetComponent<MeshFilter>(out var meshFilter) || !meshFilter.sharedMesh || !TryGetComponent<Renderer>(out _))
                return;

            var t = transform;
            var bounds = meshFilter.sharedMesh.bounds;

            var xSize = Parameters.SplitAxes.x ? Mathf.Ceil(bounds.extents.x) : 1;
            var ySize = Parameters.SplitAxes.y ? Mathf.Ceil(bounds.extents.y) : 1;
            var zSize = Parameters.SplitAxes.z ? Mathf.Ceil(bounds.extents.z) : 1;

            // dont draw too many gizmos, this lags the editor to a crawl
            if ((xSize * ySize * zSize) / Parameters.GridSize > GizmosDisplayLimit)
            {
                return;
            }

            var center = bounds.center;
            
            // TODO improve grid alignment

            Gizmos.color = new Color(1, 1, 1, 0.3f);
            
            /* credit for this line drawing code goes to https://github.com/STARasGAMES */

            // X aligned lines
            for (var y = -ySize; y <= ySize; y += Parameters.GridSize)
            {
                for (var z = -zSize; z <= zSize; z += Parameters.GridSize)
                {
                    var start = t.TransformPoint(center + new Vector3(-xSize, y, z));
                    var end = t.TransformPoint(center + new Vector3(xSize, y, z));
                    Gizmos.DrawLine(start, end);
                }
            }

            // Y aligned lines
            for (var x = -xSize; x <= xSize; x += Parameters.GridSize)
            {
                for (var z = -zSize; z <= zSize; z += Parameters.GridSize)
                {
                    var start = t.TransformPoint(center + new Vector3(x, -ySize, z));
                    var end = t.TransformPoint(center + new Vector3(x, ySize, z));
                    Gizmos.DrawLine(start, end);
                }
            }
            
            // Z aligned lines
            for (var y = -ySize; y <= ySize + 1; y += Parameters.GridSize)
            {
                for (var x = -xSize; x <= xSize + 1; x += Parameters.GridSize)
                {
                    var start = t.TransformPoint(center + new Vector3(x, y, -zSize));
                    var end = t.TransformPoint(center + new Vector3(x, y, zSize));
                    Gizmos.DrawLine(start, end);
                }
            }
        }
    }
}
