#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Illusion.Rendering.PRTGI
{
    public partial class PRTProbeVolume
    {

        /// <summary>
        /// Draw probe grid visualization with virtual offset applied
        /// </summary>
        /// <param name="probeIndex">Index of the probe</param>
        /// <param name="probePos">Position of the probe</param>
        private void DrawProbeGridWithVirtualOffset(int probeIndex, Vector3 probePos)
        {
            // Draw original probe position in cyan
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(probePos, probeHandleSize * 0.1f);

            // Calculate per-probe virtual offset (includes adjustment volumes)
            Vector3 totalVirtualOffset = CalculateProbeVirtualOffset(probePos);
            Vector3 virtualOffsetPos = probePos + totalVirtualOffset;
            
            // Draw virtual offset probe position in magenta
            Gizmos.color = Color.magenta;
            Gizmos.DrawWireSphere(virtualOffsetPos, probeHandleSize * 0.1f);

            // Draw line from original to virtual offset position
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(probePos, virtualOffsetPos);

            // Draw arrow to show offset direction
            DrawOffsetArrow(probePos, virtualOffsetPos);

            // Draw connections to neighboring probes at virtual offset positions
            DrawVirtualOffsetProbeConnections(probeIndex, virtualOffsetPos);

            // Draw adjustment volume effects
            DrawAdjustmentVolumeEffects(probePos, totalVirtualOffset);
        }


        /// <summary>
        /// Draw arrow to show offset direction and magnitude
        /// </summary>
        /// <param name="startPos">Start position of the arrow</param>
        /// <param name="endPos">End position of the arrow</param>
        private static void DrawOffsetArrow(Vector3 startPos, Vector3 endPos)
        {
            Vector3 direction = endPos - startPos;
            float distance = direction.magnitude;
            
            // Only draw arrow if there's a significant offset
            if (distance < 0.01f) return;
            
            direction.Normalize();
            
            // Draw main arrow line
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(startPos, endPos);
            
            // Calculate arrow head size based on distance
            float arrowHeadSize = Mathf.Min(distance * 0.3f, 0.2f);
            Vector3 arrowHeadPos = endPos - direction * arrowHeadSize;
            
            // Draw arrow headlines
            Vector3 right = Vector3.Cross(direction, Vector3.up).normalized;
            if (right.magnitude < 0.1f) // If direction is parallel to up vector
            {
                right = Vector3.Cross(direction, Vector3.right).normalized;
            }
            Vector3 up = Vector3.Cross(right, direction).normalized;
            
            // Draw arrow head
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(endPos, arrowHeadPos + right * arrowHeadSize * 0.3f);
            Gizmos.DrawLine(endPos, arrowHeadPos - right * arrowHeadSize * 0.3f);
            Gizmos.DrawLine(endPos, arrowHeadPos + up * arrowHeadSize * 0.3f);
            Gizmos.DrawLine(endPos, arrowHeadPos - up * arrowHeadSize * 0.3f);
        }



        /// <summary>
        /// Draw connections between neighboring probes at virtual offset positions
        /// </summary>
        /// <param name="probeIndex">Index of the probe</param>
        /// <param name="virtualOffsetPos">Virtual offset position of the probe</param>
        private void DrawVirtualOffsetProbeConnections(int probeIndex, Vector3 virtualOffsetPos)
        {
            var (x, y, z) = IndexToCoordinate(probeIndex);

            // Draw lines to neighboring probes at their virtual offset positions
            Gizmos.color = Color.magenta * 0.5f;

            // X neighbors
            if (x > 0)
            {
                int neighborIndex = CoordinateToIndex(x - 1, y, z);
                Vector3 neighborVirtualPos = Probes[neighborIndex].Position + CalculateProbeVirtualOffset(Probes[neighborIndex].Position);
                Gizmos.DrawLine(virtualOffsetPos, neighborVirtualPos);
            }

            // Y neighbors
            if (y > 0)
            {
                int neighborIndex = CoordinateToIndex(x, y - 1, z);
                Vector3 neighborVirtualPos = Probes[neighborIndex].Position + CalculateProbeVirtualOffset(Probes[neighborIndex].Position);
                Gizmos.DrawLine(virtualOffsetPos, neighborVirtualPos);
            }

            // Z neighbors
            if (z > 0)
            {
                int neighborIndex = CoordinateToIndex(x, y, z - 1);
                Vector3 neighborVirtualPos = Probes[neighborIndex].Position + CalculateProbeVirtualOffset(Probes[neighborIndex].Position);
                Gizmos.DrawLine(virtualOffsetPos, neighborVirtualPos);
            }
        }



        /// <summary>
        /// Draw adjustment volume effects for a probe
        /// </summary>
        /// <param name="probePos">Position of the probe</param>
        /// <param name="totalOffset">Total virtual offset applied to this probe</param>
        private void DrawAdjustmentVolumeEffects(Vector3 probePos, Vector3 totalOffset)
        {
            // Use direct access to manager's volumes to avoid array allocation
            var adjustmentVolumes = PRTVolumeManager.AdjustmentVolumes;
            for (int i = 0; i < adjustmentVolumes.Count; i++)
            {
                var volume = adjustmentVolumes[i];
                if (volume != null && volume.Contains(probePos))
                {
                    // Draw volume influence indicator
                    Gizmos.color = volume.mode switch
                    {
                        PRTProbeAdjustmentMode.ApplyVirtualOffset => Color.cyan * 0.3f,
                        PRTProbeAdjustmentMode.OverrideVirtualOffsetSettings => Color.yellow * 0.3f,
                        PRTProbeAdjustmentMode.IntensityScale => Color.green * 0.3f,
                        PRTProbeAdjustmentMode.InvalidateProbes => Color.red * 0.3f,
                        _ => Color.white * 0.3f
                    };

                    // Draw small indicator sphere at probe position
                    Gizmos.DrawSphere(probePos, probeHandleSize * 0.05f);

                    // Draw line from probe to volume center to show influence
                    Gizmos.DrawLine(probePos, volume.transform.position);

                    // Draw ray-traced virtual offset position for OverrideVirtualOffsetSettings mode
                    if (volume.mode == PRTProbeAdjustmentMode.OverrideVirtualOffsetSettings)
                    {
                        Vector3 rayTracedPosition = probePos + CalculateProbeVirtualOffset(probePos);
                        
                        // Draw ray-traced position
                        Gizmos.color = Color.red;
                        Gizmos.DrawWireSphere(rayTracedPosition, probeHandleSize * 0.08f);
                        
                        // Draw line from original to ray-traced position
                        Gizmos.color = Color.red;
                        Gizmos.DrawLine(probePos, rayTracedPosition);
                        
                        // Draw ray directions used for ray tracing
                        DrawRayTraceDirections(probePos, volume);
                    }
                }
            }
        }



        /// <summary>
        /// Draw ray trace directions for visualization
        /// </summary>
        /// <param name="probePos">Probe position</param>
        /// <param name="volume">Adjustment volume</param>
        private void DrawRayTraceDirections(Vector3 probePos, PRTProbeAdjustmentVolume volume)
        {
            Vector3 capture = probePos + CalculateProbeVirtualOffset(probePos);
            Gizmos.color = Color.yellow * 0.3f;
            float length = Mathf.Max(0.1f, probeGridSize * 0.25f);
            for (int x = -1; x <= 1; x++)
                for (int y = -1; y <= 1; y++)
                    for (int z = -1; z <= 1; z++)
                    {
                        if (x == 0 && y == 0 && z == 0)
                            continue;
                        Vector3 direction = new Vector3(x, y, z).normalized;
                        Vector3 origin = capture + direction * volume.rayOriginBias;
                        Gizmos.DrawLine(origin, origin + direction * length);
                    }
        }
    }
}
#endif
