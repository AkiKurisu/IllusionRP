using System;

namespace UnityRhi
{
    public enum DlssNrPreset : byte
    {
        Default = 0,
        Preset1 = 1,
        Preset2 = 2,
        Preset3 = 3,
    }

    public enum DlssNrStyle : byte
    {
        Default = 0,
        Natural = 1,
        Cinematic = 2,
    }

    /// <summary>Inputs recovered from the experimental DLSSNR feature-18 contract.</summary>
    public sealed class DlssNrDispatchDesc
    {
        public Texture Color;
        public Texture Output;
        public Texture MotionVectors;
        public Texture Depth;
        public int InputWidth;
        public int InputHeight;
        public int OutputWidth;
        public int OutputHeight;
        public float MotionVectorScaleX = 1f;
        public float MotionVectorScaleY = 1f;
        public float Intensity = 1f;
        public float LocalToneStrength = 1f;
        public float LocalStructureStrength = 1f;
        public float SkinStructureStrength = -1f;
        public bool DepthInverted = true;
        public bool Reset;
        public bool UseAutoMask;
        public bool UiCorrection;
        public bool Upscaling;
        public DlssNrPreset Preset;
        public DlssNrStyle Style;
    }

    /// <summary>
    /// Owns a standalone nvngx_dlssnr feature instance. Availability only means
    /// that the signed snippet initialized; feature creation still requires a
    /// supported RTX 50-series GPU and driver.
    /// </summary>
    public sealed class DlssNrContext : IDisposable
    {
        private readonly int _instanceId;
        private bool _disposed;

        public DlssNrContext()
        {
            _instanceId = Interop.NativeMethods.UnityRhiCreateDlssNrInstance();
            if (_instanceId <= 0)
                throw new InvalidOperationException(
                    "UnityRHI could not create a DLSS Neural Rendering context. " +
                    "Check RhiCore.IsDlssNrAvailable and RhiCore.DlssNrInitResult.");
        }

        public void Record(CommandList commandList, DlssNrDispatchDesc desc)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(DlssNrContext));
            if (commandList == null) throw new ArgumentNullException(nameof(commandList));
            if (desc == null) throw new ArgumentNullException(nameof(desc));
            if (ReferenceEquals(desc.Color, desc.Output))
                throw new ArgumentException("DLSSNR color and output textures must be distinct.", nameof(desc));

            var command = new CommandWire.DlssNrDispatchPayload
            {
                Color = Handle(desc.Color, nameof(desc.Color)),
                Output = Handle(desc.Output, nameof(desc.Output)),
                MotionVectors = Handle(desc.MotionVectors, nameof(desc.MotionVectors)),
                Depth = Handle(desc.Depth, nameof(desc.Depth)),
                InputWidth = Dimension(desc.InputWidth, nameof(desc.InputWidth)),
                InputHeight = Dimension(desc.InputHeight, nameof(desc.InputHeight)),
                OutputWidth = Dimension(desc.OutputWidth, nameof(desc.OutputWidth)),
                OutputHeight = Dimension(desc.OutputHeight, nameof(desc.OutputHeight)),
                MotionVectorScaleX = desc.MotionVectorScaleX,
                MotionVectorScaleY = desc.MotionVectorScaleY,
                Intensity = desc.Intensity,
                LocalToneStrength = desc.LocalToneStrength,
                LocalStructureStrength = desc.LocalStructureStrength,
                SkinStructureStrength = desc.SkinStructureStrength,
                InstanceId = _instanceId,
                DepthInverted = Flag(desc.DepthInverted),
                Reset = Flag(desc.Reset),
                UseAutoMask = Flag(desc.UseAutoMask),
                UiCorrection = Flag(desc.UiCorrection),
                Upscaling = Flag(desc.Upscaling),
                Preset = (byte)desc.Preset,
                Style = (byte)desc.Style,
            };
            commandList.DispatchDlssNr(command, desc.Color, desc.Output,
                desc.MotionVectors, desc.Depth);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Interop.NativeMethods.UnityRhiDestroyDlssNrInstance(_instanceId);
        }

        private static byte Flag(bool value) => value ? (byte)1 : (byte)0;

        private static ushort Dimension(int value, string name)
        {
            if (value <= 0 || value > ushort.MaxValue)
                throw new ArgumentOutOfRangeException(name, value,
                    "DLSSNR dimensions must be in the range 1..65535.");
            return checked((ushort)value);
        }

        private static ulong Handle(Texture texture, string name)
        {
            if (texture == null || !texture.IsValid)
                throw new ArgumentException("DLSSNR textures must be valid UnityRHI textures.", name);
            return texture.OpaqueIdentity;
        }
    }
}
