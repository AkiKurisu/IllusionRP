using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace Illusion.Rendering.AreaLights
{
    // Reference: UnityEngine.Rendering.HighDefinition.HDShadowAtlas / HDDynamicShadowAtlas (area light atlas, dynamic only)
    internal class AreaLightShadowAtlas
    {
        public enum BlurAlgorithm
        {
            None,
            EVSM, // exponential variance shadow maps
        }

        public int width { get; private set; }
        public int height { get; private set; }

        FilterMode m_FilterMode = FilterMode.Bilinear;
        DepthBits m_DepthBufferBits = DepthBits.Depth32;
        string m_Name = "";
        string m_MomentName;
        string m_MomentCopyName;

        // Moment shadow data
        BlurAlgorithm m_BlurAlgorithm;

        readonly List<HDShadowResolutionRequest> m_ShadowResolutionRequests = new();
        int[] m_SortedRequestsCache = new int[32];
        float m_RcpScaleFactor = 1.0f;

        public TextureDesc GetShadowMapTextureDesc()
        {
            return new TextureDesc(width, height)
            { filterMode = m_FilterMode, depthBufferBits = m_DepthBufferBits, isShadowMap = true, name = m_Name };
        }

        public void InitAtlas(int width, int height, DepthBits depthBufferBits, BlurAlgorithm blurAlgorithm, string name)
        {
            this.width = width;
            this.height = height;
            m_DepthBufferBits = depthBufferBits;
            m_Name = name;
            // With render graph, textures are "allocated" every frame so we need to prepare strings beforehand.
            m_MomentName = m_Name + "Moment";
            m_MomentCopyName = m_Name + "MomentCopy";
            m_BlurAlgorithm = blurAlgorithm;
        }

        // @IllusionRP: EVSM_1tap only reads LOD 0, the HDRP mip chain is dropped.
        public TextureDesc GetMomentAtlasDesc(bool copy = false)
        {
            return new TextureDesc(width / 2, height / 2)
            { format = GraphicsFormat.R32G32_SFloat, name = copy ? m_MomentCopyName : m_MomentName, enableRandomWrite = true };
        }

        public bool HasBlurredEVSM()
        {
            return (m_BlurAlgorithm == BlurAlgorithm.EVSM);
        }

        // This is a 9 tap filter, a gaussian with std. dev of 3. This standard deviation with this amount of taps probably cuts
        // the tail of the gaussian a bit too much, and it is a very fat curve, but it seems to work fine for our use case.
        public static readonly Vector4[] evsmBlurWeights =
        {
            new Vector4(0.1531703f, 0.1448929f, 0.1226492f, 0.0929025f),
            new Vector4(0.06297021f, 0.0f, 0.0f, 0.0f),
        };

        public int requestCount => m_ShadowResolutionRequests.Count;

        public void ClearRequests()
        {
            m_ShadowResolutionRequests.Clear();
        }

        public int ReserveResolution(Vector2 resolution)
        {
            var resolutionRequest = new HDShadowResolutionRequest
            {
                resolution = resolution,
                dynamicAtlasViewport = new Rect(0, 0, resolution.x, resolution.y)
            };
            m_ShadowResolutionRequests.Add(resolutionRequest);
            return m_ShadowResolutionRequests.Count - 1;
        }

        public Rect GetViewport(int index)
        {
            return m_ShadowResolutionRequests[index].dynamicAtlasViewport;
        }

        private static void InsertionSort(int[] array, List<HDShadowResolutionRequest> resolutionRequests, int startIndex, int lastIndex)
        {
            int i = startIndex + 1;

            while (i < lastIndex)
            {
                var curr = resolutionRequests[array[i]];
                int currHandle = array[i];

                int j = i - 1;

                // Sort in descending order.
                while ((j >= 0) && ((curr.resolution.x > resolutionRequests[array[j]].resolution.x) ||
                                    (curr.resolution.y > resolutionRequests[array[j]].resolution.y)))
                {
                    array[j + 1] = array[j];
                    j--;
                }

                array[j + 1] = currHandle;
                i++;
            }
        }

        private bool AtlasLayout(bool allowResize, int[] fullShadowList, int requestsCount)
        {
            float curX = 0, curY = 0, curH = 0, xMax = width, yMax = height;
            m_RcpScaleFactor = 1;
            for (int i = 0; i < requestsCount; ++i)
            {
                var shadowRequest = m_ShadowResolutionRequests[fullShadowList[i]];

                if (shadowRequest.resolution == Vector2.zero)
                    continue;

                // shadow atlas layouting
                Rect viewport = new Rect(Vector2.zero, shadowRequest.resolution);
                curH = Mathf.Max(curH, viewport.height);

                if (curX + viewport.width > xMax)
                {
                    curX = 0;
                    curY += curH;
                    curH = viewport.height;
                }
                if (curY + curH > yMax)
                {
                    if (allowResize)
                    {
                        LayoutResize();
                        return true;
                    }

                    return false;
                }
                viewport.x = curX;
                viewport.y = curY;
                shadowRequest.dynamicAtlasViewport = viewport;
                shadowRequest.resolution = viewport.size;
                m_ShadowResolutionRequests[fullShadowList[i]] = shadowRequest;
                curX += viewport.width;
            }

            return true;
        }

        internal bool Layout(bool allowResize = true)
        {
            int n = m_ShadowResolutionRequests.Count;
            if (m_SortedRequestsCache.Length < n)
                m_SortedRequestsCache = new int[Mathf.NextPowerOfTwo(n)];

            int i = 0;
            for (; i < n; ++i)
            {
                m_SortedRequestsCache[i] = i;
            }

            InsertionSort(m_SortedRequestsCache, m_ShadowResolutionRequests, 0, i);

            return AtlasLayout(allowResize, m_SortedRequestsCache, requestsCount: i);
        }

        void LayoutResize()
        {
            int index = 0;
            float currentX = 0;
            float currentY = 0;
            float currentMaxY = 0;
            float currentMaxX = 0;

            // Place shadows in a square shape
            while (index < m_ShadowResolutionRequests.Count)
            {
                float y = 0;
                float currentMaxXCache = currentMaxX;
                do
                {
                    var resolutionRequest = m_ShadowResolutionRequests[index];
                    Rect r = new Rect(Vector2.zero, resolutionRequest.resolution);
                    r.x = currentMaxX;
                    r.y = y;
                    y += r.height;
                    currentY = Mathf.Max(currentY, y);
                    currentMaxXCache = Mathf.Max(currentMaxXCache, currentMaxX + r.width);
                    resolutionRequest.dynamicAtlasViewport = r;
                    m_ShadowResolutionRequests[index] = resolutionRequest;
                    index++;
                } while (y < currentMaxY && index < m_ShadowResolutionRequests.Count);
                currentMaxY = Mathf.Max(currentMaxY, currentY);
                currentMaxX = currentMaxXCache;
                if (index >= m_ShadowResolutionRequests.Count)
                    continue;
                float x = 0;
                float currentMaxYCache = currentMaxY;
                do
                {
                    var resolutionRequest = m_ShadowResolutionRequests[index];
                    Rect r = new Rect(Vector2.zero, resolutionRequest.resolution);
                    r.x = x;
                    r.y = currentMaxY;
                    x += r.width;
                    currentX = Mathf.Max(currentX, x);
                    currentMaxYCache = Mathf.Max(currentMaxYCache, currentMaxY + r.height);
                    resolutionRequest.dynamicAtlasViewport = r;
                    m_ShadowResolutionRequests[index] = resolutionRequest;
                    index++;
                } while (x < currentMaxX && index < m_ShadowResolutionRequests.Count);
                currentMaxX = Mathf.Max(currentMaxX, currentX);
                currentMaxY = currentMaxYCache;
            }

            float maxResolution = Math.Max(currentMaxX, currentMaxY);
            Vector4 scale = new Vector4(width / maxResolution, height / maxResolution, width / maxResolution, height / maxResolution);
            m_RcpScaleFactor = Mathf.Min(scale.x, scale.y);

            // Scale down every shadow rects to fit with the current atlas size
            for (int i = 0; i < m_ShadowResolutionRequests.Count; i++)
            {
                var r = m_ShadowResolutionRequests[i];
                Vector4 s = new Vector4(r.dynamicAtlasViewport.x, r.dynamicAtlasViewport.y, r.dynamicAtlasViewport.width, r.dynamicAtlasViewport.height);
                Vector4 reScaled = Vector4.Scale(s, scale);

                r.dynamicAtlasViewport = new Rect(reScaled.x, reScaled.y, reScaled.z, reScaled.w);
                r.resolution = r.dynamicAtlasViewport.size;
                m_ShadowResolutionRequests[i] = r;
            }
        }

        // Reference: HDDynamicShadowAtlas.DisplayAtlas
        // @IllusionRP: the atlas is a render graph texture bound by the caller through the command buffer, not the property block.
        public void DisplayAtlas(CommandBuffer cmd, Material debugMaterial, Rect atlasViewport, float screenX, float screenY, float screenSizeX, float screenSizeY, float minValue, float maxValue, MaterialPropertyBlock mpb)
        {
            float scaleFactor = m_RcpScaleFactor;
            Vector4 validRange = new Vector4(minValue, 1.0f / (maxValue - minValue));
            float rWidth = 1.0f / width;
            float rHeight = 1.0f / height;
            Vector4 scaleBias = Vector4.Scale(new Vector4(rWidth, rHeight, rWidth, rHeight), new Vector4(atlasViewport.width, atlasViewport.height, atlasViewport.x, atlasViewport.y));

            mpb.SetVector(IllusionShaderProperties._TextureScaleBias, scaleBias);
            mpb.SetVector(IllusionShaderProperties._ValidRange, validRange);
            mpb.SetFloat(IllusionShaderProperties._RcpGlobalScaleFactor, scaleFactor);
            cmd.SetViewport(new Rect(screenX, screenY, screenSizeX, screenSizeY));
            cmd.DrawProcedural(Matrix4x4.identity, debugMaterial, debugMaterial.FindPass("RegularShadow"), MeshTopology.Triangles, 3, 1, mpb);
        }

        // Reference: HDShadowManager.CreateShadowData
        public HDShadowData CreateShadowData(ref HDShadowRequest shadowRequest)
        {
            HDShadowData data = new HDShadowData();

            var view = shadowRequest.cullingSplit.view;
            data.proj = shadowRequest.cullingSplit.deviceProjection;
            data.pos = shadowRequest.position;
            data.rot0 = new Vector3(view.m00, view.m01, view.m02);
            data.rot1 = new Vector3(view.m10, view.m11, view.m12);
            data.rot2 = new Vector3(view.m20, view.m21, view.m22);
            data.shadowToWorld = shadowRequest.shadowToWorld;
            data.cacheTranslationDelta = new Vector3(0.0f, 0.0f, 0.0f);

            var viewport = shadowRequest.dynamicAtlasViewport;

            // Compute the scale and offset (between 0 and 1) for the atlas coordinates
            float rWidth = 1.0f / width;
            float rHeight = 1.0f / height;
            data.atlasOffset = Vector2.Scale(new Vector2(rWidth, rHeight), new Vector2(viewport.x, viewport.y));

            data.shadowMapSize = new Vector4(viewport.width, viewport.height, 1.0f / viewport.width, 1.0f / viewport.height);

            data.normalBias = shadowRequest.normalBias;
            data.worldTexelSize = shadowRequest.worldTexelSize;

            data.shadowFilterParams0.x = shadowRequest.shadowSoftness;
            data.shadowFilterParams0.y = HDShadowUtils.Asfloat(shadowRequest.blockerSampleCount);
            data.shadowFilterParams0.z = HDShadowUtils.Asfloat(shadowRequest.filterSampleCount);
            data.shadowFilterParams0.w = shadowRequest.minFilterSize;

            data.zBufferParam = shadowRequest.zBufferParam;
            if (HasBlurredEVSM())
            {
                data.shadowFilterParams0 = shadowRequest.evsmParams;
            }

            data.isInCachedAtlas = shadowRequest.isInCachedAtlas ? 1.0f : 0.0f;

            return data;
        }
    }
}
