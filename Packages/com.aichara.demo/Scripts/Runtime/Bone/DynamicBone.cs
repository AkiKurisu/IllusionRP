using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using AIChara;

namespace AIChara.Bone
{
    [AddComponentMenu("Dynamic Bone/Dynamic Bone")]
    public class DynamicBone : MonoBehaviour
    {
        public enum UpdateMode
        {
            Normal = 0,
            AnimatePhysics = 1,
            UnscaledTime = 2
        }

        public enum FreezeAxis
        {
            None = 0,
            X = 1,
            Y = 2,
            Z = 3
        }

        private class Particle
        {
            public Transform m_Transform;

            public int m_ParentIndex = -1;

            public float m_Damping;

            public float m_Elasticity;

            public float m_Stiffness;

            public float m_Inert;

            public float m_Radius;

            public float m_BoneLength;

            public Vector3 m_Position = Vector3.zero;

            public Vector3 m_PrevPosition = Vector3.zero;

            public Vector3 m_EndOffset = Vector3.zero;

            public Vector3 m_InitLocalPosition = Vector3.zero;

            public Quaternion m_InitLocalRotation = Quaternion.identity;
        }

        [BurstCompile]
        private struct CalcJob : IJobFor
        {
            [NativeDisableParallelForRestriction]
            public NativeArray<ParticleStruct> Particles;

            [ReadOnly]
            public NativeArray<CollisionStruct> Colliders;

            [ReadOnly]
            public float Weight;

            [ReadOnly]
            public float ObjectScale;

            [BurstCompile]
            public void Execute(int i)
            {
                int index = i + 1;
                ParticleStruct value = Particles[index];
                ParticleStruct particleStruct = Particles[value.parentIndex];
                float num;
                if (value.isTransform != 1)
                {
                    float3 result = default;
                    MultiplyVector(ref particleStruct.worldMatrix, value.endOffset, ref result);
                    num = math.length(result);
                }
                else
                {
                    num = math.length(particleStruct.transWorldPosition - value.transWorldPosition);
                }
                float num2 = math.lerp(1f, value.stiffness, Weight);
                if (num2 > 0f || value.elasticity > 0f)
                {
                    float4x4 worldMatrix = particleStruct.worldMatrix;
                    worldMatrix[3] = new float4(particleStruct.position, 0);
                    float3 vector = default;
                    if (value.isTransform != 1)
                    {
                        MultiplyPoint3x4(ref worldMatrix, value.endOffset, ref vector);
                    }
                    else
                    {
                        MultiplyPoint3x4(ref worldMatrix, value.transLocalPosition, ref vector);
                    }
                    float3 vector2 = vector - value.position;
                    value.position += vector2 * value.elasticity;
                    if (num2 > 0f)
                    {
                        vector2 = vector - value.position;
                        float magnitude = math.length(vector2);
                        float num3 = num * (1f - num2) * 2f;
                        if (magnitude > num3)
                        {
                            value.position += vector2 * ((magnitude - num3) / magnitude);
                        }
                    }
                }
                float particleRadius = value.radius * ObjectScale;
                value.position = CalcCollider(value.position, particleRadius);
                float3 vector3 = particleStruct.position - value.position;
                float magnitude2 = math.length(vector3);
                if (magnitude2 > 0f)
                {
                    value.position += vector3 * ((magnitude2 - num) / magnitude2);
                }
                Particles[index] = value;
            }

            [BurstCompile]
            public static void MultiplyVector(ref float4x4 worldMatrix, in float3 point, ref float3 result)
            {
                result = math.mul(worldMatrix, new float4(point, 0.0f)).xyz;
            }

            [BurstCompile]
            public static void MultiplyPoint3x4(ref float4x4 worldMatrix, in float3 point, ref float3 result)
            {
                result = math.mul(worldMatrix, new float4(point, 1.0f)).xyz;
            }

            [BurstCompile]
            private float3 CalcCollider(in float3 inPosition, in float particleRadius)
            {
                float3 position = inPosition;
                for (int i = 0; i < Colliders.Length; i++)
                {
                    CollisionStruct collisionStruct = Colliders[i];
                    float num = collisionStruct.radius * math.abs(collisionStruct.lossyScale.x);
                    float num2 = collisionStruct.height * 0.5f - collisionStruct.radius;
                    float4x4 worldMatrix = collisionStruct.worldMatrix;
                    if (num2 <= 0f)
                    {
                        float4 center4 = new(collisionStruct.center, 0);
                        float3 vector = math.mul(worldMatrix, center4).xyz;
                        if (collisionStruct.bound == DynamicBoneColliderBase.Bound.Outside)
                        {
                            float num3 = num + particleRadius;
                            float num4 = num3 * num3;
                            float3 vector2 = position - vector;
                            float sqrMagnitude = math.dot(vector2, vector2);
                            if (sqrMagnitude > 0f && sqrMagnitude < num4)
                            {
                                float num5 = math.sqrt(sqrMagnitude);
                                position = vector + vector2 * (num3 / num5);
                            }
                        }
                        else
                        {
                            float num6 = num - particleRadius;
                            float num7 = num6 * num6;
                            float3 vector3 = position - vector;
                            float sqrMagnitude2 = math.dot(vector3, vector3);
                            if (sqrMagnitude2 > num7)
                            {
                                float num8 = math.sqrt(sqrMagnitude2);
                                position = vector + vector3 * (num6 / num8);
                            }
                        }
                        continue;
                    }
                    float3 center = collisionStruct.center;
                    float3 center2 = collisionStruct.center;
                    switch (collisionStruct.direction)
                    {
                        case DynamicBoneColliderBase.Direction.X:
                            center.x -= num2;
                            center2.x += num2;
                            break;
                        case DynamicBoneColliderBase.Direction.Y:
                            center.y -= num2;
                            center2.y += num2;
                            break;
                        case DynamicBoneColliderBase.Direction.Z:
                            center.z -= num2;
                            center2.z += num2;
                            break;
                    }
                    MultiplyPoint3x4(ref worldMatrix, center, ref center);
                    MultiplyPoint3x4(ref worldMatrix, center2, ref center2);
                    if (collisionStruct.bound == DynamicBoneColliderBase.Bound.Outside)
                    {
                        float num9 = num + particleRadius;
                        float num10 = num9 * num9;
                        float3 vector4 = center2 - center;
                        float3 vector5 = position - center;
                        float num11 = math.dot(vector5, vector4);
                        if (num11 <= 0f)
                        {
                            float sqrMagnitude3 = math.dot(vector5, vector5);
                            if (sqrMagnitude3 > 0f && sqrMagnitude3 < num10)
                            {
                                float num12 = math.sqrt(sqrMagnitude3);
                                position = center + vector5 * (num9 / num12);
                            }
                            continue;
                        }
                        float sqrMagnitude4 = math.dot(vector4, vector4);
                        if (num11 >= sqrMagnitude4)
                        {
                            vector5 = position - center2;
                            float sqrMagnitude5 = math.dot(vector5, vector5);
                            if (sqrMagnitude5 > 0f && sqrMagnitude5 < num10)
                            {
                                float num13 = math.sqrt(sqrMagnitude5);
                                position = center2 + vector5 * (num9 / num13);
                            }
                        }
                        else if (sqrMagnitude4 > 0f)
                        {
                            num11 /= sqrMagnitude4;
                            vector5 -= vector4 * num11;
                            float sqrMagnitude6 = math.dot(vector5, vector5);
                            if (sqrMagnitude6 > 0f && sqrMagnitude6 < num10)
                            {
                                float num14 = math.sqrt(sqrMagnitude6);
                                position += vector5 * ((num9 - num14) / num14);
                            }
                        }
                        continue;
                    }
                    float num15 = num - particleRadius;
                    float num16 = num15 * num15;
                    float3 vector6 = center2 - center;
                    float3 vector7 = position - center;
                    float num17 = math.dot(vector7, vector6);
                    if (num17 <= 0f)
                    {
                        float sqrMagnitude7 = math.dot(vector7, vector7);
                        if (sqrMagnitude7 > num16)
                        {
                            float num18 = math.sqrt(sqrMagnitude7);
                            position = center + vector7 * (num15 / num18);
                        }
                        continue;
                    }
                    float sqrMagnitude8 = math.dot(vector6, vector6);
                    if (num17 >= sqrMagnitude8)
                    {
                        vector7 = position - center2;
                        float sqrMagnitude9 = math.dot(vector7, vector7);
                        if (sqrMagnitude9 > num16)
                        {
                            float num19 = math.sqrt(sqrMagnitude9);
                            position = center2 + vector7 * (num15 / num19);
                        }
                    }
                    else if (sqrMagnitude8 > 0f)
                    {
                        num17 /= sqrMagnitude8;
                        vector7 -= vector6 * num17;
                        float sqrMagnitude10 = math.dot(vector7, vector7);
                        if (sqrMagnitude10 > num16)
                        {
                            float num20 = math.sqrt(sqrMagnitude10);
                            position += vector7 * ((num15 - num20) / num20);
                        }
                    }
                }
                return position;
            }
        }

        private struct ParticleStruct
        {
            public int parentIndex;

            public float damping;

            public float elasticity;

            public float stiffness;

            public float inert;

            public float radius;

            public float boneLength;

            public int isTransform;

            public float3 transWorldPosition;

            public float3 transLocalPosition;

            public float3 position;

            public float3 prevPosition;

            public float3 endOffset;

            public float3 initLocalPosition;

            public quaternion initLocalRotation;

            public float4x4 worldMatrix;
        }

        private struct CollisionStruct
        {
            public DynamicBoneColliderBase.Direction direction;

            public float3 center;

            public DynamicBoneColliderBase.Bound bound;

            public float radius;

            public float height;

            public float3 lossyScale;

            public float4x4 worldMatrix;
        }

        public string Comment = "";

        public Transform m_Root;

        public float m_UpdateRate = 60f;

        public UpdateMode m_UpdateMode;

        [Range(0f, 1f)]
        public float m_Damping = 0.1f;

        public AnimationCurve m_DampingDistrib;

        [Range(0f, 1f)]
        public float m_Elasticity = 0.1f;

        public AnimationCurve m_ElasticityDistrib;

        [Range(0f, 1f)]
        public float m_Stiffness = 0.1f;

        public AnimationCurve m_StiffnessDistrib;

        [Range(0f, 1f)]
        public float m_Inert;

        public AnimationCurve m_InertDistrib;

        public float m_Radius;

        public AnimationCurve m_RadiusDistrib;

        public float m_EndLength;

        public Vector3 m_EndOffset = Vector3.zero;

        public Vector3 m_Gravity = Vector3.zero;

        public Vector3 m_Force = Vector3.zero;

        public List<DynamicBoneColliderBase> m_Colliders;

        public List<Transform> m_Exclusions;

        public FreezeAxis m_FreezeAxis;

        public bool m_DistantDisable;

        public Transform m_ReferenceObject;

        public float m_DistanceToObject = 20f;

        public List<Transform> m_notRolls;

        private Vector3 m_LocalGravity = Vector3.zero;

        private Vector3 m_ObjectMove = Vector3.zero;

        private Vector3 m_ObjectPrevPosition = Vector3.zero;

        private float m_BoneTotalLength;
        private float m_ObjectScale = 1f;

        private float m_Time;

        private float m_Weight = 1f;

        private bool m_DistantDisabled;

        private readonly List<Particle> m_Particles = new();

        public IEnumerable<Transform> ParticleTransforms
        {
            get
            {
                foreach (Particle particle in m_Particles)
                {
                    if (particle.m_Transform)
                    {
                        yield return particle.m_Transform;
                    }
                }
            }
        }

        public void RebuildParticles()
        {
            SetupParticles();
        }

        public void ReinitializeTransforms()
        {
            InitTransforms();
        }

        private void Start()
        {
            // NativeLeakDetection.Mode = NativeLeakDetectionMode.Disabled;
            SetupParticles();
        }

        private void FixedUpdate()
        {
            if (m_UpdateMode == UpdateMode.AnimatePhysics)
            {
                PreUpdate();
            }
        }

        private void Update()
        {
            if (m_UpdateMode != UpdateMode.AnimatePhysics)
            {
                PreUpdate();
            }
        }

        private void LateUpdate()
        {
            if (m_DistantDisable)
            {
                CheckDistance();
            }

            if (m_Weight > 0f && IsDistanceEnable())
            {
                UpdateDynamicBones(Time.deltaTime);
            }
        }

        private bool IsDistanceEnable()
        {
            return !m_DistantDisable || !m_DistantDisabled;
        }


        private void PreUpdate()
        {
            if (m_Weight > 0f && IsDistanceEnable())
            {
                InitTransforms();
            }
        }

        private void CheckDistance()
        {
            if (!m_ReferenceObject && Camera.main)
            {
                m_ReferenceObject = Camera.main.transform;
            }
            if (!m_ReferenceObject)
            {
                return;
            }
            float distanceToObject = m_DistanceToObject * 0.1f;
            bool flag = (m_ReferenceObject.position - transform.position).sqrMagnitude > distanceToObject * distanceToObject;
            if (flag != m_DistantDisabled)
            {
                if (!flag)
                {
                    ResetParticlesPosition();
                }
                m_DistantDisabled = flag;
            }
        }

        private void OnEnable()
        {
            ResetParticlesPosition();
        }

        private void OnDisable()
        {
            InitTransforms();
        }

        private void OnValidate()
        {
            m_UpdateRate = Mathf.Max(m_UpdateRate, 0f);
            m_Damping = Mathf.Clamp01(m_Damping);
            m_Elasticity = Mathf.Clamp01(m_Elasticity);
            m_Stiffness = Mathf.Clamp01(m_Stiffness);
            m_Inert = Mathf.Clamp01(m_Inert);
            m_Radius = Mathf.Max(m_Radius, 0f);
            if (Application.isEditor && Application.isPlaying)
            {
                InitTransforms();
                SetupParticles();
            }
        }
#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (!enabled || !m_Root)
            {
                return;
            }
            if (Application.isEditor && !Application.isPlaying && transform.hasChanged)
            {
                InitTransforms();
                SetupParticles();
            }
            Gizmos.color = Color.white;
            for (int i = 0; i < m_Particles.Count; i++)
            {
                Particle particle = m_Particles[i];
                if (particle.m_ParentIndex >= 0)
                {
                    Particle particle2 = m_Particles[particle.m_ParentIndex];
                    Gizmos.DrawLine(particle.m_Position, particle2.m_Position);
                }
                if (particle.m_Radius > 0f)
                {
                    Gizmos.DrawWireSphere(particle.m_Position, particle.m_Radius * m_ObjectScale);
                }
            }
        }
#endif
        public void SetWeight(float w)
        {
            if (m_Weight != w)
            {
                if (w == 0f)
                {
                    InitTransforms();
                }
                else if (m_Weight == 0f)
                {
                    ResetParticlesPosition();
                }
                m_Weight = w;
            }
        }

        public float GetWeight()
        {
            return m_Weight;
        }

        private void UpdateDynamicBones(float t)
        {
            if (!m_Root)
            {
                return;
            }
            m_ObjectScale = Mathf.Abs(transform.lossyScale.x);
            m_ObjectMove = transform.position - m_ObjectPrevPosition;
            m_ObjectPrevPosition = transform.position;
            if (m_UpdateRate > 0f)
            {
                float num2 = 1f / m_UpdateRate;
                m_Time += t;
                int num = 0;
                while (m_Time >= num2)
                {
                    m_Time -= num2;
                    if (++num >= 3)
                    {
                        m_Time = 0f;
                        break;
                    }
                }
            }
            UpdateParticles1();
            ScheduleParticlesJob();
            m_ObjectMove = Vector3.zero;
            ApplyParticlesToTransforms();
        }

        private void SetupParticles()
        {
            m_Particles.Clear();
            if (m_Root)
            {
                m_LocalGravity = m_Root.InverseTransformDirection(m_Gravity);
                m_ObjectScale = Mathf.Abs(transform.lossyScale.x);
                m_ObjectPrevPosition = transform.position;
                m_ObjectMove = Vector3.zero;
                m_BoneTotalLength = 0f;
                AppendParticles(m_Root, -1, 0f);
                UpdateParameters();
            }
        }

        private void AppendParticles(Transform b, int parentIndex, float boneLength)
        {
            Particle particle = new()
            {
                m_Transform = b,
                m_ParentIndex = parentIndex
            };
            if (b)
            {
                particle.m_Position = particle.m_PrevPosition = b.position;
                particle.m_InitLocalPosition = b.localPosition;
                particle.m_InitLocalRotation = b.localRotation;
            }
            else
            {
                Transform transform = m_Particles[parentIndex].m_Transform;
                if (m_EndLength > 0f)
                {
                    Transform parent = transform.parent;
                    if (parent)
                    {
                        particle.m_EndOffset = transform.InverseTransformPoint(transform.position * 2f - parent.position) * m_EndLength;
                    }
                    else
                    {
                        particle.m_EndOffset = new Vector3(m_EndLength, 0f, 0f);
                    }
                }
                else
                {
                    particle.m_EndOffset = transform.InverseTransformPoint(base.transform.TransformDirection(m_EndOffset) + transform.position);
                }
                particle.m_Position = particle.m_PrevPosition = transform.TransformPoint(particle.m_EndOffset);
            }
            if (parentIndex >= 0)
            {
                boneLength += (m_Particles[parentIndex].m_Transform.position - particle.m_Position).magnitude;
                particle.m_BoneLength = boneLength;
                m_BoneTotalLength = Mathf.Max(m_BoneTotalLength, boneLength);
            }
            int count = m_Particles.Count;
            m_Particles.Add(particle);
            bool flag = false;
            int index = 0;
            if (!b)
            {
                return;
            }
            for (int i = 0; i < b.childCount; i++)
            {
                bool flag2 = false;
                if (m_Exclusions != null)
                {
                    for (int j = 0; j < m_Exclusions.Count; j++)
                    {
                        if (m_Exclusions[j] == b.GetChild(i))
                        {
                            flag2 = true;
                            break;
                        }
                    }
                }
                if (!flag2)
                {
                    for (int k = 0; k < m_notRolls.Count; k++)
                    {
                        if (m_notRolls[k] == b.GetChild(i))
                        {
                            flag = true;
                            flag2 = true;
                            index = i;
                            break;
                        }
                    }
                }
                if (!flag2)
                {
                    AppendParticles(b.GetChild(i), count, boneLength);
                }
                else if (m_EndLength > 0f || m_EndOffset != Vector3.zero)
                {
                    AppendParticles(null, count, boneLength);
                }
            }
            if (flag)
            {
                for (int l = 0; l < b.GetChild(index).childCount; l++)
                {
                    bool flag3 = false;
                    for (int m = 0; m < m_Exclusions.Count; m++)
                    {
                        if (m_Exclusions[m] == b.GetChild(index).GetChild(l))
                        {
                            flag3 = true;
                            break;
                        }
                    }
                    if (!flag3)
                    {
                        for (int n = 0; n < m_notRolls.Count; n++)
                        {
                            if (m_notRolls[n] == b.GetChild(index).GetChild(l))
                            {
                                flag = true;
                                flag3 = true;
                                break;
                            }
                        }
                    }
                    if (!flag3)
                    {
                        AppendParticles(b.GetChild(index).GetChild(l), count, boneLength);
                    }
                }
            }
            if (b.childCount == 0 && (m_EndLength > 0f || m_EndOffset != Vector3.zero))
            {
                AppendParticles(null, count, boneLength);
            }
        }

        public void UpdateParameters()
        {
            if (!m_Root)
            {
                return;
            }
            m_LocalGravity = m_Root.InverseTransformDirection(m_Gravity);
            for (int i = 0; i < m_Particles.Count; i++)
            {
                Particle particle = m_Particles[i];
                particle.m_Damping = m_Damping;
                particle.m_Elasticity = m_Elasticity;
                particle.m_Stiffness = m_Stiffness;
                particle.m_Inert = m_Inert;
                particle.m_Radius = m_Radius;
                if (m_BoneTotalLength > 0f)
                {
                    float time = particle.m_BoneLength / m_BoneTotalLength;
                    if (m_DampingDistrib != null && m_DampingDistrib.keys.Length != 0)
                    {
                        particle.m_Damping *= m_DampingDistrib.Evaluate(time);
                    }
                    if (m_ElasticityDistrib != null && m_ElasticityDistrib.keys.Length != 0)
                    {
                        particle.m_Elasticity *= m_ElasticityDistrib.Evaluate(time);
                    }
                    if (m_StiffnessDistrib != null && m_StiffnessDistrib.keys.Length != 0)
                    {
                        particle.m_Stiffness *= m_StiffnessDistrib.Evaluate(time);
                    }
                    if (m_InertDistrib != null && m_InertDistrib.keys.Length != 0)
                    {
                        particle.m_Inert *= m_InertDistrib.Evaluate(time);
                    }
                    if (m_RadiusDistrib != null && m_RadiusDistrib.keys.Length != 0)
                    {
                        particle.m_Radius *= m_RadiusDistrib.Evaluate(time);
                    }
                }
                particle.m_Damping = Mathf.Clamp01(particle.m_Damping);
                particle.m_Elasticity = Mathf.Clamp01(particle.m_Elasticity);
                particle.m_Stiffness = Mathf.Clamp01(particle.m_Stiffness);
                particle.m_Inert = Mathf.Clamp01(particle.m_Inert);
                particle.m_Radius = Mathf.Max(particle.m_Radius, 0f);
            }
        }

        private void InitTransforms()
        {
            for (int i = 0; i < m_Particles.Count; i++)
            {
                Particle particle = m_Particles[i];
                if (particle.m_Transform)
                {
                    particle.m_Transform.SetLocalPositionAndRotation(particle.m_InitLocalPosition, particle.m_InitLocalRotation);
                }
            }
        }

        public void ResetParticlesPosition()
        {
            for (int i = 0; i < m_Particles.Count; i++)
            {
                Particle particle = m_Particles[i];
                if (particle.m_Transform)
                {
                    particle.m_Position = particle.m_PrevPosition = particle.m_Transform.position;
                    continue;
                }
                Transform transform = m_Particles[particle.m_ParentIndex].m_Transform;
                particle.m_Position = particle.m_PrevPosition = transform.TransformPoint(particle.m_EndOffset);
            }
            m_ObjectPrevPosition = transform.position;
        }

        private void UpdateParticles1()
        {
            Vector3 gravity = m_Gravity;
            Vector3 normalized = m_Gravity.normalized;
            Vector3 lhs = m_Root.TransformDirection(m_LocalGravity);
            Vector3 vector = normalized * Mathf.Max(Vector3.Dot(lhs, normalized), 0f);
            gravity -= vector;
            gravity = (gravity + m_Force) * m_ObjectScale;
            for (int i = 0; i < m_Particles.Count; i++)
            {
                Particle particle = m_Particles[i];
                if (particle.m_ParentIndex >= 0)
                {
                    Vector3 vector2 = particle.m_Position - particle.m_PrevPosition;
                    Vector3 vector3 = m_ObjectMove * particle.m_Inert;
                    particle.m_PrevPosition = particle.m_Position + vector3;
                    particle.m_Position += vector2 * (1f - particle.m_Damping) + gravity + vector3;
                }
                else
                {
                    particle.m_PrevPosition = particle.m_Position;
                    particle.m_Position = particle.m_Transform.position;
                }
            }
        }

        private void UpdateParticles2()
        {
            Plane plane = default(Plane);
            for (int i = 1; i < m_Particles.Count; i++)
            {
                Particle particle = m_Particles[i];
                Particle particle2 = m_Particles[particle.m_ParentIndex];
                float num = !(particle.m_Transform != null) ? particle2.m_Transform.localToWorldMatrix.MultiplyVector(particle.m_EndOffset).magnitude : (particle2.m_Transform.position - particle.m_Transform.position).magnitude;
                float num2 = Mathf.Lerp(1f, particle.m_Stiffness, m_Weight);
                if (num2 > 0f || particle.m_Elasticity > 0f)
                {
                    Matrix4x4 localToWorldMatrix = particle2.m_Transform.localToWorldMatrix;
                    localToWorldMatrix.SetColumn(3, particle2.m_Position);
                    Vector3 vector = !(particle.m_Transform != null) ? localToWorldMatrix.MultiplyPoint3x4(particle.m_EndOffset) : localToWorldMatrix.MultiplyPoint3x4(particle.m_Transform.localPosition);
                    Vector3 vector2 = vector - particle.m_Position;
                    particle.m_Position += vector2 * particle.m_Elasticity;
                    if (num2 > 0f)
                    {
                        vector2 = vector - particle.m_Position;
                        float magnitude = vector2.magnitude;
                        float num3 = num * (1f - num2) * 2f;
                        if (magnitude > num3)
                        {
                            particle.m_Position += vector2 * ((magnitude - num3) / magnitude);
                        }
                    }
                }
                if (m_Colliders != null)
                {
                    float particleRadius = particle.m_Radius * m_ObjectScale;
                    for (int j = 0; j < m_Colliders.Count; j++)
                    {
                        DynamicBoneColliderBase dynamicBoneColliderBase = m_Colliders[j];
                        if (dynamicBoneColliderBase != null && dynamicBoneColliderBase.enabled)
                        {
                            dynamicBoneColliderBase.Collide(ref particle.m_Position, particleRadius);
                        }
                    }
                }
                if (m_FreezeAxis != 0)
                {
                    switch (m_FreezeAxis)
                    {
                        case FreezeAxis.X:
                            plane.SetNormalAndPosition(particle2.m_Transform.right, particle2.m_Position);
                            break;
                        case FreezeAxis.Y:
                            plane.SetNormalAndPosition(particle2.m_Transform.up, particle2.m_Position);
                            break;
                        case FreezeAxis.Z:
                            plane.SetNormalAndPosition(particle2.m_Transform.forward, particle2.m_Position);
                            break;
                    }
                    particle.m_Position -= plane.normal * plane.GetDistanceToPoint(particle.m_Position);
                }
                Vector3 vector3 = particle2.m_Position - particle.m_Position;
                float magnitude2 = vector3.magnitude;
                if (magnitude2 > 0f)
                {
                    particle.m_Position += vector3 * ((magnitude2 - num) / magnitude2);
                }
            }
        }

        private void SkipUpdateParticles()
        {
            for (int i = 0; i < m_Particles.Count; i++)
            {
                Particle particle = m_Particles[i];
                if (particle.m_ParentIndex >= 0)
                {
                    particle.m_PrevPosition += m_ObjectMove;
                    particle.m_Position += m_ObjectMove;
                    Particle particle2 = m_Particles[particle.m_ParentIndex];
                    float num = !(particle.m_Transform != null) ? particle2.m_Transform.localToWorldMatrix.MultiplyVector(particle.m_EndOffset).magnitude : (particle2.m_Transform.position - particle.m_Transform.position).magnitude;
                    float num2 = Mathf.Lerp(1f, particle.m_Stiffness, m_Weight);
                    if (num2 > 0f)
                    {
                        Matrix4x4 localToWorldMatrix = particle2.m_Transform.localToWorldMatrix;
                        localToWorldMatrix.SetColumn(3, particle2.m_Position);
                        Vector3 vector = !(particle.m_Transform != null) ? localToWorldMatrix.MultiplyPoint3x4(particle.m_EndOffset) : localToWorldMatrix.MultiplyPoint3x4(particle.m_Transform.localPosition);
                        Vector3 vector2 = vector - particle.m_Position;
                        float magnitude = vector2.magnitude;
                        float num3 = num * (1f - num2) * 2f;
                        if (magnitude > num3)
                        {
                            particle.m_Position += vector2 * ((magnitude - num3) / magnitude);
                        }
                    }
                    Vector3 vector3 = particle2.m_Position - particle.m_Position;
                    float magnitude2 = vector3.magnitude;
                    if (magnitude2 > 0f)
                    {
                        particle.m_Position += vector3 * ((magnitude2 - num) / magnitude2);
                    }
                }
                else
                {
                    particle.m_PrevPosition = particle.m_Position;
                    particle.m_Position = particle.m_Transform.position;
                }
            }
        }

        private static Vector3 MirrorVector(Vector3 v, Vector3 axis)
        {
            return v - axis * (Vector3.Dot(v, axis) * 2f);
        }

        private void ApplyParticlesToTransforms()
        {
            for (int i = 1; i < m_Particles.Count; i++)
            {
                Particle particle = m_Particles[i];
                Particle particle2 = m_Particles[particle.m_ParentIndex];
                if (particle2.m_Transform.childCount <= 1)
                {
                    Vector3 direction = !particle.m_Transform ? particle.m_EndOffset : particle.m_Transform.localPosition;
                    Vector3 toDirection = particle.m_Position - particle2.m_Position;
                    Quaternion quaternion = Quaternion.FromToRotation(particle2.m_Transform.TransformDirection(direction), toDirection);
                    particle2.m_Transform.rotation = quaternion * particle2.m_Transform.rotation;
                }
                if (particle.m_Transform)
                {
                    particle.m_Transform.position = particle.m_Position;
                }
            }
        }
        private void ScheduleParticlesJob()
        {
            NativeArray<ParticleStruct> calcs = new(m_Particles.Count, Allocator.TempJob);
            NativeArray<CollisionStruct> colls = new(m_Colliders.Count, Allocator.TempJob);
            for (int i = 0; i < m_Particles.Count; i++)
            {
                Particle particle = m_Particles[i];
                calcs[i] = new ParticleStruct
                {
                    parentIndex = particle.m_ParentIndex,
                    damping = particle.m_Damping,
                    elasticity = particle.m_Elasticity,
                    stiffness = particle.m_Stiffness,
                    inert = particle.m_Inert,
                    radius = particle.m_Radius,
                    boneLength = particle.m_BoneLength,
                    isTransform = particle.m_Transform ? 1 : 0,
                    transWorldPosition = particle.m_Transform ? particle.m_Transform.position : float3.zero,
                    transLocalPosition = particle.m_Transform ? particle.m_Transform.localPosition : float3.zero,
                    position = particle.m_Position,
                    prevPosition = particle.m_PrevPosition,
                    endOffset = particle.m_EndOffset,
                    initLocalPosition = particle.m_InitLocalPosition,
                    initLocalRotation = particle.m_InitLocalRotation,
                    worldMatrix = particle.m_Transform ? particle.m_Transform.localToWorldMatrix : float4x4.identity
                };
            }
            for (int j = 0; j < m_Colliders.Count; j++)
            {
                DynamicBoneCollider dynamicBoneCollider = m_Colliders[j] as DynamicBoneCollider;
                if (dynamicBoneCollider && dynamicBoneCollider.enabled)
                {
                    colls[j] = new CollisionStruct
                    {
                        direction = dynamicBoneCollider.m_Direction,
                        center = dynamicBoneCollider.m_Center,
                        bound = dynamicBoneCollider.m_Bound,
                        radius = dynamicBoneCollider.m_Radius,
                        height = dynamicBoneCollider.m_Height,
                        lossyScale = dynamicBoneCollider.transform.lossyScale,
                        worldMatrix = dynamicBoneCollider.transform.localToWorldMatrix
                    };
                }
            }
            CalcJob jobData = new()
            {
                Particles = calcs,
                Colliders = colls,
                Weight = m_Weight,
                ObjectScale = m_ObjectScale
            };
            jobData.Schedule(calcs.Length - 1, default).Complete();
            for (int k = 0; k < m_Particles.Count; k++)
            {
                m_Particles[k].m_Position = calcs[k].position;
            }
            calcs.Dispose();
            colls.Dispose();
        }
    }
}
