using UnityEngine;

namespace Illusion.Rendering.PRTGI
{
    internal static class PRTDataValidation
    {
        internal static bool IsFinite(Vector3 v) => float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z);
        internal static bool Validate(PRTProbeGrid grid, int width, PRTProbeMetadata[] probes,
            PRTSectorData[] sectors, out string reason)
        {
            reason = null;
            long count = (long)grid.count.x * grid.count.y * grid.count.z;
            if (grid.count.x < 1 || grid.count.y < 1 || grid.count.z < 1 || count > int.MaxValue ||
                !float.IsFinite(grid.spacing) || grid.spacing <= 0 || !IsFinite(grid.origin))
                return Fail("Invalid probe grid.", out reason);
            if (width < 1 || probes == null || probes.Length != count || sectors == null ||
                sectors.Length != ((grid.count.x + width - 1) / width) * ((grid.count.z + width - 1) / width))
                return Fail("Sector layout is missing or does not match the probe grid. Rebake the transport.", out reason);
            var seen = new bool[count];
            for (int i = 0; i < sectors.Length; i++)
            {
                var data = sectors[i];
                if (data?.surfels == null || data.bricks == null || data.factors == null || data.probes == null ||
                    data.skySamples == null || data.probeIds == null || data.probeIds.Length != data.probes.Length ||
                    data.probeIds.Length == 0 || !IsFinite(data.surfelBounds.center) || !IsFinite(data.surfelBounds.size))
                    return Fail("Incomplete sector transport.", out reason);
                int previous = -1;
                foreach (int id in data.probeIds)
                {
                    if (id <= previous || id >= count || seen[id]) return Fail("Invalid global probe ID.", out reason);
                    int sector = id / (grid.count.y * grid.count.z) / width * ((grid.count.z + width - 1) / width)
                        + id % grid.count.z / width;
                    if (sector != i) return Fail("Probe belongs to a different sector.", out reason);
                    seen[id] = true;
                    previous = id;
                }
                if (!ValidateSector(data, probes, out reason)) return false;
            }
            foreach (bool assigned in seen) if (!assigned) return Fail("Probe has no sector.", out reason);
            return true;
        }

        private static bool ValidateSector(PRTSectorData data, PRTProbeMetadata[] probes, out string reason)
        {
            reason = null;
            foreach (Surfel surfel in data.surfels)
                if (surfel.flags != 0 || !IsFinite(surfel.position) || !IsFinite(surfel.normal) ||
                    !IsFinite(surfel.albedo) || surfel.normal.sqrMagnitude < 0.5f || surfel.materialKey == 0 ||
                    surfel.nearestProbe < -1 || surfel.nearestProbe >= probes.Length ||
                    surfel.nearestProbe >= 0 && (probes[surfel.nearestProbe].validity >> 24) == 0)
                    return Fail("Transport contains an invalid geometry surfel.", out reason);
            foreach (SurfelIndices brick in data.bricks)
                if (brick.count < 1 || !Contains(brick.start, brick.count, data.surfels.Length))
                    return Fail("Invalid brick surfel range.", out reason);
            foreach (BrickFactor factor in data.factors)
                if (factor.brickIndex < 0 || factor.brickIndex >= data.bricks.Length || !factor.IsFinite || factor.sh0 <= 0)
                    return Fail("Invalid probe-to-brick transfer.", out reason);
            foreach (PRTSkySample sky in data.skySamples)
                if (!IsFinite(sky.direction) || Mathf.Abs(sky.direction.sqrMagnitude - 1f) > 0.001f ||
                    !float.IsFinite(sky.weight) || sky.weight <= 0)
                    return Fail("Invalid sky visibility sample.", out reason);
            foreach (PRTProbeData probe in data.probes)
                if (!Contains(probe.factorStart, probe.factorCount, data.factors.Length) ||
                    !Contains(probe.skyStart, probe.skyCount, data.skySamples.Length) || !IsFinite(probe.captureOffset))
                    return Fail("Invalid probe transport range or capture offset.", out reason);
            return true;
        }
        private static bool Contains(int start, int count, int length) => start >= 0 && count >= 0 &&
            start <= length && count <= length - start;
        private static bool Fail(string message, out string reason) { reason = message; return false; }
    }
}
