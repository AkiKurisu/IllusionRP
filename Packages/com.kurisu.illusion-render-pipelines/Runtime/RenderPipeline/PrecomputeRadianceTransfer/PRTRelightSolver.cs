using System;
using UnityEngine;

namespace Illusion.Rendering.PRTGI
{
    internal sealed class PRTRelightSolver : IDisposable
    {
        public const float AbsoluteTolerance = 1e-5f;
        public const float RelativeTolerance = 1e-3f;
        public PRTProbeGrid Grid { get; }
        public int ProbeCount => Grid.ProbeCount;
        public GraphicsBuffer Previous { get; }
        public GraphicsBuffer Metadata { get; }
        public GraphicsBuffer PreviousMetadata { get; }
        public GraphicsBuffer Ready { get; }
        public GraphicsBuffer Layout { get; }
        public GraphicsBuffer Directions { get; }
        public int DirectionCount { get; }
        public uint Generation { get; private set; }
        public uint[] SectorRevisions { get; }
        public uint[] SectorFrames { get; }
        public long FixedBytes => (long)ProbeCount * (9 * 16 + 12) + PRTLayoutConstants.Stride + Pyramid.Bytes;
        public PRTProbePyramid Pyramid { get; }
        public PRTSectorResidency Residency { get; }
        public PRTSectorScheduler Scheduler { get; }
        public float Residual { get; internal set; } = float.NaN;
        public float AbsoluteResidual { get; internal set; } = float.NaN;
        public int NonFiniteCount { get; internal set; }
        public bool Disposed { get; private set; }

        public PRTRelightSolver(PRTProbeVolume volume)
        {
            Grid = volume.asset.Grid;
            Previous = Upload(new Vector4[ProbeCount * 9], 16, "PRT global frame-start SH");
            Metadata = Upload(volume.GetValidityMasks(), 4, "PRT input metadata");
            PreviousMetadata = Upload(new uint[ProbeCount], 4, "PRT committed metadata");
            Ready = Upload(new uint[ProbeCount], 4, "PRT global ready state");
            var signature = volume.asset.Signature;
            DirectionCount = signature.sampleCount;
            Directions = Upload(PRTBakeSampling.GenerateDirections(DirectionCount, signature.seed), 16, "PRT bake directions");
            Layout = PRTLayoutConstants.Allocate();
            Layout.SetData(new[] { PRTLayoutConstants.Create(Grid, Grid.count, 0) });
            SectorRevisions = new uint[volume.asset.Sectors.Length];
            SectorFrames = new uint[SectorRevisions.Length];
            Residency = new PRTSectorResidency(volume.asset);
            Pyramid = new PRTProbePyramid(Grid);
            Scheduler = new PRTSectorScheduler(volume.asset);
        }

        internal static GraphicsBuffer Upload<T>(T[] values, int stride, string name) where T : struct
        {
            var buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, Mathf.Max(1, values.Length), stride) { name = name };
            buffer.SetData(values.Length == 0 ? new T[1] : values);
            return buffer;
        }

        public void ScheduleCommit(int sector)
        {
            Generation++;
            SectorRevisions[sector] = Generation;
            SectorFrames[sector] = PRTRelightFrame.Index;
        }

        public void Dispose()
        {
            Disposed = true;
            Residency.Dispose();
            Previous.Dispose(); Metadata.Dispose(); PreviousMetadata.Dispose(); Ready.Dispose(); Layout.Dispose(); Directions.Dispose();
            Pyramid.Dispose();
        }
    }
}
