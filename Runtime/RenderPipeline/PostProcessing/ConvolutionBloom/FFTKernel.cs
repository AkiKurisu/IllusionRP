using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

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

        private void InternalFFT(CommandBuffer cmd, RTHandle texture, bool highQuality)
        {
            _fftShader.EnableKeyword(_keywordInoutTarget);
            cmd.SetComputeTextureParam(_fftShader, _fftKernel, ShaderIds.Target, texture);
            UpdateSize(texture.rt.width, texture.rt.height);

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

        public void FFT(CommandBuffer cmd, RTHandle texture, bool highQuality)
        {
            cmd.EnableKeyword(_fftShader, _keywordForward);
            InternalFFT(cmd, texture, highQuality);
            cmd.DisableKeyword(_fftShader, _keywordForward);
        }

        private void InverseFFT(CommandBuffer cmd, RTHandle texture, bool highQuality)
        {
            cmd.EnableKeyword(_fftShader, _keywordInverse);
            InternalFFT(cmd, texture, highQuality);
            cmd.DisableKeyword(_fftShader, _keywordInverse);
        }

        public void Convolve(CommandBuffer cmd, RTHandle texture, RTHandle filter, bool highQuality)
        {
            FFT(cmd, texture, highQuality);

            _convolveGroupSize = new GroupSize(_convolveShader, _convolveKernel);
            _convolveDispatchSize.Update(_convolveGroupSize, new Vector3Int(_sizeX, _sizeY, 1));

            cmd.SetComputeTextureParam(_convolveShader, _convolveKernel, ShaderIds.Target, texture);
            cmd.SetComputeTextureParam(_convolveShader, _convolveKernel, ShaderIds.Filter, filter);
            cmd.SetComputeIntParams(_convolveShader, ShaderIds.TargetSize, texture.rt.width, texture.rt.height);
            cmd.DispatchCompute(_convolveShader, _convolveKernel,
                _convolveDispatchSize.X, _convolveDispatchSize.Y / 2 + 1, 1);

            InverseFFT(cmd, texture, highQuality);
        }

        // warn that logic is not impl for offset.x
        public void ConvolveOpt(CommandBuffer cmd,
            RTHandle texture,
            RTHandle filter,
            Vector2Int size,
            Vector2Int horizontalRange,
            Vector2Int verticalRange,
            Vector2Int offset)
        {
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

            int horizontalY = texture.rt.height;
            
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
                
        public void FFT(ComputeCommandBuffer cmd, TextureHandle texture, bool highQuality)
        {
            cmd.EnableKeyword(_fftShader, _keywordForward);
            InternalFFT(cmd, texture, highQuality);
            cmd.DisableKeyword(_fftShader, _keywordForward);
        }
        
        private void InverseFFT(ComputeCommandBuffer cmd, TextureHandle texture, bool highQuality)
        {
            cmd.EnableKeyword(_fftShader, _keywordInverse);
            InternalFFT(cmd, texture, highQuality);
            cmd.DisableKeyword(_fftShader, _keywordInverse);
        }
        
        private void InternalFFT(ComputeCommandBuffer cmd, TextureHandle texture, bool highQuality)
        {
            _fftShader.EnableKeyword(_keywordInoutTarget);
            cmd.SetComputeTextureParam(_fftShader, _fftKernel, ShaderIds.Target, texture);
            RTHandle rt = texture;
            UpdateSize(rt.rt.width, rt.rt.height);

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
                
        public void Convolve(ComputeCommandBuffer cmd, TextureHandle texture, TextureHandle filter, bool highQuality)
        {
            RTHandle rt = texture;
            FFT(cmd, texture, highQuality);

            _convolveGroupSize = new GroupSize(_convolveShader, _convolveKernel);
            _convolveDispatchSize.Update(_convolveGroupSize, new Vector3Int(_sizeX, _sizeY, 1));

            cmd.SetComputeTextureParam(_convolveShader, _convolveKernel, ShaderIds.Target, texture);
            cmd.SetComputeTextureParam(_convolveShader, _convolveKernel, ShaderIds.Filter, filter);
            cmd.SetComputeIntParams(_convolveShader, ShaderIds.TargetSize, rt.rt.width, rt.rt.height);
            cmd.DispatchCompute(_convolveShader, _convolveKernel,
                _convolveDispatchSize.X, _convolveDispatchSize.Y / 2 + 1, 1);

            InverseFFT(cmd, texture, highQuality);
        }
        
        public void ConvolveOpt(ComputeCommandBuffer cmd,
            TextureHandle texture,
            TextureHandle filter,
            Vector2Int size,
            Vector2Int horizontalRange,
            Vector2Int verticalRange,
            Vector2Int offset)
        {
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

            RTHandle rt = texture;
            int horizontalY = rt.rt.height;
            
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