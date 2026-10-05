/*
 * Copyright (c) 2023-2026, NVIDIA CORPORATION. All rights reserved.
 *
 * NVIDIA CORPORATION and its licensors retain all intellectual property
 * and proprietary rights in and to this software, related documentation
 * and any modifications thereto.  Any use, reproduction, disclosure or
 * distribution of this software and related documentation without an express
 * license agreement from NVIDIA CORPORATION is strictly prohibited.
 */

#ifndef HASH_GRID_TYPES_H
#define HASH_GRID_TYPES_H

#define HASH_GRID_HASH_MAP_BUCKET_SIZE  16
#define HASH_GRID_INVALID_HASH_KEY      0
#define HASH_GRID_INVALID_CACHE_INDEX   0xFFFFFFFF

#define HashGridIndex uint

struct HashGridParameters
{
    float3 cameraPosition;      // world-space camera position; distance to camera defines voxel level/size
    float logarithmBase;        // controls how quickly grid levels change with distance (SHaRC typically uses 2.0f)
    float sceneScale;           // world-space scale factor controlling voxel size
    float levelBias;            // biases level selection: can add more near-camera levels or clamp minimum voxel size (start with 0)
};

#endif // HASH_GRID_TYPES_H
