using UnityEngine;
using UnityEngine.Rendering;
using Unity.Mathematics;

namespace Illusion.Rendering.AreaLights
{
    // Reference: UnityEngine.Rendering.HighDefinition.HDShadowUtils (rectangle area light slice)
    // TODO remove every occurrence of ShadowSplitData in function parameters when we'll have scriptable culling
    internal static class HDShadowUtils
    {
        public const int k_MaxShadowSplitCount = 6;
        public const float k_MinShadowNearPlane = 0.01f;
        public const float k_MaxShadowNearPlane = 10.0f;

        public static float Asfloat(uint val) { unsafe { return *((float*)&val); } }
        public static float Asfloat(int val) { unsafe { return *((float*)&val); } }
        public static int Asint(float val) { unsafe { return *((int*)&val); } }
        public static uint Asuint(float val) { unsafe { return *((uint*)&val); } }

        public static Matrix4x4 GetGPUProjectionMatrix(Matrix4x4 projectionMatrix, bool invertY, bool reverseZ)
        {
            float4x4 gpuProjectionMatrix = math.transpose(projectionMatrix);
            if (invertY)
            {
                gpuProjectionMatrix.c1 = -gpuProjectionMatrix.c1;
            }

            // Now scale&bias to get Z range from -1..1 to 0..1 or 1..0
            // matrix = scaleBias * matrix
            //  1   0   0   0
            //  0   1   0   0
            //  0   0 0.5 0.5
            //  0   0   0   1
            float multiplier = reverseZ ? -0.5f : 0.5f;
            gpuProjectionMatrix.c2 = gpuProjectionMatrix.c2 * multiplier + gpuProjectionMatrix.c3 * 0.5f;

            return math.transpose(gpuProjectionMatrix);
        }

        public static void ExtractRectangleAreaLightData(VisibleLight visibleLight, float forwardOffset, float areaLightShadowCone, float shadowNearPlane, Vector2 shapeSize, Vector2 viewportSize, float normalBiasMax, bool reverseZ,
            out Matrix4x4 view, out Matrix4x4 invViewProjection, out Matrix4x4 projection, out Vector4 deviceProjection, out Matrix4x4 deviceProjectionYFlip, out ShadowSplitData splitData)
        {
            Vector4 lightDir;
            float aspectRatio = shapeSize.x / shapeSize.y;
            float spotAngle = areaLightShadowCone;
            visibleLight.spotAngle = spotAngle;
            float guardAngle = CalcGuardAnglePerspective(visibleLight.spotAngle, viewportSize.x, 1, normalBiasMax, 180.0f - visibleLight.spotAngle);

            ExtractSpotLightMatrix(visibleLight, forwardOffset, visibleLight.spotAngle, shadowNearPlane, guardAngle, aspectRatio, reverseZ,  out view, out projection, out deviceProjection, out deviceProjectionYFlip, out invViewProjection, out lightDir, out splitData);
        }

        static void InvertView(ref Matrix4x4 view, out Matrix4x4 invview)
        {
            invview = Matrix4x4.zero;
            invview.m00 = view.m00; invview.m01 = view.m10; invview.m02 = view.m20;
            invview.m10 = view.m01; invview.m11 = view.m11; invview.m12 = view.m21;
            invview.m20 = view.m02; invview.m21 = view.m12; invview.m22 = view.m22;
            invview.m33 = 1.0f;
            invview.m03 = -(invview.m00 * view.m03 + invview.m01 * view.m13 + invview.m02 * view.m23);
            invview.m13 = -(invview.m10 * view.m03 + invview.m11 * view.m13 + invview.m12 * view.m23);
            invview.m23 = -(invview.m20 * view.m03 + invview.m21 * view.m13 + invview.m22 * view.m23);
        }

        static void InvertPerspective(ref Matrix4x4 proj, ref Matrix4x4 view, out Matrix4x4 vpinv)
        {
            Matrix4x4 invview;
            InvertView(ref view, out invview);

            Matrix4x4 invproj = Matrix4x4.zero;
            invproj.m00 = 1.0f / proj.m00;
            invproj.m03 = proj.m02 * invproj.m00;
            invproj.m11 = 1.0f / proj.m11;
            invproj.m13 = proj.m12 * invproj.m11;
            invproj.m22 = 0.0f;
            invproj.m23 = -1.0f;
            invproj.m33 = proj.m22 / proj.m23;
            invproj.m32 = invproj.m33 / proj.m22;

            // We explicitly perform the invview * invproj multiplication given that there are lots of 0s involved, so it will be much faster.
            vpinv.m00 = invview.m00 * invproj.m00;
            vpinv.m01 = invview.m01 * invproj.m11;
            vpinv.m02 = invview.m03 * invproj.m32;
            vpinv.m03 = invview.m00 * invproj.m03 + invview.m01 * invproj.m13 - invview.m02 + invview.m03 * invproj.m33;

            vpinv.m10 = invview.m10 * invproj.m00;
            vpinv.m11 = invview.m11 * invproj.m11;
            vpinv.m12 = invview.m13 * invproj.m32;
            vpinv.m13 = invview.m10 * invproj.m03 + invview.m11 * invproj.m13 - invview.m12 + invview.m13 * invproj.m33;

            vpinv.m20 = invview.m20 * invproj.m00;
            vpinv.m21 = invview.m21 * invproj.m11;
            vpinv.m22 = invview.m23 * invproj.m32;
            vpinv.m23 = invview.m20 * invproj.m03 + invview.m21 * invproj.m13 - invview.m22 + invview.m23 * invproj.m33;

            vpinv.m30 = 0.0f;
            vpinv.m31 = 0.0f;
            vpinv.m32 = invproj.m32;
            vpinv.m33 = invproj.m33;
        }

        public static Matrix4x4 ExtractSpotLightProjectionMatrix(float range, float spotAngle, float nearPlane, float aspectRatio, float guardAngle)
        {
            float fov = spotAngle + guardAngle;
            float nearZ = Mathf.Max(nearPlane, k_MinShadowNearPlane);

            float e = 1.0f / Mathf.Tan(fov / 180.0f * Mathf.PI / 2.0f);
            float a = aspectRatio;
            float n = nearZ;
            float f = n + range;

            // Unity does something messed up if the aspect ratio is less than 1. I assume it happens on the C++ side.
            // A workaround is to avoid using Matrix4x4.Perspective and build the matrix manually...
            Matrix4x4 mat = new Matrix4x4();

            if (a < 1)
            {
                mat.m00 = e;
                mat.m11 = e * a;
            }
            else
            {
                mat.m00 = e / a;
                mat.m11 = e;
            }

            mat.m22 = -(f + n) / (f - n);
            mat.m23 = -2 * f * n / (f - n);
            mat.m32 = -1;

            return mat;
        }

        static Matrix4x4 ExtractSpotLightMatrix(VisibleLight vl, float forwardOffset, float spotAngle, float nearPlane, float guardAngle, float aspectRatio, bool reverseZ, out Matrix4x4 view, out Matrix4x4 proj, out Vector4 deviceProj, out Matrix4x4 deviceProjYFlip, out Matrix4x4 vpinverse, out Vector4 lightDir, out ShadowSplitData splitData)
        {
            splitData = new ShadowSplitData();
            splitData.cullingSphere.Set(0.0f, 0.0f, 0.0f, float.NegativeInfinity);
            splitData.cullingPlaneCount = 0;
            lightDir = vl.GetForward();

            // calculate view
            Matrix4x4 localToWorldOffset = vl.localToWorldMatrix;
            CoreMatrixUtils.MatrixTimesTranslation(ref localToWorldOffset, Vector3.forward * forwardOffset);
            view = localToWorldOffset.inverse;
            view.m20 *= -1;
            view.m21 *= -1;
            view.m22 *= -1;
            view.m23 *= -1;

            // calculate projection
            proj = ExtractSpotLightProjectionMatrix(vl.range - forwardOffset, spotAngle, nearPlane - forwardOffset, aspectRatio, guardAngle);

            // and the compound (deviceProj will potentially inverse-Z)
            Matrix4x4 deviceProjMatrix = GetGPUProjectionMatrix(proj, false, reverseZ);
            deviceProjYFlip = GetGPUProjectionMatrix(proj, true, reverseZ);
            InvertPerspective(ref deviceProjMatrix, ref view, out vpinverse);

            deviceProj = new Vector4(deviceProjMatrix.m00, deviceProjMatrix.m11, deviceProjMatrix.m22, deviceProjMatrix.m23);

            Matrix4x4 viewProj = CoreMatrixUtils.MultiplyPerspectiveMatrix(proj, view);
            float4 planesLeft;
            float4 planesRight;
            float4 planesBottom;
            float4 planesTop;
            float4 planesNear;
            float4 planesFar;
            CalculateFrustumPlanes(viewProj, out planesLeft, out planesRight, out planesBottom, out planesTop, out planesNear, out planesFar);
            // We can avoid computing proj * view for frustum planes, if device has reversed Z we flip the culling planes as we should have computed them with proj
            if (reverseZ)
            {
                var tmpPlane = planesBottom;
                planesBottom = planesTop;
                planesTop = tmpPlane;
            }
            splitData.cullingPlaneCount = 6;

            Plane leftPlane = new Plane();
            leftPlane.normal = planesLeft.xyz;
            leftPlane.distance = planesLeft.w;
            splitData.SetCullingPlane(0, leftPlane);

            Plane rightPlane = new Plane();
            rightPlane.normal = planesRight.xyz;
            rightPlane.distance = planesRight.w;
            splitData.SetCullingPlane(1, rightPlane);
            Plane bottomPlane = new Plane();
            bottomPlane.normal = planesBottom.xyz;
            bottomPlane.distance = planesBottom.w;
            splitData.SetCullingPlane(2, bottomPlane);
            Plane topPlane = new Plane();
            topPlane.normal = planesTop.xyz;
            topPlane.distance = planesTop.w;
            splitData.SetCullingPlane(3, topPlane);
            Plane planeNear = new Plane();
            planeNear.normal = planesNear.xyz;
            planeNear.distance = planesNear.w;
            splitData.SetCullingPlane(4, planeNear);
            Plane planeFar = new Plane();
            planeFar.normal = planesFar.xyz;
            planeFar.distance = planesFar.w;
            splitData.SetCullingPlane(5, planeFar);

            Matrix4x4 deviceViewProj = CoreMatrixUtils.MultiplyPerspectiveMatrix(deviceProjMatrix, view);

            splitData.cullingMatrix = deviceViewProj;
            splitData.cullingNearPlane = nearPlane - forwardOffset;
            return deviceViewProj;
        }

        public static void CalculateFrustumPlanes(float4x4 finalMatrix, out float4 outPlanesLeft, out float4 outPlanesRight, out float4 outPlanesBottom, out float4 outPlanesTop, out float4 outPlanesNear, out float4 outPlanesFar)
        {
            finalMatrix = math.transpose(finalMatrix);

            float4 tmpVec = finalMatrix.c3;
            float4 otherVec = finalMatrix.c0;

            // left & right
            float4 leftNormalAndDist = otherVec + tmpVec;
            float4 leftNormalZeroedDist = math.asfloat(math.asuint(leftNormalAndDist) & new uint4(0xFFFFFFFF, 0xFFFFFFFF, 0xFFFFFFFF, 0));
            float leftDotProduct = math.dot(leftNormalZeroedDist, leftNormalZeroedDist);
            float leftMagnitude = math.sqrt(leftDotProduct);
            float leftInvMagnitude = 1.0f / leftMagnitude;
            leftNormalAndDist *= leftInvMagnitude;
            outPlanesLeft = leftNormalAndDist;

            float4 rightNormalAndDist = -otherVec + tmpVec;
            float4 rightNormalZeroedDist = math.asfloat(math.asuint(rightNormalAndDist) & new uint4(0xFFFFFFFF, 0xFFFFFFFF, 0xFFFFFFFF, 0));
            float rightDotProduct = math.dot(rightNormalZeroedDist, rightNormalZeroedDist);
            float rightMagnitude = math.sqrt(rightDotProduct);
            float rightInvMagnitude = 1.0f / rightMagnitude;
            rightNormalAndDist *= rightInvMagnitude;
            outPlanesRight = rightNormalAndDist;

            // bottom & top
            otherVec = finalMatrix.c1;

            float4 bottomNormalAndDist = otherVec + tmpVec;
            float4 bottomNormalZeroedDist = math.asfloat(math.asuint(bottomNormalAndDist) & new uint4(0xFFFFFFFF, 0xFFFFFFFF, 0xFFFFFFFF, 0));
            float bottomDotProduct = math.dot(bottomNormalZeroedDist, bottomNormalZeroedDist);
            float bottomMagnitude = math.sqrt(bottomDotProduct);
            float bottomInvMagnitude = math.rcp(bottomMagnitude);
            bottomNormalAndDist *= bottomInvMagnitude;
            outPlanesBottom = bottomNormalAndDist;

            float4 topNormalAndDist = -otherVec + tmpVec;
            float4 topNormalZeroedDist = math.asfloat(math.asuint(topNormalAndDist) & new uint4(0xFFFFFFFF, 0xFFFFFFFF, 0xFFFFFFFF, 0));
            float topDotProduct = math.dot(topNormalZeroedDist, topNormalZeroedDist);
            float topMagnitude = math.sqrt(topDotProduct);
            float topInvMagnitude = math.rcp(topMagnitude);
            topNormalAndDist *= topInvMagnitude;
            outPlanesTop = topNormalAndDist;

            // near & far
            otherVec = finalMatrix.c2;

            float4 nearNormalAndDist = otherVec + tmpVec;
            float4 nearNormalZeroedDist = math.asfloat(math.asuint(nearNormalAndDist) & new uint4(0xFFFFFFFF, 0xFFFFFFFF, 0xFFFFFFFF, 0));
            float nearDotProduct = math.dot(nearNormalZeroedDist, nearNormalZeroedDist);
            float nearMagnitude = math.sqrt(nearDotProduct);
            float nearInvMagnitude = math.rcp(nearMagnitude);
            nearNormalAndDist *= nearInvMagnitude;
            outPlanesNear = nearNormalAndDist;

            float4 farNormalAndDist = -otherVec + tmpVec;
            float4 farNormalZeroedDist = math.asfloat(math.asuint(farNormalAndDist) & new uint4(0xFFFFFFFF, 0xFFFFFFFF, 0xFFFFFFFF, 0));
            float farDotProduct = math.dot(farNormalZeroedDist, farNormalZeroedDist);
            float farMagnitude = math.sqrt(farDotProduct);
            float farInvMagnitude = math.rcp(farMagnitude);
            farNormalAndDist *= farInvMagnitude;
            outPlanesFar = farNormalAndDist;
        }

        static float CalcGuardAnglePerspective(float angleInDeg, float resolution, float filterWidth, float normalBiasMax, float guardAngleMaxInDeg)
        {
            float angleInRad  = angleInDeg * 0.5f * Mathf.Deg2Rad;
            float res         = 2.0f / resolution;
            float texelSize   = math.cos(angleInRad) * res;
            float beta        = normalBiasMax * texelSize * 1.4142135623730950488016887242097f;
            float guardAngle  = math.atan(beta);
            texelSize   = math.tan(angleInRad + guardAngle) * res;
            guardAngle  = math.atan((resolution + math.ceil(filterWidth)) * texelSize * 0.5f) * 2.0f * Mathf.Rad2Deg - angleInDeg;
            guardAngle *= 2.0f;

            return guardAngle < guardAngleMaxInDeg ? guardAngle : guardAngleMaxInDeg;
        }

        public static float GetSlopeBias(float baseBias, float normalizedSlopeBias)
        {
            return normalizedSlopeBias * baseBias;
        }
    }

    // Reference: UnityEngine.Rendering.HighDefinition.VisibleLightExtensionMethods
    internal static class VisibleLightExtensionMethods
    {
        public struct VisibleLightAxisAndPosition
        {
            public Vector3 Position;
            public Vector3 Forward;
            public Vector3 Up;
            public Vector3 Right;
        }

        public static Vector3 GetPosition(this VisibleLight value)
        {
            return value.localToWorldMatrix.GetColumn(3);
        }

        public static Vector3 GetForward(this VisibleLight value)
        {
            return value.localToWorldMatrix.GetColumn(2);
        }

        public static Vector3 GetUp(this VisibleLight value)
        {
            return value.localToWorldMatrix.GetColumn(1);
        }

        public static Vector3 GetRight(this VisibleLight value)
        {
            return value.localToWorldMatrix.GetColumn(0);
        }

        public static VisibleLightAxisAndPosition GetAxisAndPosition(this VisibleLight value)
        {
            var matrix = value.localToWorldMatrix;
            VisibleLightAxisAndPosition output;
            output.Position = matrix.GetColumn(3);
            output.Forward  = matrix.GetColumn(2);
            output.Up       = matrix.GetColumn(1);
            output.Right    = matrix.GetColumn(0);
            return output;
        }
    }
}
