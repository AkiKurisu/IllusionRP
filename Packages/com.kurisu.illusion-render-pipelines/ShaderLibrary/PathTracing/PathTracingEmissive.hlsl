#ifndef ILLUSION_PATH_TRACING_EMISSIVE_INCLUDED
#define ILLUSION_PATH_TRACING_EMISSIVE_INCLUDED

#define PT_EMISSIVE_NO_RECORD 0xFFFFFFFFu

#if defined(PT_EMISSIVE_TABLE_ACCESS)
StructuredBuffer<uint2> t_IllusionEmissiveInstances;
StructuredBuffer<uint> t_IllusionEmissiveTriangleToRecord;
uint _IllusionEmissiveInstanceCount;

uint PathTracingFindEmissiveRecord(uint instanceID, uint triangleIndex)
{
    if (instanceID >= _IllusionEmissiveInstanceCount)
        return PT_EMISSIVE_NO_RECORD;
    uint2 range = t_IllusionEmissiveInstances[instanceID];
    if (triangleIndex >= range.y)
        return PT_EMISSIVE_NO_RECORD;
    return t_IllusionEmissiveTriangleToRecord[range.x + triangleIndex];
}
#endif

#endif
