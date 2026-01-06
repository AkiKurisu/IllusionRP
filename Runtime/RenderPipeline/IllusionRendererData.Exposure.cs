using Illusion.Rendering.PostProcessing;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Illusion.Rendering
{
    public partial class IllusionRendererData
    {
        private static void SetExposureTextureToEmpty(RTHandle exposureTexture)
        {
            var tex = new Texture2D(1, 1, GraphicsFormat.R16G16_SFloat, TextureCreationFlags.None);
            tex.SetPixel(0, 0, new Color(1f, ColorUtils.ConvertExposureToEV100(1f), 0f, 0f));
            tex.Apply();
            Graphics.Blit(tex, exposureTexture);
            CoreUtils.Destroy(tex);
        }

        public void GrabExposureRequiredTextures(out RTHandle outPrevExposure, out RTHandle outNextExposure)
        {
            // One frame delay + history RTs being flipped at the beginning of the frame means we
            // have to grab the exposure marked as "previous"
            outPrevExposure = CurrentExposureTextures.Current;
            outNextExposure = CurrentExposureTextures.Previous;

            if (ResetPostProcessingHistory)
            {
                // For Dynamic Exposure, we need to undo the pre-exposure from the color buffer to calculate the correct one
                // When we reset history we must setup neutral value
                outPrevExposure = _emptyExposureTexture; // Use neutral texture
            }
        }

        public RTHandle GetExposureTexture()
        {
            // Note: GetExposureTexture(camera) must be call AFTER the call of DoFixedExposure to be correctly taken into account
            // When we use Dynamic Exposure and we reset history we can't use pre-exposure (as there is no information)
            // For this reasons we put neutral value at the beginning of the frame in Exposure textures and
            // apply processed exposure from color buffer at the end of the Frame, only for a single frame.
            // After that we re-use the pre-exposure system
            if (_exposure != null && ResetPostProcessingHistory && !IsExposureFixed())
                return _emptyExposureTexture;

            // 1x1 pixel, holds the current exposure multiplied in the red channel and EV100 value
            // in the green channel
            return GetExposureTextureHandle(CurrentExposureTextures.Current);
        }

        private RTHandle GetExposureTextureHandle(RTHandle rt)
        {
            return rt ?? _emptyExposureTexture;
        }

        public RTHandle GetExposureDebugData()
        {
            return _debugExposureData;
        }

        public RTHandle GetPreviousExposureTexture()
        {
            // If the history was reset in the previous frame, then the history buffers were actually rendered with a neutral EV100 exposure multiplier
            return DidResetPostProcessingHistoryInLastFrame && !IsExposureFixed() ?
                _emptyExposureTexture : GetExposureTextureHandle(CurrentExposureTextures.Previous);
        }

        public bool IsExposureFixed()
        {
            if (_exposure == null) return true;
            return _exposure.mode.value == ExposureMode.Fixed;
            // || _automaticExposure.mode.value == ExposureMode.UsePhysicalCamera;
        }
        
        public bool CanRunFixedExposurePass() => IsExposureFixed()
                                                 && CurrentExposureTextures.Current != null;

        /// <summary>
        /// Get RTHandle wrapper for Texture2D.whiteTexture (lazy initialization)
        /// </summary>
        public RTHandle GetWhiteTextureRT()
        {
            if (_whiteTextureRTHandle == null)
            {
                _whiteTextureRTHandle = RTHandles.Alloc(Texture2D.whiteTexture);
            }
            return _whiteTextureRTHandle;
        }

        /// <summary>
        /// Get RTHandle wrapper for Texture2D.blackTexture (lazy initialization)
        /// </summary>
        public RTHandle GetBlackTextureRT()
        {
            if (_blackTextureRTHandle == null)
            {
                _blackTextureRTHandle = RTHandles.Alloc(Texture2D.blackTexture);
            }
            return _blackTextureRTHandle;
        }

        /// <summary>
        /// Get RTHandle wrapper for Texture2D.grayTexture (lazy initialization)
        /// </summary>
        public RTHandle GetGrayTextureRT()
        {
            if (_grayTextureRTHandle == null)
            {
                _grayTextureRTHandle = RTHandles.Alloc(Texture2D.grayTexture);
            }
            return _grayTextureRTHandle;
        }

        private struct ExposureTextures
        {
            public RTHandle Current;
            
            public RTHandle Previous;

            public void Clear()
            {
                Current = null;
                Previous = null;
            }
        }

        private ExposureTextures _exposureTextures = new() {Current = null, Previous = null };

        private ExposureTextures CurrentExposureTextures => _exposureTextures;

        private void SetupExposureTextures()
        {
            var currentTexture = GetCurrentFrameRT((int)IllusionFrameHistoryType.Exposure);
            if (currentTexture == null)
            {
                RTHandle Allocator(string id, int frameIndex, RTHandleSystem rtHandleSystem)
                {
                    // r: multiplier, g: EV100
                    var rt = rtHandleSystem.Alloc(1, 1, colorFormat: ExposureFormat,
                        enableRandomWrite: true, name: $"{id} Exposure Texture {frameIndex}"
                    );
                    SetExposureTextureToEmpty(rt);
                    return rt;
                }

                currentTexture = AllocHistoryFrameRT((int)IllusionFrameHistoryType.Exposure, Allocator, 2);
            }

            // One frame delay + history RTs being flipped at the beginning of the frame means we
            // have to grab the exposure marked as "previous"
            _exposureTextures.Current = GetPreviousFrameRT((int)IllusionFrameHistoryType.Exposure);
            _exposureTextures.Previous = currentTexture;
        }
    }
}