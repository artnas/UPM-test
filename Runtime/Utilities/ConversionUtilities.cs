using Unity.Mathematics;
using UnityEngine;

namespace MeshSplit.Scripts.Utilities
{
    public static class ConversionUtilities
    {
        public static half4 ToHalf4(this Vector2 v)
        {
            return new half4((half)v.x, (half)v.y, half.zero, half.zero);
        }
        
        public static float4 ToFloat4(this Vector3 v)
        {
            return new float4(v.x, v.y, v.z, 0);
        }
    }
}