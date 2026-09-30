using UnityEngine;
using UnityEngine.Rendering;

namespace Illusion.Rendering.PathTracing
{
    internal readonly struct PathTracingShaderTime
    {
        private static readonly int Time = Shader.PropertyToID("_Time");
        private static readonly int SinTime = Shader.PropertyToID("_SinTime");
        private static readonly int CosTime = Shader.PropertyToID("_CosTime");
        private static readonly int DeltaTime = Shader.PropertyToID("unity_DeltaTime");
        private static readonly int TimeParameters = Shader.PropertyToID("_TimeParameters");
        private static readonly int LastTimeParameters = Shader.PropertyToID("_LastTimeParameters");

        private readonly float _time;
        private readonly float _deltaTime;
        private readonly float _smoothDeltaTime;

        private PathTracingShaderTime(float time, float deltaTime, float smoothDeltaTime)
        {
            _time = time;
            _deltaTime = deltaTime;
            _smoothDeltaTime = smoothDeltaTime;
        }

        public static PathTracingShaderTime Current
        {
            get
            {
#if UNITY_EDITOR
                float time = Application.isPlaying ? UnityEngine.Time.time : UnityEngine.Time.realtimeSinceStartup;
#else
                float time = UnityEngine.Time.time;
#endif
                return new PathTracingShaderTime(time, UnityEngine.Time.deltaTime, UnityEngine.Time.smoothDeltaTime);
            }
        }

        public void Push(CommandBuffer cmd)
        {
            float lastTime = _time - _deltaTime;
            cmd.SetGlobalVector(Time, _time * new Vector4(1f / 20f, 1f, 2f, 3f));
            cmd.SetGlobalVector(SinTime, new Vector4(Mathf.Sin(_time / 8f), Mathf.Sin(_time / 4f), Mathf.Sin(_time / 2f), Mathf.Sin(_time)));
            cmd.SetGlobalVector(CosTime, new Vector4(Mathf.Cos(_time / 8f), Mathf.Cos(_time / 4f), Mathf.Cos(_time / 2f), Mathf.Cos(_time)));
            cmd.SetGlobalVector(DeltaTime, new Vector4(_deltaTime, 1f / _deltaTime, _smoothDeltaTime, 1f / _smoothDeltaTime));
            cmd.SetGlobalVector(TimeParameters, new Vector4(_time, Mathf.Sin(_time), Mathf.Cos(_time), 0f));
            cmd.SetGlobalVector(LastTimeParameters, new Vector4(lastTime, Mathf.Sin(lastTime), Mathf.Cos(lastTime), 0f));
        }
    }
}
