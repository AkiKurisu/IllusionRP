using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
#if ODIN_INSPECTOR
using Sirenix.OdinInspector;
#endif

namespace AIChara.Bone
{
	[AddComponentMenu("Dynamic Bone/Dynamic Bone Ver02")]
	public class DynamicBone_Ver02 : MonoBehaviour
	{
		public delegate void LateUpdateDecision(DynamicBone_Ver02 dynamicBone, ref bool shouldRun);

		public static event LateUpdateDecision LateUpdateDeciding;

		private void Awake()
		{
			Init();
		}

		public void Init()
		{
			InitNodeParticle();
			SetupParticles();
			InitLocalPosition();
			if (IsRefTransform())
			{
				SetPtn(0, true);
			}
			InitTransforms();
		}

		public void ReinitializeTransforms()
		{
			InitTransforms();
		}

		public void UpdateDynamicBonesForPoseEditor(float deltaTime)
		{
			UpdateDynamicBones(deltaTime);
		}

		private void LateUpdate()
		{
			bool shouldRun = true;
			LateUpdateDeciding?.Invoke(this, ref shouldRun);
			if (!shouldRun)
			{
				return;
			}

			if (Weight > 0f)
			{
				InitTransforms();
				UpdateDynamicBones(Time.deltaTime);
			}
		}

		private void OnEnable()
		{
			ResetParticlesPosition();
			if (Root)
			{
				ObjectPrevPosition = Root.position;
				return;
			}

			ObjectPrevPosition = transform.position;
		}

		private void OnDisable()
		{
			InitTransforms();
		}

		private void OnValidate()
		{
			UpdateRate = Mathf.Max(UpdateRate, 0f);
			if (Application.isEditor)
			{
				InitNodeParticle();
				SetupParticles();
				InitLocalPosition();
				if (IsRefTransform())
				{
					SetPtn(PtnNo, true);
				}
				InitTransforms();
			}
		}
		private void OnDrawGizmosSelected()
		{
			if (!enabled || Root == null)
			{
				return;
			}
			if (Application.isEditor && !Application.isPlaying && transform.hasChanged)
			{
				InitNodeParticle();
				SetupParticles();
				InitLocalPosition();
				if (IsRefTransform())
				{
					SetPtn(PtnNo, true);
				}
				InitTransforms();
			}
			Gizmos.color = Color.white;
			foreach (Particle particle in Particles)
			{
				if (particle.ParentIndex >= 0)
				{
					Particle particle2 = Particles[particle.ParentIndex];
					Gizmos.DrawLine(particle.Position, particle2.Position);
				}
				if (particle.Radius > 0f)
				{
					Gizmos.DrawWireSphere(particle.Position, particle.Radius * ObjectScale);
				}
			}
		}
		public void SetWeight(float _weight)
		{
			if (Weight != _weight)
			{
				if (_weight == 0f)
				{
					InitTransforms();
				}
				else if (Weight == 0f)
				{
					ResetParticlesPosition();
					if (Root != null)
					{
						ObjectPrevPosition = Root.position;
					}
					else
					{
						ObjectPrevPosition = transform.position;
					}
				}
				Weight = _weight;
			}
		}

		public float GetWeight()
		{
			return Weight;
		}

		public void SetRoot(Transform _transRoot)
		{
			Root = _transRoot;
		}

		public Particle GetParticle(int index)
		{
			if (index >= Particles.Count)
			{
				return null;
			}
			return Particles[index];
		}

		public int GetParticleCount()
		{
			return Particles.Count;
		}

		public bool SetPtn(int ptn, bool isSameForcePtn = false)
		{
			if (Particles == null || Patterns == null)
			{
				return false;
			}
			if (Particles.Count == 0 || Patterns.Count == 0)
			{
				return false;
			}
			if (Patterns.Count <= ptn)
			{
				return false;
			}
			if (Particles.Count != Patterns[ptn].ParticlePtns.Count)
			{
				return false;
			}
			if (PtnNo == ptn && !isSameForcePtn)
			{
				return false;
			}
			PtnNo = ptn;
			Gravity = Patterns[ptn].Gravity;
			for (int i = 0; i < Particles.Count; i++)
			{
				Particle particle = Particles[i];
				ParticlePtn particlePtn = Patterns[ptn].ParticlePtns[i];
				particle.IsRotationCalc = particlePtn.IsRotationCalc;
				particle.Damping = particlePtn.Damping;
				particle.Elasticity = particlePtn.Elasticity;
				particle.Stiffness = particlePtn.Stiffness;
				particle.Inert = particlePtn.Inert;
				particle.ScaleNextBoneLength = particlePtn.ScaleNextBoneLength;
				particle.Radius = particlePtn.Radius;
				particle.IsMoveLimit = particlePtn.IsMoveLimit;
				particle.MoveLimitMin = particlePtn.MoveLimitMin;
				particle.MoveLimitMax = particlePtn.MoveLimitMax;
				particle.KeepLengthLimitMin = particlePtn.KeepLengthLimitMin;
				particle.KeepLengthLimitMax = particlePtn.KeepLengthLimitMax;
				particle.IsCrush = particlePtn.IsCrush;
				particle.CrushMoveAreaMin = particlePtn.CrushMoveAreaMin;
				particle.CrushMoveAreaMax = particlePtn.CrushMoveAreaMax;
				particle.CrushAddXYMin = particlePtn.CrushAddXYMin;
				particle.CrushAddXYMax = particlePtn.CrushAddXYMax;
				particle.Damping = Mathf.Clamp01(particle.Damping);
				particle.Elasticity = Mathf.Clamp01(particle.Elasticity);
				particle.Stiffness = Mathf.Clamp01(particle.Stiffness);
				particle.Inert = Mathf.Clamp01(particle.Inert);
				particle.ScaleNextBoneLength = Mathf.Max(particle.ScaleNextBoneLength, 0f);
				particle.Radius = Mathf.Max(particle.Radius, 0f);
				particle.InitLocalPosition = particlePtn.InitLocalPosition;
				particle.InitLocalRotation = particlePtn.InitLocalRotation;
				particle.InitLocalScale = particlePtn.InitLocalScale;
				particle.refTrans = particlePtn.refTrans;
				particle.LocalPosition = particlePtn.LocalPosition;
				particle.EndOffset = particlePtn.EndOffset;
			}
			return true;
		}
		public void ResetParticlesPosition()
		{
			if (Root)
			{
				ObjectPrevPosition = Root.position;
			}
			else
			{
				ObjectPrevPosition = transform.position;
			}
			foreach (Particle particle in Particles)
			{
				if (particle.Transform)
				{
					particle.Position = particle.PrevPosition = particle.Transform.position;
				}
				else
				{
					Transform transform = Particles[particle.ParentIndex].Transform;
					particle.Position = particle.PrevPosition = transform.TransformPoint(particle.EndOffset);
				}
			}
		}

		public void InitLocalPosition()
		{
			List<TransformParam> list = new();
			for (int i = 0; i < Particles.Count; i++)
			{
				Particle particle = Particles[i];
				TransformParam transformParam = new();
				if (!particle.Transform)
				{
					list.Add(transformParam);
				}
				else
				{
					transformParam.pos = particle.Transform.localPosition;
					transformParam.rot = particle.Transform.localRotation;
					transformParam.scale = particle.Transform.localScale;
					list.Add(transformParam);
				}
			}
			for (int j = 0; j < Patterns.Count; j++)
			{
				BonePtn bonePtn = Patterns[j];
				for (int k = 0; k < bonePtn.Params.Count; k++)
				{
					bonePtn.ParticlePtns[k].InitLocalPosition = bonePtn.Params[k].RefTransform.localPosition;
					bonePtn.ParticlePtns[k].InitLocalRotation = bonePtn.Params[k].RefTransform.localRotation;
					bonePtn.ParticlePtns[k].InitLocalScale = bonePtn.Params[k].RefTransform.localScale;
					bonePtn.ParticlePtns[k].refTrans = bonePtn.Params[k].RefTransform;
				}
				if (bonePtn.ParticlePtns.Count == Particles.Count)
				{
					for (int l = 0; l < Particles.Count; l++)
					{
						Particle particle2 = Particles[l];
						if (particle2.Transform )
						{
							particle2.Transform.SetLocalPositionAndRotation(bonePtn.ParticlePtns[l].InitLocalPosition, bonePtn.ParticlePtns[l].InitLocalRotation);
							particle2.Transform.localScale = bonePtn.ParticlePtns[l].InitLocalScale;
						}
					}
				}
				for (int m = 1; m < bonePtn.Params.Count; m++)
				{
					if (bonePtn.Params[m].RefTransform && bonePtn.Params[m - 1].RefTransform)
					{
						bonePtn.ParticlePtns[m].LocalPosition = CalcLocalPosition(bonePtn.Params[m].RefTransform.position, bonePtn.Params[m - 1].RefTransform);
					}
				}
			}
			for (int n = 0; n < Particles.Count; n++)
			{
				Particle particle3 = Particles[n];
				if (!(particle3.Transform == null))
				{
					particle3.Transform.SetLocalPositionAndRotation(list[n].pos, list[n].rot);
					particle3.Transform.localScale = list[n].scale;
				}
			}
		}
		public void ResetPosition()
		{
			InitLocalPosition();
			SetPtn(PtnNo, true);
			if (enabled)
			{
				InitTransforms();
			}
		}

		public bool PtnBlend(int _blendAnswerPtn, int _blendPtn1, int _blendPtn2, float _t)
		{
			if (Patterns == null)
			{
				return false;
			}
			int count = Patterns.Count;
			if (count <= _blendAnswerPtn || count <= _blendPtn1 || count <= _blendPtn2)
			{
				return false;
			}
			if (Patterns[_blendAnswerPtn].ParticlePtns.Count != Patterns[_blendPtn1].ParticlePtns.Count || Patterns[_blendPtn2].ParticlePtns.Count != Patterns[_blendPtn1].ParticlePtns.Count)
			{
				return false;
			}
			Patterns[_blendAnswerPtn].Gravity = Vector3.Lerp(Patterns[_blendPtn1].Gravity, Patterns[_blendPtn2].Gravity, _t);
			for (int i = 0; i < Patterns[_blendAnswerPtn].ParticlePtns.Count; i++)
			{
				ParticlePtn particlePtn = Patterns[_blendAnswerPtn].ParticlePtns[i];
				ParticlePtn particlePtn2 = Patterns[_blendPtn1].ParticlePtns[i];
				ParticlePtn particlePtn3 = Patterns[_blendPtn2].ParticlePtns[i];
				particlePtn.IsRotationCalc = particlePtn3.IsRotationCalc;
				particlePtn.Damping = Mathf.Lerp(particlePtn2.Damping, particlePtn3.Damping, _t);
				particlePtn.Elasticity = Mathf.Lerp(particlePtn2.Elasticity, particlePtn3.Elasticity, _t);
				particlePtn.Stiffness = Mathf.Lerp(particlePtn2.Stiffness, particlePtn3.Stiffness, _t);
				particlePtn.Inert = Mathf.Lerp(particlePtn2.Inert, particlePtn3.Inert, _t);
				particlePtn.ScaleNextBoneLength = Mathf.Lerp(particlePtn2.ScaleNextBoneLength, particlePtn3.ScaleNextBoneLength, _t);
				particlePtn.Radius = Mathf.Lerp(particlePtn2.Radius, particlePtn3.Radius, _t);
				particlePtn.IsMoveLimit = particlePtn3.IsMoveLimit;
				particlePtn.MoveLimitMin = Vector3.Lerp(particlePtn2.MoveLimitMin, particlePtn3.MoveLimitMin, _t);
				particlePtn.MoveLimitMax = Vector3.Lerp(particlePtn2.MoveLimitMax, particlePtn3.MoveLimitMax, _t);
				particlePtn.KeepLengthLimitMin = Mathf.Lerp(particlePtn2.KeepLengthLimitMin, particlePtn3.KeepLengthLimitMin, _t);
				particlePtn.KeepLengthLimitMax = Mathf.Lerp(particlePtn2.KeepLengthLimitMax, particlePtn3.KeepLengthLimitMax, _t);
				particlePtn.IsCrush = particlePtn3.IsCrush;
				particlePtn.CrushMoveAreaMin = Mathf.Lerp(particlePtn2.CrushMoveAreaMin, particlePtn3.CrushMoveAreaMin, _t);
				particlePtn.CrushMoveAreaMax = Mathf.Lerp(particlePtn2.CrushMoveAreaMax, particlePtn3.CrushMoveAreaMax, _t);
				particlePtn.CrushAddXYMin = Mathf.Lerp(particlePtn2.CrushAddXYMin, particlePtn3.CrushAddXYMin, _t);
				particlePtn.CrushAddXYMax = Mathf.Lerp(particlePtn2.CrushAddXYMax, particlePtn3.CrushAddXYMax, _t);
				particlePtn.Damping = Mathf.Clamp01(particlePtn.Damping);
				particlePtn.Elasticity = Mathf.Clamp01(particlePtn.Elasticity);
				particlePtn.Stiffness = Mathf.Clamp01(particlePtn.Stiffness);
				particlePtn.Inert = Mathf.Clamp01(particlePtn.Inert);
				particlePtn.ScaleNextBoneLength = Mathf.Max(particlePtn.ScaleNextBoneLength, 0f);
				particlePtn.Radius = Mathf.Max(particlePtn.Radius, 0f);
				particlePtn.InitLocalPosition = Vector3.Lerp(particlePtn2.InitLocalPosition, particlePtn3.InitLocalPosition, _t);
				particlePtn.InitLocalRotation = Quaternion.Lerp(particlePtn2.InitLocalRotation, particlePtn3.InitLocalRotation, _t);
				particlePtn.InitLocalScale = Vector3.Lerp(particlePtn2.InitLocalScale, particlePtn3.InitLocalScale, _t);
				particlePtn.refTrans = particlePtn3.refTrans;
				particlePtn.LocalPosition = Vector3.Lerp(particlePtn2.LocalPosition, particlePtn3.LocalPosition, _t);
				particlePtn.EndOffset = Vector3.Lerp(particlePtn2.EndOffset, particlePtn3.EndOffset, _t);
			}
			return true;
		}

		public bool SetGravity(int _ptn, Vector3 _gravity, bool _isNowGravity = true)
		{
			if (Particles == null || Patterns == null)
			{
				return false;
			}
			if (Particles.Count == 0 || Patterns.Count == 0)
			{
				return false;
			}
			if (Patterns.Count <= _ptn)
			{
				return false;
			}
			if (_isNowGravity)
			{
				Gravity = _gravity;
			}
			if (_ptn < 0)
			{
				for (int i = 0; i < Patterns.Count; i++)
				{
					Patterns[i].Gravity = _gravity;
				}
			}
			else
			{
				if (Patterns.Count <= _ptn)
				{
					return false;
				}
				Patterns[_ptn].Gravity = _gravity;
			}
			return true;
		}

		public bool SetSoftParams(int _ptn, int _bone, float _damping, float _elasticity, float _stiffness, bool _isNowParam = true)
		{
			if (Particles == null || Patterns == null)
			{
				return false;
			}
			if (Particles.Count == 0 || Patterns.Count == 0)
			{
				return false;
			}
			if (Patterns.Count <= _ptn)
			{
				return false;
			}
			if (_isNowParam)
			{
				if (_bone == -1)
				{
					for (int i = 0; i < Particles.Count; i++)
					{
						Particles[i].Damping = _damping;
						Particles[i].Elasticity = _elasticity;
						Particles[i].Stiffness = _stiffness;
					}
				}
				else if (Particles.Count > _bone)
				{
					Particles[_bone].Damping = _damping;
					Particles[_bone].Elasticity = _elasticity;
					Particles[_bone].Stiffness = _stiffness;
				}
			}
			if (_ptn < 0)
			{
				for (int j = 0; j < Patterns.Count; j++)
				{
					if (_bone == -1)
					{
						for (int k = 0; k < Patterns[j].ParticlePtns.Count; k++)
						{
							SetSoftParams(Patterns[j].ParticlePtns[k], _damping, _elasticity, _stiffness);
						}
						for (int l = 0; l < Patterns[j].Params.Count; l++)
						{
							SetSoftParams(Patterns[j].Params[l], _damping, _elasticity, _stiffness);
						}
					}
					else
					{
						if (Patterns[j].ParticlePtns.Count > _bone)
						{
							SetSoftParams(Patterns[j].ParticlePtns[_bone], _damping, _elasticity, _stiffness);
						}
						if (Patterns[j].Params.Count > _bone)
						{
							SetSoftParams(Patterns[j].Params[_bone], _damping, _elasticity, _stiffness);
						}
					}
				}
			}
			else
			{
				if (Patterns.Count <= _ptn)
				{
					return false;
				}
				if (_bone == -1)
				{
					for (int m = 0; m < Patterns[_ptn].ParticlePtns.Count; m++)
					{
						SetSoftParams(Patterns[_ptn].ParticlePtns[m], _damping, _elasticity, _stiffness);
					}
					for (int n = 0; n < Patterns[_ptn].Params.Count; n++)
					{
						SetSoftParams(Patterns[_ptn].Params[n], _damping, _elasticity, _stiffness);
					}
				}
				else
				{
					if (Patterns[_ptn].ParticlePtns.Count > _bone)
					{
						SetSoftParams(Patterns[_ptn].ParticlePtns[_bone], _damping, _elasticity, _stiffness);
					}
					if (Patterns[_ptn].Params.Count > _bone)
					{
						SetSoftParams(Patterns[_ptn].Params[_bone], _damping, _elasticity, _stiffness);
					}
				}
			}
			return true;
		}

		private bool SetSoftParams(ParticlePtn _ptn, float _damping, float _elasticity, float _stiffness)
		{
			_ptn.Damping = _damping;
			_ptn.Elasticity = _elasticity;
			_ptn.Stiffness = _stiffness;
			return true;
		}

		private bool SetSoftParams(BoneParameter _ptn, float _damping, float _elasticity, float _stiffness)
		{
			_ptn.Damping = _damping;
			_ptn.Elasticity = _elasticity;
			_ptn.Stiffness = _stiffness;
			return true;
		}

		public bool SetRotationCalcParams(int _ptn, int _bone, bool _enable, bool _isNowParam = true)
		{
			if (Particles == null || Patterns == null)
			{
				return false;
			}
			if (Particles.Count == 0 || Patterns.Count == 0)
			{
				return false;
			}
			if (Patterns.Count <= _ptn)
			{
				return false;
			}
			if (_isNowParam)
			{
				if (_bone == -1)
				{
					for (int i = 0; i < Particles.Count; i++)
					{
						Particles[i].IsRotationCalc = _enable;
					}
				}
				else if (Particles.Count > _bone)
				{
					Particles[_bone].IsRotationCalc = _enable;
				}
			}
			if (_ptn < 0)
			{
				for (int j = 0; j < Patterns.Count; j++)
				{
					if (_bone == -1)
					{
						for (int k = 0; k < Patterns[j].ParticlePtns.Count; k++)
						{
							Patterns[j].ParticlePtns[k].IsRotationCalc = _enable;
						}
						for (int l = 0; l < Patterns[j].Params.Count; l++)
						{
							Patterns[j].Params[l].IsRotationCalc = _enable;
						}
					}
					else
					{
						if (Patterns[j].ParticlePtns.Count > _bone)
						{
							Patterns[j].ParticlePtns[_bone].IsRotationCalc = _enable;
						}
						if (Patterns[j].Params.Count > _bone)
						{
							Patterns[j].Params[_bone].IsRotationCalc = _enable;
						}
					}
				}
			}
			else
			{
				if (Patterns.Count <= _ptn)
				{
					return false;
				}
				if (_bone == -1)
				{
					for (int m = 0; m < Patterns[_ptn].ParticlePtns.Count; m++)
					{
						Patterns[_ptn].ParticlePtns[m].IsRotationCalc = _enable;
					}
					for (int n = 0; n < Patterns[_ptn].Params.Count; n++)
					{
						Patterns[_ptn].Params[n].IsRotationCalc = _enable;
					}
				}
				else
				{
					if (Patterns[_ptn].ParticlePtns.Count > _bone)
					{
						Patterns[_ptn].ParticlePtns[_bone].IsRotationCalc = _enable;
					}
					if (Patterns[_ptn].Params.Count > _bone)
					{
						Patterns[_ptn].Params[_bone].IsRotationCalc = _enable;
					}
				}
			}
			return true;
		}

		public bool SetMoveLimitParams(int _ptn, int _bone, bool _enable, bool _isNowParam = true)
		{
			if (Particles == null || Patterns == null)
			{
				return false;
			}
			if (Particles.Count == 0 || Patterns.Count == 0)
			{
				return false;
			}
			if (Patterns.Count <= _ptn)
			{
				return false;
			}
			if (_isNowParam)
			{
				if (_bone == -1)
				{
					for (int i = 0; i < Particles.Count; i++)
					{
						Particles[i].IsMoveLimit = _enable;
					}
				}
				else if (Particles.Count > _bone)
				{
					Particles[_bone].IsMoveLimit = _enable;
				}
			}
			if (_ptn < 0)
			{
				for (int j = 0; j < Patterns.Count; j++)
				{
					if (_bone == -1)
					{
						for (int k = 0; k < Patterns[j].ParticlePtns.Count; k++)
						{
							Patterns[j].ParticlePtns[k].IsMoveLimit = _enable;
						}
						for (int l = 0; l < Patterns[j].Params.Count; l++)
						{
							Patterns[j].Params[l].IsMoveLimit = _enable;
						}
					}
					else
					{
						if (Patterns[j].ParticlePtns.Count > _bone)
						{
							Patterns[j].ParticlePtns[_bone].IsMoveLimit = _enable;
						}
						if (Patterns[j].Params.Count > _bone)
						{
							Patterns[j].Params[_bone].IsMoveLimit = _enable;
						}
					}
				}
			}
			else
			{
				if (Patterns.Count <= _ptn)
				{
					return false;
				}
				if (_bone == -1)
				{
					for (int m = 0; m < Patterns[_ptn].ParticlePtns.Count; m++)
					{
						Patterns[_ptn].ParticlePtns[m].IsMoveLimit = _enable;
					}
					for (int n = 0; n < Patterns[_ptn].Params.Count; n++)
					{
						Patterns[_ptn].Params[n].IsMoveLimit = _enable;
					}
				}
				else
				{
					if (Patterns[_ptn].ParticlePtns.Count > _bone)
					{
						Patterns[_ptn].ParticlePtns[_bone].IsMoveLimit = _enable;
					}
					if (Patterns[_ptn].Params.Count > _bone)
					{
						Patterns[_ptn].Params[_bone].IsMoveLimit = _enable;
					}
				}
			}
			return true;
		}

		public bool SetMoveLimitData(int _ptn, int _bone, Vector3 _min, Vector3 _max, bool _isNowParam = true)
		{
			if (Particles == null || Patterns == null)
			{
				return false;
			}
			if (Particles.Count == 0 || Patterns.Count == 0)
			{
				return false;
			}
			if (Patterns.Count <= _ptn)
			{
				return false;
			}
			if (_isNowParam)
			{
				if (_bone == -1)
				{
					for (int i = 0; i < Particles.Count; i++)
					{
						Particles[i].MoveLimitMin = _min;
						Particles[i].MoveLimitMax = _max;
					}
				}
				else if (Particles.Count > _bone)
				{
					Particles[_bone].MoveLimitMin = _min;
					Particles[_bone].MoveLimitMax = _max;
				}
			}
			if (_ptn < 0)
			{
				for (int j = 0; j < Patterns.Count; j++)
				{
					if (_bone == -1)
					{
						for (int k = 0; k < Patterns[j].ParticlePtns.Count; k++)
						{
							Patterns[j].ParticlePtns[k].MoveLimitMin = _min;
							Patterns[j].ParticlePtns[k].MoveLimitMax = _max;
						}
						for (int l = 0; l < Patterns[j].Params.Count; l++)
						{
							Patterns[j].Params[l].MoveLimitMin = _min;
							Patterns[j].Params[l].MoveLimitMax = _max;
						}
					}
					else
					{
						if (Patterns[j].ParticlePtns.Count > _bone)
						{
							Patterns[j].ParticlePtns[_bone].MoveLimitMin = _min;
							Patterns[j].ParticlePtns[_bone].MoveLimitMax = _max;
						}
						if (Patterns[j].Params.Count > _bone)
						{
							Patterns[j].Params[_bone].MoveLimitMin = _min;
							Patterns[j].Params[_bone].MoveLimitMax = _max;
						}
					}
				}
			}
			else
			{
				if (Patterns.Count <= _ptn)
				{
					return false;
				}
				if (_bone == -1)
				{
					for (int m = 0; m < Patterns[_ptn].ParticlePtns.Count; m++)
					{
						Patterns[_ptn].ParticlePtns[m].MoveLimitMin = _min;
						Patterns[_ptn].ParticlePtns[m].MoveLimitMax = _max;
					}
					for (int n = 0; n < Patterns[_ptn].Params.Count; n++)
					{
						Patterns[_ptn].Params[n].MoveLimitMin = _min;
						Patterns[_ptn].Params[n].MoveLimitMax = _max;
					}
				}
				else
				{
					if (Patterns[_ptn].ParticlePtns.Count > _bone)
					{
						Patterns[_ptn].ParticlePtns[_bone].MoveLimitMin = _min;
						Patterns[_ptn].ParticlePtns[_bone].MoveLimitMax = _max;
					}
					if (Patterns[_ptn].Params.Count > _bone)
					{
						Patterns[_ptn].Params[_bone].MoveLimitMin = _min;
						Patterns[_ptn].Params[_bone].MoveLimitMax = _max;
					}
				}
			}
			return true;
		}


		public bool setSoftParamsEx(int _ptn, int _bone, float _inert, bool _isNowParam = true)
		{
			if (Particles == null || Patterns == null)
			{
				return false;
			}
			if (Particles.Count == 0 || Patterns.Count == 0)
			{
				return false;
			}
			if (Patterns.Count <= _ptn)
			{
				return false;
			}
			if (_isNowParam)
			{
				if (_bone == -1)
				{
					for (int i = 0; i < Particles.Count; i++)
					{
						Particles[i].Inert = _inert;
					}
				}
				else if (Particles.Count > _bone)
				{
					Particles[_bone].Inert = _inert;
				}
			}
			if (_ptn < 0)
			{
				for (int j = 0; j < Patterns.Count; j++)
				{
					if (_bone == -1)
					{
						for (int k = 0; k < Patterns[j].ParticlePtns.Count; k++)
						{
							Patterns[j].ParticlePtns[k].Inert = _inert;
						}
						for (int l = 0; l < Patterns[j].Params.Count; l++)
						{
							Patterns[j].Params[l].Inert = _inert;
						}
					}
					else
					{
						if (Patterns[j].ParticlePtns.Count > _bone)
						{
							Patterns[j].ParticlePtns[_bone].Inert = _inert;
						}
						if (Patterns[j].Params.Count > _bone)
						{
							Patterns[j].Params[_bone].Inert = _inert;
						}
					}
				}
			}
			else
			{
				if (Patterns.Count <= _ptn)
				{
					return false;
				}
				if (_bone == -1)
				{
					for (int m = 0; m < Patterns[_ptn].ParticlePtns.Count; m++)
					{
						Patterns[_ptn].ParticlePtns[m].Inert = _inert;
					}
					for (int n = 0; n < Patterns[_ptn].Params.Count; n++)
					{
						Patterns[_ptn].Params[n].Inert = _inert;
					}
				}
				else
				{
					if (Patterns[_ptn].ParticlePtns.Count > _bone)
					{
						Patterns[_ptn].ParticlePtns[_bone].Inert = _inert;
					}
					if (Patterns[_ptn].Params.Count > _bone)
					{
						Patterns[_ptn].Params[_bone].Inert = _inert;
					}
				}
			}
			return true;
		}

		public bool LoadTextList(List<string> list)
		{
			LoadInfo loadInfo = new();
			int num = 0;
			while (list.Count > num && LoadText(loadInfo, list, ref num))
			{
			}
			if (list.Count > num)
			{
				return false;
			}
			Comment = loadInfo.Comment;
			ReflectSpeed = loadInfo.ReflectSpeed;
			HeavyLoopMaxCount = loadInfo.HeavyLoopMaxCount;
			Colliders = new List<DynamicBoneColliderBase>(loadInfo.Colliders);
			Bones = new List<Transform>(loadInfo.Bones);
			Patterns = new List<BonePtn>();
			foreach (BonePtn bonePtn in loadInfo.Patterns)
			{
				BonePtn bonePtn2 = new()
				{
					Name = bonePtn.Name,
					Gravity = bonePtn.Gravity,
					EndOffset = bonePtn.EndOffset,
					EndOffsetDamping = bonePtn.EndOffsetDamping,
					EndOffsetElasticity = bonePtn.EndOffsetElasticity,
					EndOffsetStiffness = bonePtn.EndOffsetStiffness,
					EndOffsetInert = bonePtn.EndOffsetInert
				};
				foreach (BoneParameter boneParameter in bonePtn.Params)
				{
					BoneParameter boneParameter2 = new()
					{
						Name = boneParameter.Name,
						RefTransform = boneParameter.RefTransform,
						IsRotationCalc = boneParameter.IsRotationCalc,
						Damping = boneParameter.Damping,
						Elasticity = boneParameter.Elasticity,
						Stiffness = boneParameter.Stiffness,
						Inert = boneParameter.Inert,
						NextBoneLength = boneParameter.NextBoneLength,
						CollisionRadius = boneParameter.CollisionRadius,
						IsMoveLimit = boneParameter.IsMoveLimit,
						MoveLimitMin = boneParameter.MoveLimitMin,
						MoveLimitMax = boneParameter.MoveLimitMax,
						KeepLengthLimitMin = boneParameter.KeepLengthLimitMin,
						KeepLengthLimitMax = boneParameter.KeepLengthLimitMax,
						IsCrush = boneParameter.IsCrush,
						CrushMoveAreaMin = boneParameter.CrushMoveAreaMin,
						CrushMoveAreaMax = boneParameter.CrushMoveAreaMax,
						CrushAddXYMin = boneParameter.CrushAddXYMin,
						CrushAddXYMax = boneParameter.CrushAddXYMax
					};
					bonePtn2.Params.Add(boneParameter2);
				}
				Patterns.Add(bonePtn2);
			}
			InitNodeParticle();
			SetupParticles();
			InitLocalPosition();
			if (IsRefTransform())
			{
				SetPtn(0, true);
			}
			InitTransforms();
			return true;
		}

		private void UpdateDynamicBones(float deltaTime)
		{
			if (!Root)
			{
				return;
			}
			ObjectScale = Mathf.Abs(Root.lossyScale.x);
			ObjectMove = Root.position - ObjectPrevPosition;
			ObjectPrevPosition = Root.position;
			int num = 1;
			if (UpdateRate > 0f)
			{
				float num2 = 1f / UpdateRate;
				UpdateTime += deltaTime;
				num = 0;
				while (UpdateTime >= num2)
				{
					UpdateTime -= num2;
					if (++num >= HeavyLoopMaxCount)
					{
						UpdateTime = 0f;
						break;
					}
				}
			}
			if (num > 0)
			{
				for (int i = 0; i < num; i++)
				{
					UpdateParticles1();
					UpdateParticles2();
					ObjectMove = Vector3.zero;
				}
			}
			else
			{
				SkipUpdateParticles();
			}
			ApplyParticlesToTransforms();
		}

		private void InitNodeParticle()
		{
			if (Patterns == null)
			{
				return;
			}
			foreach (BonePtn bonePtn in Patterns)
			{
				if (bonePtn.ParticlePtns != null)
				{
					bonePtn.ParticlePtns.Clear();
				}
				else
				{
					bonePtn.ParticlePtns = new List<ParticlePtn>();
				}
				if (bonePtn.Params.Count == Bones.Count)
				{
					foreach (var item in Bones.Select((value, idx) => new
					{
						value,
						idx
					}))
					{
						bonePtn.ParticlePtns.Add(AppendParticlePtn(bonePtn.Params[item.idx], Vector3.zero));
					}
					BoneParameter boneParameter = new()
					{
						Damping = bonePtn.EndOffsetDamping,
						Elasticity = bonePtn.EndOffsetElasticity,
						Stiffness = bonePtn.EndOffsetStiffness,
						Inert = bonePtn.EndOffsetInert
					};
					bonePtn.ParticlePtns.Add(AppendParticlePtn(boneParameter, bonePtn.EndOffset));
				}
			}
		}
		private void SetupParticles()
		{
			Particles.Clear();
			if (!Root && Bones.Count > 0)
			{
				Root = Bones[0];
			}
			if (!Root)
			{
				return;
			}
			if (Bones == null || Patterns == null)
			{
				return;
			}
			if (Bones.Count == 0 || Patterns.Count == 0)
			{
				return;
			}
			if (Bones.Count != Patterns[0].Params.Count)
			{
				return;
			}
			ObjectScale = Root.lossyScale.x;
			ObjectPrevPosition = Root.position;
			ObjectMove = Vector3.zero;
			int num = -1;
			foreach (var pair in Bones.Select((value, idx) => new
			{
				value,
				idx
			}))
			{
				AppendParticles(pair.value, Patterns[0].Params[pair.idx], Vector3.zero, num);
				num++;
			}
			AppendParticles(null, new BoneParameter(), Patterns[0].EndOffset, num);
		}

		private static ParticlePtn AppendParticlePtn(BoneParameter parameter, Vector3 endOffset)
		{
			ParticlePtn particlePtn = new()
			{
				IsRotationCalc = parameter.IsRotationCalc,
				Damping = parameter.Damping,
				Elasticity = parameter.Elasticity,
				Stiffness = parameter.Stiffness,
				Inert = parameter.Inert,
				ScaleNextBoneLength = parameter.NextBoneLength,
				Radius = parameter.CollisionRadius,
				IsMoveLimit = parameter.IsMoveLimit,
				MoveLimitMin = parameter.MoveLimitMin,
				MoveLimitMax = parameter.MoveLimitMax,
				KeepLengthLimitMin = parameter.KeepLengthLimitMin,
				KeepLengthLimitMax = parameter.KeepLengthLimitMax,
				IsCrush = parameter.IsCrush,
				CrushMoveAreaMin = parameter.CrushMoveAreaMin,
				CrushMoveAreaMax = parameter.CrushMoveAreaMax,
				CrushAddXYMin = parameter.CrushAddXYMin,
				CrushAddXYMax = parameter.CrushAddXYMax
			};
			particlePtn.Damping = Mathf.Clamp01(particlePtn.Damping);
			particlePtn.Elasticity = Mathf.Clamp01(particlePtn.Elasticity);
			particlePtn.Stiffness = Mathf.Clamp01(particlePtn.Stiffness);
			particlePtn.Inert = Mathf.Clamp01(particlePtn.Inert);
			particlePtn.ScaleNextBoneLength = Mathf.Max(particlePtn.ScaleNextBoneLength, 0f);
			particlePtn.Radius = Mathf.Max(particlePtn.Radius, 0f);
			if (parameter.RefTransform)
			{
				particlePtn.InitLocalPosition = parameter.RefTransform.localPosition;
				particlePtn.InitLocalRotation = parameter.RefTransform.localRotation;
				particlePtn.InitLocalScale = parameter.RefTransform.localScale;
				particlePtn.refTrans = parameter.RefTransform;
			}
			else
			{
				particlePtn.EndOffset = endOffset;
			}
			return particlePtn;
		}

		private Particle AppendParticles(Transform _transform, BoneParameter parameter, Vector3 endOffset, int parentIndex)
		{
			Particle particle = new()
			{
				Transform = _transform,
				IsRotationCalc = parameter.IsRotationCalc,
				Damping = parameter.Damping,
				Elasticity = parameter.Elasticity,
				Stiffness = parameter.Stiffness,
				Inert = parameter.Inert,
				ScaleNextBoneLength = parameter.NextBoneLength,
				Radius = parameter.CollisionRadius,
				IsMoveLimit = parameter.IsMoveLimit,
				MoveLimitMin = parameter.MoveLimitMin,
				MoveLimitMax = parameter.MoveLimitMax,
				KeepLengthLimitMin = parameter.KeepLengthLimitMin,
				KeepLengthLimitMax = parameter.KeepLengthLimitMax,
				IsCrush = parameter.IsCrush,
				CrushMoveAreaMin = parameter.CrushMoveAreaMin,
				CrushMoveAreaMax = parameter.CrushMoveAreaMax,
				CrushAddXYMin = parameter.CrushAddXYMin,
				CrushAddXYMax = parameter.CrushAddXYMax,
				ParentIndex = parentIndex
			};
			particle.Damping = Mathf.Clamp01(particle.Damping);
			particle.Elasticity = Mathf.Clamp01(particle.Elasticity);
			particle.Stiffness = Mathf.Clamp01(particle.Stiffness);
			particle.Inert = Mathf.Clamp01(particle.Inert);
			particle.ScaleNextBoneLength = Mathf.Max(particle.ScaleNextBoneLength, 0f);
			particle.Radius = Mathf.Max(particle.Radius, 0f);
			if (_transform)
			{
				particle.Position = particle.PrevPosition = _transform.position;
				particle.InitLocalPosition = _transform.localPosition;
				particle.InitLocalRotation = _transform.localRotation;
				particle.refTrans = _transform;
				if (parentIndex >= 0)
				{
					CalcLocalPosition(particle, Particles[parentIndex]);
				}
			}
			else
			{
				Transform transform = Particles[parentIndex].Transform;
				particle.EndOffset = endOffset;
				particle.Position = particle.PrevPosition = transform.TransformPoint(particle.EndOffset);
			}
			Particles.Add(particle);
			return particle;
		}

		private void InitTransforms()
		{
			int count = Particles.Count;
			for (int i = 0; i < count; i++)
			{
				Particle particle = Particles[i];
				if (particle.Transform)
				{
					if (particle.refTrans)
					{
						particle.Transform.SetLocalPositionAndRotation(particle.refTrans.localPosition, particle.refTrans.localRotation);
						particle.Transform.localScale = particle.refTrans.localScale;
					}
					else
					{
						particle.Transform.SetLocalPositionAndRotation(particle.InitLocalPosition, particle.InitLocalRotation);
						particle.Transform.localScale = particle.InitLocalScale;
					}
				}
			}
		}

		private void UpdateParticles1()
		{
			if (Patterns == null || (Patterns != null && Patterns.Count == 0))
			{
				return;
			}
			Vector3 b = (Gravity + Force) * ObjectScale;
			for (int i = 0; i < Particles.Count; i++)
			{
				Particle particle = Particles[i];
				if (particle.ParentIndex >= 0)
				{
					Vector3 a = (particle.Position - particle.PrevPosition) * ReflectSpeed;
					Vector3 b2 = ObjectMove * particle.Inert;
					particle.PrevPosition = particle.Position + b2;
					particle.Position += a * (1f - particle.Damping) + b + b2;
				}
				else
				{
					particle.PrevPosition = particle.Position;
					particle.Position = particle.Transform.position;
				}
			}
		}

		private void UpdateParticles2()
		{
			for (int i = 1; i < Particles.Count; i++)
			{
				Particle particle = Particles[i];
				Particle particle2 = Particles[particle.ParentIndex];
				float num;
				if (particle.Transform)
				{
					num = (particle2.Transform.position - particle.Transform.position).magnitude;
				}
				else
				{
					num = particle.EndOffset.magnitude * ObjectScale;
				}
				Matrix4x4 localToWorldMatrix = particle2.Transform.localToWorldMatrix;
				localToWorldMatrix.SetColumn(3, new Vector4(particle2.Position.x, particle2.Position.y, particle2.Position.z, 1f));
				Vector3 vector;
				if (particle.Transform)
				{
					vector = localToWorldMatrix.MultiplyPoint3x4(particle.LocalPosition);
				}
				else
				{
					vector = localToWorldMatrix.MultiplyPoint3x4(particle.EndOffset);
				}
				float num2 = Mathf.Lerp(1f, particle.Stiffness, Weight);
				if (num2 > 0f || particle.Elasticity > 0f)
				{
					Vector3 a = vector - particle.Position;
					particle.Position += a * particle.Elasticity;
					if (num2 > 0f)
					{
						a = vector - particle.Position;
						float magnitude = a.magnitude;
						float num3 = num * (1f - num2) * 2f;
						if (magnitude > num3)
						{
							particle.Position += a * ((magnitude - num3) / magnitude);
						}
					}
				}
				float particleRadius = particle.Radius * ObjectScale;
				foreach (var dynamicBoneCollider in Colliders)
				{
					if (dynamicBoneCollider && dynamicBoneCollider.enabled && dynamicBoneCollider.gameObject.activeInHierarchy)
					{
						dynamicBoneCollider.Collide(ref particle.Position, particleRadius);
					}
				}
				Vector3 a2 = particle2.Position - particle.Position;
				float magnitude2 = a2.magnitude;
				if (magnitude2 > 0f)
				{
					float num4 = (magnitude2 - num) / magnitude2;
					if (particle.KeepLengthLimitMin >= num4)
					{
						particle.Position += a2 * (num4 - particle.KeepLengthLimitMin);
					}
					else if (num4 >= particle.KeepLengthLimitMax)
					{
						particle.Position += a2 * (num4 - particle.KeepLengthLimitMax);
					}
				}
				if (particle.Transform && particle.IsMoveLimit)
				{
					Matrix4x4 localToWorldMatrix2 = particle.Transform.localToWorldMatrix;
					localToWorldMatrix2.SetColumn(3, new Vector4(vector.x, vector.y, vector.z, 1f));
					Vector3 vector2 = localToWorldMatrix2.inverse.MultiplyPoint3x4(particle.Position);
					vector2.x = Mathf.Clamp(vector2.x, particle.MoveLimitMin.x, particle.MoveLimitMax.x);
					vector2.y = Mathf.Clamp(vector2.y, particle.MoveLimitMin.y, particle.MoveLimitMax.y);
					vector2.z = Mathf.Clamp(vector2.z, particle.MoveLimitMin.z, particle.MoveLimitMax.z);
					particle.Position = localToWorldMatrix2.MultiplyPoint3x4(vector2);
				}
			}
		}

		private void SkipUpdateParticles()
		{
			for (int i = 0; i < Particles.Count; i++)
			{
				Particle particle = Particles[i];
				if (particle.ParentIndex >= 0)
				{
					Vector3 b = ObjectMove * particle.Inert;
					particle.PrevPosition += b;
					particle.Position += b;
					Particle particle2 = Particles[particle.ParentIndex];
					float num;
					if (particle.Transform)
					{
						num = (particle2.Transform.position - particle.Transform.position).magnitude;
					}
					else
					{
						num = particle.EndOffset.magnitude * ObjectScale;
					}
					Matrix4x4 localToWorldMatrix = particle2.Transform.localToWorldMatrix;
					localToWorldMatrix.SetColumn(3, new Vector4(particle2.Position.x, particle2.Position.y, particle2.Position.z, 1f));
					Vector3 vector;
					if (particle.Transform)
					{
						vector = localToWorldMatrix.MultiplyPoint3x4(particle.LocalPosition);
					}
					else
					{
						vector = localToWorldMatrix.MultiplyPoint3x4(particle.EndOffset);
					}
					float num2 = Mathf.Lerp(1f, particle.Stiffness, Weight);
					if (num2 > 0f)
					{
						Vector3 a = vector - particle.Position;
						float magnitude = a.magnitude;
						float num3 = num * (1f - num2) * 2f;
						if (magnitude > num3)
						{
							particle.Position += a * ((magnitude - num3) / magnitude);
						}
					}
					Vector3 a2 = particle2.Position - particle.Position;
					float magnitude2 = a2.magnitude;
					if (magnitude2 > 0f)
					{
						float num4 = (magnitude2 - num) / magnitude2;
						if (particle.KeepLengthLimitMin >= num4)
						{
							particle.Position += a2 * (num4 - particle.KeepLengthLimitMin);
						}
						else if (num4 >= particle.KeepLengthLimitMax)
						{
							particle.Position += a2 * (num4 - particle.KeepLengthLimitMax);
						}
					}
					if (particle.Transform && particle.IsMoveLimit)
					{
						Matrix4x4 localToWorldMatrix2 = particle.Transform.localToWorldMatrix;
						localToWorldMatrix2.SetColumn(3, new Vector4(vector.x, vector.y, vector.z, 1f));
						Vector3 vector2 = localToWorldMatrix2.inverse.MultiplyPoint3x4(particle.Position);
						vector2.x = Mathf.Clamp(vector2.x, particle.MoveLimitMin.x, particle.MoveLimitMax.x);
						vector2.y = Mathf.Clamp(vector2.y, particle.MoveLimitMin.y, particle.MoveLimitMax.y);
						vector2.z = Mathf.Clamp(vector2.z, particle.MoveLimitMin.z, particle.MoveLimitMax.z);
						particle.Position = localToWorldMatrix2.MultiplyPoint3x4(vector2);
					}
				}
				else
				{
					particle.PrevPosition = particle.Position;
					particle.Position = particle.Transform.position;
				}
			}
		}

		private void ApplyParticlesToTransforms()
		{
			for (int i = 1; i < Particles.Count; i++)
			{
				Particle particle = Particles[i];
				Particle particle2 = Particles[particle.ParentIndex];
				if (particle2.IsRotationCalc)
				{
					Vector3 direction;
					if (particle.Transform)
					{
						direction = particle.LocalPosition;
					}
					else
					{
						direction = particle.EndOffset;
					}
					Vector3 vector = particle2.Transform.TransformDirection(direction);
					Vector3 toDirection = particle.Position - particle2.Position;
					if (direction.magnitude != 0f)
					{
						toDirection = particle.Position + -1f * (1f - particle2.ScaleNextBoneLength) * vector - particle2.Position;
					}
					Quaternion lhs = Quaternion.FromToRotation(vector, toDirection);
					particle2.Transform.rotation = lhs * particle2.Transform.rotation;
				}
				if (particle.Transform)
				{
					Vector3 vector2 = particle.Transform.localToWorldMatrix.inverse.MultiplyPoint3x4(particle.Position);
					if (particle.IsCrush)
					{
						float num2;
						if (vector2.z <= 0f)
						{
							float num = Mathf.Clamp01(Mathf.InverseLerp(particle.CrushMoveAreaMin, 0f, vector2.z));
							num2 = particle.CrushAddXYMin * (1f - num);
						}
						else
						{
							float num3 = Mathf.Clamp01(Mathf.InverseLerp(0f, particle.CrushMoveAreaMax, vector2.z));
							num2 = particle.CrushAddXYMax * num3;
						}
						particle.Transform.localScale = particle.InitLocalScale + new Vector3(num2, num2, 0f);
					}
					particle.Transform.position = particle.Position;
				}
			}
		}

		private static void CalcLocalPosition(Particle particle, Particle parent)
		{
			particle.LocalPosition = parent.Transform.InverseTransformPoint(particle.Position);
		}

		private static Vector3 CalcLocalPosition(Vector3 particle, Transform parent)
		{
			return parent.InverseTransformPoint(particle);
		}

		private bool IsRefTransform()
		{
			if (Patterns == null)
			{
				return false;
			}
			foreach (BonePtn bonePtn in Patterns)
			{
				if (bonePtn.Params == null)
				{
					return false;
				}
				foreach (var param in bonePtn.Params)
				{
					if (param.RefTransform == null)
					{
						return false;
					}
				}
			}
			return true;
		}

		private static Transform FindLoop(Transform transform, string name)
		{
			if (string.Compare(name, transform.name) == 0)
			{
				return transform;
			}
			foreach (object obj in transform)
			{
				Transform transform2 = (Transform)obj;
				Transform transform3 = FindLoop(transform2, name);
				if (null != transform3)
				{
					return transform3;
				}
			}
			return null;
		}

		private bool LoadText(LoadInfo _info, List<string> _list, ref int _index)
		{
			string[] array = _list[_index].Split(new[]
			{
			'\t'
			});
			int num = array.Length;
			if (num == 0)
			{
				return false;
			}
			if (array[0][..2].Equals("//"))
			{
				_index++;
				return true;
			}
			string a = array[0];
			if (!(a == "#Comment"))
			{
				if (!(a == "#ReflectSpeed"))
				{
					if (!(a == "#HeavyLoopMaxCount"))
					{
						if (!(a == "#Colliders name"))
						{
							if (!(a == "#Bone name"))
							{
								if (!(a == "#PtnClassMember"))
								{
									return false;
								}
								BonePtn bonePtn = new();
								if (!LoadPtnClassMember(bonePtn, array, _index))
								{
									return false;
								}
								_index++;
								if (!LoadParamClassMember(bonePtn, _list, ref _index))
								{
									return false;
								}
								_info.Patterns.Add(bonePtn);
								return true;
							}
							else
							{
								for (int i = 1; i < num; i++)
								{
									if (array[i] == "" || array[i] == " ")
									{
										break;
									}
									Transform transform = FindLoop(base.transform, array[i]);
									if (transform == null)
									{
										return false;
									}
									_info.Bones.Add(transform);
								}
							}
						}
						else
						{
							for (int j = 1; j < num; j++)
							{
								if (array[j] == "" || array[j] == " ")
								{
									break;
								}
								Transform transform2 = FindLoop(transform, array[j]);
								if (transform2 == null)
								{
									return false;
								}
								if (!transform2.TryGetComponent<DynamicBoneCollider>(out var component))
								{
									return false;
								}
								_info.Colliders.Add(component);
							}
						}
					}
					else
					{
						if (!int.TryParse(array[1], out int heavyLoopMaxCount))
						{
							return false;
						}
						_info.HeavyLoopMaxCount = heavyLoopMaxCount;
					}
				}
				else
				{
					if (!float.TryParse(array[1], out float reflectSpeed))
					{
						return false;
					}
					_info.ReflectSpeed = reflectSpeed;
				}
			}
			else
			{
				_info.Comment = array[1];
			}
			_index++;
			return true;
		}

		private bool LoadPtnClassMember(BonePtn ptn, string[] str, int index)
		{
			int length = str.Length;
			int num = 0;
			if (!CheckLength(length, ref num, index, "[PtnClassMember] 表示する名前", ""))
			{
				return false;
			}
			ptn.Name = str[num];
			if (!GetMemberFloat(length, str, ref num, index, out float num2, "[PtnClassMember] 重力X", ""))
			{
				return false;
			}
			Vector3 vector = ptn.Gravity;
			vector.x = num2;
			ptn.Gravity = vector;
			if (!GetMemberFloat(length, str, ref num, index, out num2, "[PtnClassMember] 重力Y", ""))
			{
				return false;
			}
			vector = ptn.Gravity;
			vector.y = num2;
			ptn.Gravity = vector;
			if (!GetMemberFloat(length, str, ref num, index, out num2, "[PtnClassMember] 重力Z", ""))
			{
				return false;
			}
			vector = ptn.Gravity;
			vector.z = num2;
			ptn.Gravity = vector;
			if (!GetMemberFloat(length, str, ref num, index, out num2, "[PtnClassMember] EndOffsetX", ""))
			{
				return false;
			}
			vector = ptn.EndOffset;
			vector.x = num2;
			ptn.EndOffset = vector;
			if (!GetMemberFloat(length, str, ref num, index, out num2, "[PtnClassMember] EndOffsetY", ""))
			{
				return false;
			}
			vector = ptn.EndOffset;
			vector.y = num2;
			ptn.EndOffset = vector;
			if (!GetMemberFloat(length, str, ref num, index, out num2, "[PtnClassMember] EndOffsetZ", ""))
			{
				return false;
			}
			vector = ptn.EndOffset;
			vector.z = num2;
			ptn.EndOffset = vector;
			if (!GetMemberFloat(length, str, ref num, index, out num2, "[PtnClassMember] EndOffsetの空気抵抗", ""))
			{
				return false;
			}
			ptn.EndOffsetDamping = num2;
			if (!GetMemberFloat(length, str, ref num, index, out num2, "[PtnClassMember] EndOffsetの弾力", ""))
			{
				return false;
			}
			ptn.EndOffsetElasticity = num2;
			if (!GetMemberFloat(length, str, ref num, index, out num2, "[PtnClassMember] EndOffsetの硬さ", ""))
			{
				return false;
			}
			ptn.EndOffsetStiffness = num2;
			if (!GetMemberFloat(length, str, ref num, index, out num2, "[PtnClassMember] EndOffsetの惰性", ""))
			{
				return false;
			}
			ptn.EndOffsetInert = num2;
			return true;
		}

		private bool LoadParamClassMember(BonePtn ptn, List<string> list, ref int index)
		{
			while (list.Count > index)
			{
				string[] array = list[index].Split(new[]
				{
				'\t'
				});
				int num = array.Length;
				int num2 = 0;
				if (num <= num2)
				{
					return false;
				}
				if (array[num2][..2].Equals("//"))
				{
					index++;
				}
				else
				{
					if (array[num2] != "#ParamClassMember")
					{
						break;
					}
					BoneParameter boneParameter = new();
					if (!CheckLength(num, ref num2, index, "[ParamClassMember] 表示する名前", ""))
					{
						return false;
					}
					boneParameter.Name = array[num2];
					if (!CheckLength(num, ref num2, index, "[ParamClassMember] 参照するフレーム", ""))
					{
						return false;
					}
					Transform transform = FindLoop(base.transform, array[num2]);
					if (transform == null)
					{
						return false;
					}
					boneParameter.RefTransform = transform;
					if (!GetMemberBool(num, array, ref num2, index, out bool flag, "[ParamClassMember] 回転するか ", ""))
					{
						return false;
					}
					boneParameter.IsRotationCalc = flag;
					if (!GetMemberFloat(num, array, ref num2, index, out float num3, "[ParamClassMember] 空気抵抗", ""))
					{
						return false;
					}
					boneParameter.Damping = num3;
					if (!GetMemberFloat(num, array, ref num2, index, out num3, "[ParamClassMember] 弾力", ""))
					{
						return false;
					}
					boneParameter.Elasticity = num3;
					if (!GetMemberFloat(num, array, ref num2, index, out num3, "[ParamClassMember] 硬さ", ""))
					{
						return false;
					}
					boneParameter.Stiffness = num3;
					if (!GetMemberFloat(num, array, ref num2, index, out num3, "[ParamClassMember] 惰性", ""))
					{
						return false;
					}
					boneParameter.Inert = num3;
					if (!GetMemberFloat(num, array, ref num2, index, out num3, "[ParamClassMember] 次の骨までの距離補正", ""))
					{
						return false;
					}
					boneParameter.NextBoneLength = num3;
					if (!GetMemberFloat(num, array, ref num2, index, out num3, "[ParamClassMember] 当たり判定の半径", ""))
					{
						return false;
					}
					boneParameter.CollisionRadius = num3;
					if (!GetMemberBool(num, array, ref num2, index, out flag, "[ParamClassMember] 移動制限するか ", ""))
					{
						return false;
					}
					boneParameter.IsMoveLimit = flag;
					if (!GetMemberFloat(num, array, ref num2, index, out num3, "[ParamClassMember] 移動制限最小X", ""))
					{
						return false;
					}
					Vector3 vector = boneParameter.MoveLimitMin;
					vector.x = num3;
					boneParameter.MoveLimitMin = vector;
					if (!GetMemberFloat(num, array, ref num2, index, out num3, "[ParamClassMember] 移動制限最小Y", ""))
					{
						return false;
					}
					vector = boneParameter.MoveLimitMin;
					vector.y = num3;
					boneParameter.MoveLimitMin = vector;
					if (!GetMemberFloat(num, array, ref num2, index, out num3, "[ParamClassMember] 移動制限最小Z", ""))
					{
						return false;
					}
					vector = boneParameter.MoveLimitMin;
					vector.z = num3;
					boneParameter.MoveLimitMin = vector;
					if (!GetMemberFloat(num, array, ref num2, index, out num3, "[ParamClassMember] 移動制限最大X", ""))
					{
						return false;
					}
					vector = boneParameter.MoveLimitMax;
					vector.x = num3;
					boneParameter.MoveLimitMax = vector;
					if (!GetMemberFloat(num, array, ref num2, index, out num3, "[ParamClassMember] 移動制限最大Y", ""))
					{
						return false;
					}
					vector = boneParameter.MoveLimitMax;
					vector.y = num3;
					boneParameter.MoveLimitMax = vector;
					if (!GetMemberFloat(num, array, ref num2, index, out num3, "[ParamClassMember] 移動制限最大Z", ""))
					{
						return false;
					}
					vector = boneParameter.MoveLimitMax;
					vector.z = num3;
					boneParameter.MoveLimitMax = vector;
					if (!GetMemberFloat(num, array, ref num2, index, out num3, "[ParamClassMember] 親からの長さの補正しない範囲最小", ""))
					{
						return false;
					}
					boneParameter.KeepLengthLimitMin = num3;
					if (!GetMemberFloat(num, array, ref num2, index, out num3, "[ParamClassMember] 親からの長さの補正しない範囲最大", ""))
					{
						return false;
					}
					boneParameter.KeepLengthLimitMax = num3;
					if (!GetMemberBool(num, array, ref num2, index, out flag, "[ParamClassMember] 潰すか ", ""))
					{
						return false;
					}
					boneParameter.IsCrush = flag;
					if (!GetMemberFloat(num, array, ref num2, index, out num3, "[ParamClassMember] 潰す移動判断範囲最小", ""))
					{
						return false;
					}
					boneParameter.CrushMoveAreaMin = num3;
					if (!GetMemberFloat(num, array, ref num2, index, out num3, "[ParamClassMember] 潰す移動判断範囲最大", ""))
					{
						return false;
					}
					boneParameter.CrushMoveAreaMax = num3;
					if (!GetMemberFloat(num, array, ref num2, index, out num3, "[ParamClassMember] 潰れた時に加算するXYスケール", ""))
					{
						return false;
					}
					boneParameter.CrushAddXYMin = num3;
					if (!GetMemberFloat(num, array, ref num2, index, out num3, "[ParamClassMember] 伸びた時に加算するXYスケール", ""))
					{
						return false;
					}
					boneParameter.CrushAddXYMax = num3;
					ptn.Params.Add(boneParameter);
					index++;
				}
			}
			return true;
		}

		private bool CheckLength(int _length, ref int _index, int _line, string _warning = "", string _warning1 = "")
		{
			int num = _index + 1;
			_index = num;
			return _length > num;
		}

		private bool GetMemberFloat(int _length, string[] _str, ref int _index, int _line, out float _param, string _warning = "", string _warning1 = "")
		{
			_param = 0f;
			return CheckLength(_length, ref _index, _line, _warning, "") && float.TryParse(_str[_index], out _param);
		}

		private bool GetMemberInt(int _length, string[] _str, ref int _index, int _line, out int _param, string _warning = "", string _warning1 = "")
		{
			_param = 0;
			return CheckLength(_length, ref _index, _line, _warning, "") && int.TryParse(_str[_index], out _param);
		}

		private bool GetMemberBool(int _length, string[] _str, ref int _index, int _line, out bool _param, string _warning = "", string _warning1 = "")
		{
			_param = false;
			if (!CheckLength(_length, ref _index, _line, _warning, ""))
			{
				return false;
			}
			if (_str[_index] == "false" || _str[_index] == "FALSE" || _str[_index] == "False")
			{
				_param = false;
				return true;
			}
			if (_str[_index] == "true" || _str[_index] == "TRUE" || _str[_index] == "True")
			{
				_param = true;
				return true;
			}
			return false;
		}

#if ODIN_INSPECTOR
		[Button("Load Bone Data")]
#endif
		private void LoadBoneData()
		{
			string path = $"Assets/{Comment}.txt";
			LoadTextList(File.ReadAllLines(path).ToList());
			Debug.Log($"[DynamicBone] Load bone data from {path}");
		}

#if ODIN_INSPECTOR
		[Button("Save Bone Data")]
#endif
		private void SaveBoneData()
		{
			string path = $"Assets/{Comment}.txt";
			using var writer = new StreamWriter(path);
			SaveText(writer);
			Debug.Log($"[DynamicBone] Save bone data to {path}");
		}

		private void SaveText(StreamWriter sw)
		{
			sw.Write("//コメント\n");
			sw.Write("#Comment\t" + Comment + "\n");
			sw.Write("//粒子のスピード\n");
			sw.Write("#ReflectSpeed\t" + ReflectSpeed + "\n");
			sw.Write("//重い時に何回まで回すか\u3000回数多いと正確になるけど更に重くなるよ\n");
			sw.Write("#HeavyLoopMaxCount\t" + HeavyLoopMaxCount + "\n");
			sw.Write("//登録する当たり判定の名前\n");
			sw.Write("#Colliders name\t");
			foreach (var dynamicBoneCollider in Colliders)
			{
				sw.Write(dynamicBoneCollider.gameObject.name + "\t");
			}
			sw.Write("\n");
			sw.Write("//登録する骨の名前\n");
			sw.Write("#Bone name\t");
			foreach (Transform transform in Bones)
			{
				sw.Write(transform.name + "\t");
			}
			sw.Write("\n");
			foreach (BonePtn bonePtn in Patterns)
			{
				sw.Write("//パターンの設定\n");
				sw.Write("//PtnClass\t");
				sw.Write("表示する名前\t");
				sw.Write("重力 X\t");
				sw.Write("重力 Y\t");
				sw.Write("重力 Z\t");
				sw.Write("EndOffset x\t");
				sw.Write("EndOffset y\t");
				sw.Write("EndOffset z\t");
				sw.Write("EndOffsetの空気抵抗\t");
				sw.Write("EndOffsetの弾力\t");
				sw.Write("EndOffsetの硬さ\t");
				sw.Write("EndOffsetの惰性\t");
				sw.Write("\n");
				sw.Write("#PtnClassMember\t");
				sw.Write(bonePtn.Name + "\t");
				sw.Write(bonePtn.Gravity.x + "\t");
				sw.Write(bonePtn.Gravity.y + "\t");
				sw.Write(bonePtn.Gravity.z + "\t");
				sw.Write(bonePtn.EndOffset.x + "\t");
				sw.Write(bonePtn.EndOffset.y + "\t");
				sw.Write(bonePtn.EndOffset.z + "\t");
				sw.Write(bonePtn.EndOffsetDamping + "\t");
				sw.Write(bonePtn.EndOffsetElasticity + "\t");
				sw.Write(bonePtn.EndOffsetStiffness + "\t");
				sw.Write(bonePtn.EndOffsetInert + "\t");
				sw.Write("\n");
				sw.Write("//そのパターンの骨に対するパラメーター\n");
				sw.Write("//ParamClass\t");
				sw.Write("表示する名前\t");
				sw.Write("参照するフレーム名\t");
				sw.Write("回転する？\t");
				sw.Write("空気抵抗\t");
				sw.Write("弾力\t");
				sw.Write("硬さ\t");
				sw.Write("惰性\t");
				sw.Write("次の骨までの距離補正\t");
				sw.Write("当たり判定の半径\t");
				sw.Write("移動制限する？\t");
				sw.Write("移動制限最小X\t");
				sw.Write("移動制限最小Y\t");
				sw.Write("移動制限最小Z\t");
				sw.Write("移動制限最大X\t");
				sw.Write("移動制限最大Y\t");
				sw.Write("移動制限最大Z\t");
				sw.Write("親からの長さを補正しない範囲最小値\t");
				sw.Write("親からの長さを補正しない範囲最大値\t");
				sw.Write("潰す？\t");
				sw.Write("潰す移動判断範囲最小\t");
				sw.Write("潰す移動判断範囲最大\t");
				sw.Write("潰れた時に加算するXYスケール\t");
				sw.Write("伸びた時に加算するXYスケール\t");
				sw.Write("\n");
				foreach (BoneParameter boneParameter in bonePtn.Params)
				{
					sw.Write("#ParamClassMember\t");
					sw.Write(boneParameter.Name + "\t");
					string str = "";
					if (boneParameter.RefTransform != null)
					{
						str = boneParameter.RefTransform.name;
					}
					sw.Write(str + "\t");
					sw.Write(boneParameter.IsRotationCalc + "\t");
					sw.Write(boneParameter.Damping + "\t");
					sw.Write(boneParameter.Elasticity + "\t");
					sw.Write(boneParameter.Stiffness + "\t");
					sw.Write(boneParameter.Inert + "\t");
					sw.Write(boneParameter.NextBoneLength + "\t");
					sw.Write(boneParameter.CollisionRadius + "\t");
					sw.Write(boneParameter.IsMoveLimit + "\t");
					sw.Write(boneParameter.MoveLimitMin.x + "\t");
					sw.Write(boneParameter.MoveLimitMin.y + "\t");
					sw.Write(boneParameter.MoveLimitMin.z + "\t");
					sw.Write(boneParameter.MoveLimitMax.x + "\t");
					sw.Write(boneParameter.MoveLimitMax.y + "\t");
					sw.Write(boneParameter.MoveLimitMax.z + "\t");
					sw.Write(boneParameter.KeepLengthLimitMin + "\t");
					sw.Write(boneParameter.KeepLengthLimitMax + "\t");
					sw.Write(boneParameter.IsCrush + "\t");
					sw.Write(boneParameter.CrushMoveAreaMin + "\t");
					sw.Write(boneParameter.CrushMoveAreaMin + "\t");
					sw.Write(boneParameter.CrushAddXYMin + "\t");
					sw.Write(boneParameter.CrushAddXYMax + "\t");
					sw.Write("\n");
				}
			}
		}

#if UNITY_EDITOR && ODIN_INSPECTOR
		[ShowInInspector, DisableInPlayMode, BoxGroup("Editor Only")]
		private Transform CopySource { get; set; }

		[Button("Copy From"), DisableInPlayMode, BoxGroup("Editor Only")]
		private void CopyFrom()
		{
			if (!CopySource) return;
			var bones = CopySource.GetComponentsInChildren<Transform>();
			var replaceDict = new Dictionary<string, Transform>();
			foreach (var bone in bones)
			{
				replaceDict[bone.name] = bone;
			}
			for (int i = 0; i < Bones.Count; i++)
			{
				if (replaceDict.ContainsKey(Bones[i].name)) Bones[i] = replaceDict[Bones[i].name];
			}
			for (int i = 0; i < Colliders.Count; i++)
			{
				if (replaceDict.ContainsKey(Colliders[i].transform.name))
				{
					var remoteBone = replaceDict[Colliders[i].transform.name];
					if (!remoteBone.gameObject.TryGetComponent(out DynamicBoneCollider dynamicBoneCollider))
					{
						dynamicBoneCollider = remoteBone.gameObject.AddComponent<DynamicBoneCollider>();
					}
					JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(Colliders[i]), dynamicBoneCollider);
					Colliders[i] = dynamicBoneCollider;
				}
			}
			foreach (var pattern in Patterns)
			{
				for (int i = 0; i < pattern.Params.Count; i++)
				{
					if (replaceDict.ContainsKey(pattern.Params[i].RefTransform.name)) pattern.Params[i].RefTransform = replaceDict[pattern.Params[i].RefTransform.name];
				}
			}
		}
#endif

		public string Comment = "";

		public Transform Root;

		public float UpdateRate = 60f;

		[Range(0f, 100f)]
		[Tooltip("速度UP")]
		public float ReflectSpeed = 1f;

		[Range(0f, 10f)]
		[Tooltip("重い時に何回まで回す？正確になるけどその分重くなる")]
		public int HeavyLoopMaxCount = 3;

		public Vector3 Gravity = Vector3.zero;

		public Vector3 Force = Vector3.zero;

		public List<DynamicBoneColliderBase> Colliders;

		public List<Transform> Bones;

		public List<BonePtn> Patterns;

		private Vector3 ObjectMove = Vector3.zero;

		private Vector3 ObjectPrevPosition = Vector3.zero;

		private float ObjectScale = 1f;

		private float UpdateTime;

		private float Weight = 1f;

#if ODIN_INSPECTOR
		[ShowInInspector, ReadOnly]
#endif
		private readonly List<Particle> Particles = new();

		public int PtnNo;

		[Serializable]
		public class BoneParameter
		{
			public BoneParameter()
			{
				Name = "";
				IsRotationCalc = false;
				Damping = 0f;
				Elasticity = 0f;
				Stiffness = 0f;
				Inert = 0f;
				NextBoneLength = 1f;
				CollisionRadius = 0f;
				IsMoveLimit = false;
				MoveLimitMin = Vector3.zero;
				MoveLimitMax = Vector3.zero;
				KeepLengthLimitMin = 0f;
				KeepLengthLimitMax = 0f;
				IsCrush = false;
				CrushMoveAreaMin = 0f;
				CrushMoveAreaMax = 0f;
				CrushAddXYMin = 0f;
				CrushAddXYMax = 0f;
			}

			public string Name = "";

			[Tooltip("参照骨")]
			public Transform RefTransform;

			[Tooltip("回転させる？")]
			public bool IsRotationCalc;

			[Range(0f, 1f)]
			[Tooltip("空気抵抗")]
			public float Damping;

			[Range(0f, 1f)]
			[Tooltip("弾力(元の位置に戻ろうとする力)")]
			public float Elasticity;

			[Range(0f, 1f)]
			[Tooltip("硬さ(要は移動のリミット：移動制限)")]
			public float Stiffness;

			[Range(0f, 1f)]
			[Tooltip("惰性(ルートが動いた分を加算するか 加算すると親子付されてる感じになる？)")]
			public float Inert;

			[Range(0f, 100f)]
			[Tooltip("次の骨までの長さの制御(回転に影響する：短いと回りやすい(角度が出やすい)\u3000長いと回りにくい(角度が出にくい))")]
			public float NextBoneLength = 1f;

			[Tooltip("コリジョンの大きさ")]
			public float CollisionRadius;

			[Tooltip("移動制限")]
			public bool IsMoveLimit;

			[Tooltip("ローカル移動制限最小")]
			public Vector3 MoveLimitMin = Vector3.zero;

			[Tooltip("ローカル移動制限最大")]
			public Vector3 MoveLimitMax = Vector3.zero;

			[Tooltip("骨の長さを留める制限最小")]
			public float KeepLengthLimitMin;

			[Tooltip("骨の長さを留める制限最大")]
			public float KeepLengthLimitMax;

			[Tooltip("潰れ制御")]
			public bool IsCrush;

			[Tooltip("潰れ範囲最小 この間で設定されたスケール値を足す 判定はローカル位置のZ値")]
			public float CrushMoveAreaMin;

			[Tooltip("潰れ範囲最大 この間で設定されたスケール値を足す 判定はローカル位置のZ値")]
			public float CrushMoveAreaMax;

			[Tooltip("潰れた時に加算するXYスケール")]
			public float CrushAddXYMin;

			[Tooltip("伸びた時に加算するXYスケール")]
			public float CrushAddXYMax;
		}

		public class ParticlePtn
		{
			public ParticlePtn()
			{
				Damping = 0f;
				Elasticity = 0f;
				Stiffness = 0f;
				Inert = 0f;
				Radius = 0f;
				IsRotationCalc = true;
				ScaleNextBoneLength = 1f;
				IsMoveLimit = false;
				MoveLimitMin = Vector3.zero;
				MoveLimitMax = Vector3.zero;
				KeepLengthLimitMin = 0f;
				KeepLengthLimitMax = 0f;
				IsCrush = false;
				CrushMoveAreaMin = 0f;
				CrushMoveAreaMax = 0f;
				CrushAddXYMin = 0f;
				CrushAddXYMax = 0f;
				EndOffset = Vector3.zero;
				InitLocalPosition = Vector3.zero;
				InitLocalRotation = Quaternion.identity;
				InitLocalScale = Vector3.one;
				refTrans = null;
				LocalPosition = Vector3.zero;
			}

			public float Damping;

			public float Elasticity;

			public float Stiffness;

			public float Inert;

			public float Radius;

			public bool IsRotationCalc = true;

			public float ScaleNextBoneLength = 1f;

			public bool IsMoveLimit;

			public Vector3 MoveLimitMin = Vector3.zero;

			public Vector3 MoveLimitMax = Vector3.zero;

			public float KeepLengthLimitMin;

			public float KeepLengthLimitMax;

			public bool IsCrush;

			public float CrushMoveAreaMin;

			public float CrushMoveAreaMax;

			public float CrushAddXYMin;

			public float CrushAddXYMax;

			public Vector3 EndOffset = Vector3.zero;

			public Vector3 InitLocalPosition = Vector3.zero;

			public Quaternion InitLocalRotation = Quaternion.identity;

			public Vector3 InitLocalScale = Vector3.one;

			public Transform refTrans;

			public Vector3 LocalPosition = Vector3.zero;
		}

		[Serializable]
		public class BonePtn
		{
			public string Name = "";

			[Tooltip("重力")]
			public Vector3 Gravity = Vector3.zero;

			[Tooltip("最後の骨を回すために必要")]
			public Vector3 EndOffset = Vector3.zero;

			[Range(0f, 1f)]
			[Tooltip("空気抵抗")]
			public float EndOffsetDamping;

			[Range(0f, 1f)]
			[Tooltip("弾力(元の位置に戻ろうとする力)")]
			public float EndOffsetElasticity;

			[Range(0f, 1f)]
			[Tooltip("硬さ(要は移動のリミット：移動制限)")]
			public float EndOffsetStiffness;

			[Range(0f, 1f)]
			[Tooltip("惰性(ルートが動いた分を加算するか 加算すると親子付されてる感じになる？)")]
			public float EndOffsetInert;

			public List<BoneParameter> Params = new();

#if ODIN_INSPECTOR
			[ShowInInspector, ReadOnly]
#endif
			public List<ParticlePtn> ParticlePtns = new();
		}

		public class Particle
		{
			public Transform Transform;

			public int ParentIndex = -1;

			public float Damping;

			public float Elasticity;

			public float Stiffness;

			public float Inert;

			public float Radius;

			public bool IsRotationCalc = true;

			public float ScaleNextBoneLength = 1f;

			public bool IsMoveLimit;

			public Vector3 MoveLimitMin = Vector3.zero;

			public Vector3 MoveLimitMax = Vector3.zero;

			public float KeepLengthLimitMin;

			public float KeepLengthLimitMax;

			public bool IsCrush;

			public float CrushMoveAreaMin;

			public float CrushMoveAreaMax;

			public float CrushAddXYMin;

			public float CrushAddXYMax;

			public Vector3 Position = Vector3.zero;

			public Vector3 PrevPosition = Vector3.zero;

			public Vector3 EndOffset = Vector3.zero;

			public Vector3 InitLocalPosition = Vector3.zero;

			public Quaternion InitLocalRotation = Quaternion.identity;

			public Vector3 InitLocalScale = Vector3.one;

			public Transform refTrans;

			public Vector3 LocalPosition = Vector3.zero;
		}

		public class LoadInfo
		{
			public string Comment;

			public float ReflectSpeed;

			public int HeavyLoopMaxCount;

			public List<DynamicBoneColliderBase> Colliders = new();

			public List<Transform> Bones = new();

			public List<BonePtn> Patterns = new();
		}

		private class TransformParam
		{
			public Vector3 pos;

			public Quaternion rot;

			public Vector3 scale;
		}
	}
}
