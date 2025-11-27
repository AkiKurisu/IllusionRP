using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Illusion.Rendering.PostProcessing
{
    public class FFTKernel
    {
        public enum FFTSize
        {
            Size256 = 256,
            Size512 = 512,
            Size1024 = 1024
        }

        private readonly ComputeShader _fftShader;
        
        private readonly int _fftKernel;
        
        private readonly int _convolution2DKernel;

        private readonly ComputeShader _convolveShader;
        
        private readonly int _convolveKernel;
        
        private GroupSize _convolveGroupSize;
        
        private DispatchSize _convolveDispatchSize;

        private struct GroupSize
        {
            public readonly uint X;
            
            public readonly uint Y;

            public GroupSize(ComputeShader shader, int kernel)
            {
                shader.GetKernelThreadGroupSizes(kernel, out X, out Y, out _);
            }
        }

        private struct DispatchSize
        {
            public int X;
            
            public int Y;

            public void Update(GroupSize groupSize, Vector3Int threadSize)
            {
                X = Mathf.CeilToInt(threadSize.x / (float)groupSize.X);
                Y = Mathf.CeilToInt(threadSize.y / (float)groupSize.Y);
            }
        }
        
        private readonly LocalKeyword _keywordHighQuality;
        
        private readonly LocalKeyword _keywordVertical;
        
        private readonly LocalKeyword _keywordForward;
        
        private readonly LocalKeyword _keywordInverse;
        
        private readonly LocalKeyword _keywordConvolution2D;
        
        private readonly LocalKeyword _keywordInoutTarget;
        
        private readonly LocalKeyword _keywordInplace;
        
        // private readonly LocalKeyword _keywordPadding;
        
        // private readonly LocalKeyword _keywordThreadRemap;
        
        private readonly LocalKeyword _keywordReadBlock;
        
        private readonly LocalKeyword _keywordWriteBlock;
        
        private readonly LocalKeyword _keywordRWShift;

        private int _sizeX;
        
        private int _sizeY;

        public FFTKernel(ComputeShader fftShader, ComputeShader convolveShader)
        {
            _fftShader = fftShader;
            _convolveShader = convolveShader;

            _fftKernel = fftShader.FindKernel("FFT");
            _convolution2DKernel = fftShader.FindKernel("Convolution2D");

            _convolveKernel = convolveShader.FindKernel("Convolve");

            _convolveGroupSize = new GroupSize(convolveShader, _convolveKernel);

            _keywordHighQuality = fftShader.keywordSpace.FindKeyword("QUALITY_HIGH");
            _keywordForward = fftShader.keywordSpace.FindKeyword("FORWARD");
            _keywordInverse = fftShader.keywordSpace.FindKeyword("INVERSE");
            _keywordConvolution2D = fftShader.keywordSpace.FindKeyword("CONVOLUTION_2D");
            _keywordVertical = fftShader.keywordSpace.FindKeyword("VERTICAL");
            _keywordInoutTarget = fftShader.keywordSpace.FindKeyword("INOUT_TARGET");
            _keywordInplace = fftShader.keywordSpace.FindKeyword("INPLACE");
            // _keywordPadding = fftShader.keywordSpace.FindKeyword("PADDING");
            // _keywordThreadRemap = fftShader.keywordSpace.FindKeyword("THREAD_REMAP");
            _keywordReadBlock = fftShader.keywordSpace.FindKeyword("READ_BLOCK");
            _keywordWriteBlock = fftShader.keywordSpace.FindKeyword("WRITE_BLOCK");
            _keywordRWShift = fftShader.keywordSpace.FindKeyword("RW_SHIFT");
            UpdateSize((int)FFTSize.Size512, (int)FFTSize.Size256);
        }

        private void UpdateSize(int width, int height)
        {
            if (width != _sizeX)
            {
                _sizeX = width;
                _convolveDispatchSize.Update(_convolveGroupSize, new Vector3Int(_sizeX, _sizeY, 1));
            }

            if (height != _sizeY)
            {
                _sizeY = height;
                _convolveDispatchSize.Update(_convolveGroupSize, new Vector3Int(_sizeX, _sizeY, 1));
            }
        }

        private void InternalFFT(RenderTexture texture, CommandBuffer cmd, bool highQuality)
        {
            _fftShader.EnableKeyword(_keywordInoutTarget);
            cmd.SetComputeTextureParam(_fftShader, _fftKernel, ShaderIds.Target, texture);
            UpdateSize(texture.width, texture.height);

            if (highQuality)
            {
                 cmd.EnableKeyword(_fftShader, _keywordHighQuality);
            }
            else
            {
                cmd.DisableKeyword(_fftShader, _keywordHighQuality);
            }
            cmd.DispatchCompute(_fftShader, _fftKernel, 1, _sizeY, 1);

            cmd.EnableKeyword(_fftShader, _keywordVertical);
            cmd.DispatchCompute(_fftShader, _fftKernel, 1, _sizeX, 1);
            cmd.DisableKeyword(_fftShader, _keywordVertical);
        }

        public void FFT(RenderTexture texture, CommandBuffer cmd, bool highQuality)
        {
            using (new ProfilingScope(cmd, _fftProfilingSampler))
            {
                cmd.EnableKeyword(_fftShader, _keywordForward);
                InternalFFT(texture, cmd, highQuality);
                cmd.DisableKeyword(_fftShader, _keywordForward);
            }
        }

        private void InverseFFT(RenderTexture texture, CommandBuffer cmd, bool sqrtNormalize, bool highQuality)
        {
            using (new ProfilingScope(cmd, _fftProfilingSampler))
            {
                cmd.EnableKeyword(_fftShader, _keywordInverse);
                InternalFFT(texture, cmd, highQuality);
                cmd.DisableKeyword(_fftShader, _keywordInverse);
            }
        }

        private readonly ProfilingSampler _fftProfilingSampler = new("FFT Operation");

        private readonly ProfilingSampler _spectrumProfilingSampler = new("FFT Conv - Spectrum Multiplication");

        private readonly ProfilingSampler _fftHorizontalProfilingSampler = new("FFT Horizontal");

        private readonly ProfilingSampler _mixedProfilingSampler = new("FFT Vertical + Spectrum + IFFT Vertical");

        public void Convolve(CommandBuffer cmd, RenderTexture texture, RenderTexture filter, bool highQuality)
        {
            if (texture.width != filter.width || texture.height != filter.height)
            {
                throw new Exception("Texture size not match");
            }
            FFT(texture, cmd, highQuality);

            _convolveGroupSize = new GroupSize(_convolveShader, _convolveKernel);
            _convolveDispatchSize.Update(_convolveGroupSize, new Vector3Int(_sizeX, _sizeY, 1));

            using (new ProfilingScope(cmd, _spectrumProfilingSampler))
            {
                cmd.SetComputeTextureParam(_convolveShader, _convolveKernel, ShaderIds.Target, texture);
                cmd.SetComputeTextureParam(_convolveShader, _convolveKernel, ShaderIds.Filter, filter);
                cmd.SetComputeIntParams(_convolveShader, ShaderIds.TargetSize, texture.width, texture.height);
                cmd.DispatchCompute(_convolveShader, _convolveKernel,
                    _convolveDispatchSize.X, _convolveDispatchSize.Y / 2 + 1, 1);
            }

            InverseFFT(texture, cmd, false, highQuality);
        }

        // warn that logic is not impl for offset.x
        public void ConvolveOpt(CommandBuffer cmd,
            RenderTexture texture,
            RenderTexture filter,
            Vector2Int size,
            Vector2Int horizontalRange,
            Vector2Int verticalRange,
            Vector2Int offset)
        {
            if (size.x != filter.width || size.y != filter.height)
            {
                throw new Exception("Texture size not match");
            }

            int rwRangeBeginX = horizontalRange[0];
            int rwRangeEndX = horizontalRange[1];
            int rwRangeBeginY = verticalRange[0];
            int rwRangeEndY = verticalRange[1];
            bool horizontalReadWriteBlock = horizontalRange != Vector2Int.zero;
            bool vertiacalReadWriteBlock = verticalRange != Vector2Int.zero;
            bool verticalOffset = offset.y != 0;

            cmd.EnableKeyword(_fftShader, _keywordInoutTarget);
            cmd.SetComputeTextureParam(_fftShader, _fftKernel, ShaderIds.Target, texture);
            UpdateSize(size.x, size.y);

            int horizontalY = texture.height;

            using (new ProfilingScope(cmd, _fftHorizontalProfilingSampler))
            {
                if (horizontalReadWriteBlock)
                {
                    cmd.EnableKeyword(_fftShader, _keywordReadBlock);
                    cmd.SetComputeIntParams(_fftShader, ShaderIds.ReadWriteRangeAndOffset, rwRangeBeginX, rwRangeEndX);
                }

                cmd.EnableKeyword(_fftShader, _keywordForward);
                cmd.DispatchCompute(_fftShader, _fftKernel, 1, horizontalY, 1);
                cmd.DisableKeyword(_fftShader, _keywordForward);
                if (horizontalReadWriteBlock)
                {
                    cmd.DisableKeyword(_fftShader, _keywordReadBlock);
                }
            }


            using (new ProfilingScope(cmd, _mixedProfilingSampler))
            {
                cmd.EnableKeyword(_fftShader, _keywordVertical);
                if (vertiacalReadWriteBlock || verticalOffset)
                {
                    if (vertiacalReadWriteBlock)
                    {
                        cmd.EnableKeyword(_fftShader, _keywordReadBlock);
                        cmd.EnableKeyword(_fftShader, _keywordWriteBlock);
                    }

                    if (verticalOffset)
                    {
                        cmd.EnableKeyword(_fftShader, _keywordRWShift);
                    }

                    cmd.SetComputeIntParams(_fftShader, ShaderIds.ReadWriteRangeAndOffset,
                        rwRangeBeginY,
                        rwRangeEndY,
                        0,
                        offset.y);
                }

                cmd.EnableKeyword(_fftShader, _keywordConvolution2D);
                cmd.EnableKeyword(_fftShader, _keywordInplace);

                cmd.SetComputeIntParams(_fftShader, ShaderIds.TargetSize, size.x, size.y);
                cmd.SetComputeTextureParam(_fftShader, _convolution2DKernel, ShaderIds.Target, texture);
                cmd.SetComputeTextureParam(_fftShader, _convolution2DKernel, ShaderIds.ConvKernelSpectrum, filter);
                cmd.DispatchCompute(_fftShader, _convolution2DKernel, 1, (_sizeX + 1) / 2, 1);

                cmd.DisableKeyword(_fftShader, _keywordInplace);
                cmd.DisableKeyword(_fftShader, _keywordConvolution2D);

                cmd.DisableKeyword(_fftShader, _keywordVertical);
                if (vertiacalReadWriteBlock)
                {
                    cmd.DisableKeyword(_fftShader, _keywordReadBlock);
                    cmd.DisableKeyword(_fftShader, _keywordWriteBlock);
                }

                if (verticalOffset)
                {
                    cmd.DisableKeyword(_fftShader, _keywordRWShift);
                }
            }

            using (new ProfilingScope(cmd, _fftHorizontalProfilingSampler))
            {
                if (horizontalReadWriteBlock)
                {
                    cmd.EnableKeyword(_fftShader, _keywordWriteBlock);
                    cmd.SetComputeIntParams(_fftShader, ShaderIds.ReadWriteRangeAndOffset, rwRangeBeginX, rwRangeEndX);
                }

                cmd.EnableKeyword(_fftShader, _keywordInverse);
                cmd.DispatchCompute(_fftShader, _fftKernel, 1, horizontalY, 1);
                cmd.DisableKeyword(_fftShader, _keywordInverse);
                if (horizontalReadWriteBlock)
                {
                    cmd.DisableKeyword(_fftShader, _keywordWriteBlock);
                }
            }
        }
        
        private static class ShaderIds
        {
            public static readonly int ReadWriteRangeAndOffset = MemberNameHelpers.ShaderPropertyID();
            
            public static readonly int TargetSize = MemberNameHelpers.ShaderPropertyID();
            
            public static readonly int Target = MemberNameHelpers.ShaderPropertyID();
            
            public static readonly int ConvKernelSpectrum = MemberNameHelpers.ShaderPropertyID();
            
            public static readonly int Filter = MemberNameHelpers.ShaderPropertyID();
        }
    }
}