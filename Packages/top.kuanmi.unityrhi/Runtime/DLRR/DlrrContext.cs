using System;

namespace UnityRhi
{
    /// <summary>Typed inputs for one DLSS Ray Reconstruction dispatch.</summary>
    public sealed class DlrrDispatchDesc
    {
        public Texture Input;
        public Texture Output;
        public Texture MotionVectors;
        public Texture Depth;
        public Texture DiffuseAlbedo;
        public Texture SpecularAlbedo;
        public Texture NormalRoughness;
        public Texture SpecularMotionVectors;
        public float[] WorldToView;
        public float[] ViewToClip;
        public UnityEngine.Vector2 CameraJitterPixels;
        public int RenderWidth;
        public int RenderHeight;
        public int OutputWidth;
        public int OutputHeight;
        public UpscalerMode Mode;
        public DlssRrPreset Preset;
        public bool UseSpecularMotionVectors = true;
    }

    /// <summary>
    /// Public façade for UnityRHI's native DLSS Ray Reconstruction extension.
    /// Native identifiers, resource handles and wire payloads stay encapsulated.
    /// </summary>
    public sealed class DlrrContext : IDisposable
    {
        private readonly int _instanceId;
        private bool _disposed;

        public DlrrContext()
        {
            _instanceId = Interop.NativeMethods.UnityRhiCreateDlrrInstance();
            if (_instanceId <= 0)
                throw new InvalidOperationException(
                    "UnityRHI failed to create the in-process DLSS-RR instance.");
        }

        public unsafe void Record(CommandList commandList, DlrrDispatchDesc desc)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(DlrrContext));
            if (commandList == null) throw new ArgumentNullException(nameof(commandList));
            if (desc == null) throw new ArgumentNullException(nameof(desc));
            RequireMatrix(desc.WorldToView, nameof(desc.WorldToView));
            RequireMatrix(desc.ViewToClip, nameof(desc.ViewToClip));

            var command = new CommandWire.DlrrDispatchPayload
            {
                Input = Handle(desc.Input, nameof(desc.Input)),
                Output = Handle(desc.Output, nameof(desc.Output)),
                MotionVectors = Handle(desc.MotionVectors, nameof(desc.MotionVectors)),
                Depth = Handle(desc.Depth, nameof(desc.Depth)),
                DiffuseAlbedo = Handle(desc.DiffuseAlbedo, nameof(desc.DiffuseAlbedo)),
                SpecularAlbedo = Handle(desc.SpecularAlbedo, nameof(desc.SpecularAlbedo)),
                NormalRoughness = Handle(desc.NormalRoughness, nameof(desc.NormalRoughness)),
                SpecularMotion = Handle(desc.SpecularMotionVectors,
                    nameof(desc.SpecularMotionVectors)),
                OutputWidth = Dimension(desc.OutputWidth, nameof(desc.OutputWidth)),
                OutputHeight = Dimension(desc.OutputHeight, nameof(desc.OutputHeight)),
                CurrentWidth = Dimension(desc.RenderWidth, nameof(desc.RenderWidth)),
                CurrentHeight = Dimension(desc.RenderHeight, nameof(desc.RenderHeight)),
                CameraJitterX = desc.CameraJitterPixels.x,
                CameraJitterY = desc.CameraJitterPixels.y,
                InstanceId = _instanceId,
                UseSpecularMotionVector = desc.UseSpecularMotionVectors ? (byte)1 : (byte)0,
                UpscalerMode = (byte)desc.Mode,
                Preset = (byte)desc.Preset,
            };
            for (int i = 0; i < 16; ++i)
            {
                command.WorldToViewMatrix[i] = desc.WorldToView[i];
                command.ViewToClipMatrix[i] = desc.ViewToClip[i];
            }

            commandList.DispatchDlrr(command,
                desc.Input, desc.Output, desc.MotionVectors, desc.Depth,
                desc.DiffuseAlbedo, desc.SpecularAlbedo, desc.NormalRoughness,
                desc.SpecularMotionVectors);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Interop.NativeMethods.UnityRhiDestroyDlrrInstance(_instanceId);
        }

        private static ushort Dimension(int value, string name)
        {
            if (value <= 0 || value > ushort.MaxValue)
                throw new ArgumentOutOfRangeException(name, value,
                    "DLSS-RR dimensions must be in the range 1..65535.");
            return checked((ushort)value);
        }

        private static void RequireMatrix(float[] value, string name)
        {
            if (value == null || value.Length != 16)
                throw new ArgumentException("A 4x4 matrix must contain exactly 16 values.", name);
        }

        private static ulong Handle(Texture texture, string name)
        {
            if (texture == null || !texture.IsValid)
                throw new ArgumentException("DLSS-RR textures must be valid UnityRHI textures.", name);
            return texture.OpaqueIdentity;
        }
    }
}
