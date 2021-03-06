﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using CompoundParts;

namespace KerbalJointReinforcement
{
	[KSPAddon(KSPAddon.Startup.Flight, false)]
	public class KJRManager : MonoBehaviour
	{
		private static KJRManager _instance;

#if Compatible
		internal static System.Type ModuleDecoupleType;
		internal static System.Reflection.FieldInfo isDecoupledField;

		internal static System.Reflection.FieldInfo onRoboticPartLockChangingField = null;
#endif

		internal static KJRManager Instance
		{
			get { return _instance; }
		}

		List<Vessel> updatedVessels;
		HashSet<Vessel> easingVessels;
		KJRMultiJointManager multiJointManager;
		List<Vessel> updatingVessels;

		internal KJRMultiJointManager GetMultiJointManager()
		{
			return multiJointManager;
		}

		public void Awake()
		{
			KJRJointUtils.LoadConstants();
			updatedVessels = new List<Vessel>();
			easingVessels = new HashSet<Vessel>();
			multiJointManager = new KJRMultiJointManager();
			updatingVessels = new List<Vessel>();

#if Compatible
			ModuleDecoupleType = Type.GetType("ModuleDecouple, Assembly-CSharp");
			isDecoupledField = ModuleDecoupleType.GetField("isDecoupled");

			// >= 1.7.1
			KJRJointUtils.IRoboticServoType = Type.GetType("Expansions.Serenity.IRoboticServo, Assembly-CSharp"); // >= 1.7.1

			// 1.7.1 detection
			onRoboticPartLockChangingField = typeof(GameEvents).GetField("onRoboticPartLockChanging"); // >= 1.7.2
			if(onRoboticPartLockChangingField == null)
				KJRJointUtils.BaseServoType = Type.GetType("Expansions.Serenity.BaseServo, Assembly-CSharp"); // 1.7.1 only
#endif

			_instance = this;
		}

		public void Start()
		{
			GameEvents.onVesselCreate.Add(OnVesselCreate);
			GameEvents.onVesselWasModified.Add(OnVesselWasModified);
			GameEvents.onVesselDestroy.Add(OnVesselDestroy); // FEHLER, evtl. onAboutToDestroy nutzen??

			GameEvents.onVesselGoOffRails.Add(OnVesselOffRails);
			GameEvents.onVesselGoOnRails.Add(OnVesselOnRails);

			GameEvents.onPartDestroyed.Add(RemovePartJoints);
			GameEvents.onPartDie.Add(RemovePartJoints);
			GameEvents.onPartDeCouple.Add(RemovePartJoints);

			GameEvents.onPhysicsEaseStart.Add(OnEaseStart);
			GameEvents.onPhysicsEaseStop.Add(OnEaseStop);

#if Compatible
			// >= 1.7.2
			if(onRoboticPartLockChangingField != null)
			{
				EventData<Part, bool> onRoboticPartLockChanging = (EventData<Part, bool>)onRoboticPartLockChangingField.GetValue(null);
				onRoboticPartLockChanging.Add(OnRoboticPartLockChanging);
			}
#endif

#if !Compatible
			GameEvents.onRoboticPartLockChanging.Add(OnRoboticPartLockChanging);
#endif

// FHELER, der decoupler hat noch das Pack behandelt... müsste man das auch noch? ist das nicht immer dann, wenn man onrails geht??? -> doch, wird genau nur dann gesendet
		}

		public void OnDestroy()
		{
			GameEvents.onVesselCreate.Remove(OnVesselCreate);
			GameEvents.onVesselWasModified.Remove(OnVesselWasModified);
			GameEvents.onVesselDestroy.Remove(OnVesselDestroy);

			GameEvents.onVesselGoOffRails.Remove(OnVesselOffRails);
			GameEvents.onVesselGoOnRails.Remove(OnVesselOnRails);

			GameEvents.onPartDestroyed.Remove(RemovePartJoints);
			GameEvents.onPartDie.Remove(RemovePartJoints);
			GameEvents.onPartDeCouple.Remove(RemovePartJoints);

			GameEvents.onPhysicsEaseStart.Remove(OnEaseStart);
			GameEvents.onPhysicsEaseStop.Remove(OnEaseStop);

#if Compatible
			// >= 1.7.2
			if(onRoboticPartLockChangingField != null)
			{
				EventData<Part, bool> onRoboticPartLockChanging = (EventData<Part, bool>)onRoboticPartLockChangingField.GetValue(null);
				onRoboticPartLockChanging.Remove(OnRoboticPartLockChanging);
			}
#endif

#if !Compatible
			GameEvents.onRoboticPartLockChanging.Remove(OnRoboticPartLockChanging);
#endif

			updatedVessels = null;
			easingVessels = null;

			multiJointManager = null;
		}

		IEnumerator RunVesselJointUpdateFunctionDelayed(Vessel v)
		{
			yield return new WaitForFixedUpdate();

			updatingVessels.Remove(v);

			RunVesselJointUpdateFunction(v);

#if IncludeAnalyzer
			KJRAnalyzerJoint.RunVesselJointUpdateFunction(v);

			KJRAnalyzer.WasModified(v);
#endif
		}

		private void OnVesselCreate(Vessel v)
		{
			multiJointManager.RemoveAllVesselJoints(v);

			updatedVessels.Remove(v);

#if IncludeAnalyzer
			KJRAnalyzer.WasModified(v);
#endif
		}

		internal void OnVesselWasModified(Vessel v)
		{
			if((object)v == null || v.isEVA || v.GetComponent<KerbalEVA>())
				return; 

			multiJointManager.RemoveAllVesselJoints(v);
			updatedVessels.Remove(v);

			if(KJRJointUtils.debug)
			{
				StringBuilder debugString = new StringBuilder();
				debugString.AppendLine("KJR: Modified vessel " + v.id + " (" + v.GetName() + ")");
				debugString.AppendLine(System.Environment.StackTrace);
				debugString.AppendLine("Now contains: ");
				foreach(Part p in v.Parts)
					debugString.AppendLine("  " + p.partInfo.name + " (" + p.flightID + ")");
				Debug.Log(debugString);
			}

			if(!updatingVessels.Contains(v))
			{
				updatingVessels.Add(v);
				StartCoroutine(RunVesselJointUpdateFunctionDelayed(v));
			}
		}

		private void OnVesselDestroy(Vessel v)
		{
			easingVessels.Remove(v);

			updatedVessels.Remove(v);

#if IncludeAnalyzer
			KJRAnalyzer.Clear(v);
#endif
		}

		private void OnRoboticPartLockChanging(Part p, bool b)
		{
			OnVesselWasModified(p.vessel);
		}

		// this function can be called by compatible modules instead of calling
		// Vessel.CycleAllAutoStrut, if you want only KJR to cycle the extra joints
		public static void CycleAllAutoStrut(Vessel v)
		{
			_instance.OnVesselWasModified(v);
		}

		private void OnVesselOnRails(Vessel v)
		{
			if((object)v == null)
				return;

			if(updatedVessels.Contains(v))
			{
				multiJointManager.RemoveAllVesselJoints(v);

				updatedVessels.Remove(v);
			}
		}

		private void OnVesselOffRails(Vessel v)
		{
			if((object)v == null || v.isEVA || v.GetComponent<KerbalEVA>())
				return; 

			if(!updatingVessels.Contains(v))
			{
				updatingVessels.Add(v);
				StartCoroutine(RunVesselJointUpdateFunctionDelayed(v));
			}
		}

		private void RemovePartJoints(Part p)
		{
			multiJointManager.RemovePartJoints(p);
		}

		public void OnEaseStart(Vessel v)
		{
			if(KJRJointUtils.debug)
				Debug.Log("KJR easing " + v.vesselName);

			foreach(Part p in v.Parts)
			{
				if(KJRJointUtils.IsJointUnlockable(p))
					continue; // exclude those actions from joints that can be dynamically unlocked

				p.crashTolerance = p.crashTolerance * 10000f;
				if(p.attachJoint)
					p.attachJoint.SetUnbreakable(true, false);

				Joint[] partJoints = p.GetComponents<Joint>();

				if(p.Modules.Contains<LaunchClamp>())
				{
					for(int j = 0; j < partJoints.Length; j++)
						if(partJoints[j].connectedBody == null)
						{
							GameObject.Destroy(partJoints[j]);
							KJRJointUtils.ConnectLaunchClampToGround(p);
							break;
						}
				}
			}

			easingVessels.Add(v);
		}

		public void OnEaseStop(Vessel v)
		{
			if(!easingVessels.Contains(v))
				return; // we expect, that in this case, we are in an OnDestroy and should not get this call at all

			foreach(Part p in v.Parts)
			{
				if(KJRJointUtils.IsJointUnlockable(p))
					continue; // exclude those actions from joints that can be dynamically unlocked

				p.crashTolerance = p.crashTolerance / 10000f;
				if(p.attachJoint)
					p.attachJoint.SetUnbreakable(false, false);
			}

			if(!updatingVessels.Contains(v))
			{
				updatingVessels.Add(v);
				StartCoroutine(RunVesselJointUpdateFunctionDelayed(v));
			}
		}

		private void RunVesselJointUpdateFunction(Vessel v)
		{
			if(KJRJointUtils.debug)
			{
				Debug.Log("KJR: Processing vessel " + v.id + " (" + v.GetName() + "); root " +
							v.rootPart.partInfo.name + " (" + v.rootPart.flightID + ")");
			}

			bool bReinforced = false;

#if IncludeAnalyzer
			if(WindowManager.Instance.ReinforceExistingJoints)
			{
#endif

			foreach(Part p in v.Parts)
			{
				if(KJRJointUtils.reinforceAttachNodes)
				{
					if((p.parent != null) && (p.physicalSignificance == Part.PhysicalSignificance.FULL))
					{
						bReinforced = true;
						ReinforceAttachJoints(p);
					}
				}

				if(KJRJointUtils.reinforceDecouplersFurther)
				{
#if Compatible
					if(p.parent && (p.children.Count > 0))
					{
						for(int i = 0; i < p.Modules.Count; i++)
						{
							if(KJRJointUtils.IsOfClass(p.Modules[i], ModuleDecoupleType))
							{
								if(!(bool)isDecoupledField.GetValue(p.Modules[i]))
								{
									bReinforced = true;
									ReinforceDecouplers(p);
									goto continue_;
								}
							}
						}
					}
#endif

#if !Compatible
					ModuleDecouplerBase d = p.GetComponent<ModuleDecouplerBase>(); // FEHLER, wieso nicht auch ModuleDockingNode ??
				
					if(p.parent && (p.children.Count > 0) && d && !d.isDecoupled)
					{
						bReinforced = true;
						ReinforceDecouplers(p);
						continue;
					}
#endif
				}

				if(KJRJointUtils.reinforceLaunchClampsFurther)
				{
					if(p.parent && p.GetComponent<LaunchClamp>())
					{
						ReinforceLaunchClamps(p);
					}
				}
continue_:;
			}

#if IncludeAnalyzer
			}
#endif

			if(bReinforced && !updatedVessels.Contains(v))
				updatedVessels.Add(v);

			if(KJRJointUtils.reinforceAttachNodes && KJRJointUtils.multiPartAttachNodeReinforcement)
				MultiPartJointTreeChildren(v);
		}

#if IncludeAnalyzer
		public void FixedUpdate()
		{
			if(FlightGlobals.ready && FlightGlobals.Vessels != null)
			{
				KJRAnalyzer.Update();
			}
	   }
#endif

		// attachJoint's are always joints from a part to its parent
		private void ReinforceAttachJoints(Part p)
		{
			if(p.rb == null || p.attachJoint == null || !KJRJointUtils.IsJointAdjustmentAllowed(p))
				return;

			if((p.attachMethod == AttachNodeMethod.LOCKED_JOINT)
			&& KJRJointUtils.debug)
			{
				Debug.Log("KJR: Already processed part before: " + p.partInfo.name + " (" + p.flightID + ") -> " +
							p.parent.partInfo.name + " (" + p.parent.flightID + ")");
			}

			List<ConfigurableJoint> jointList;

			if(p.Modules.Contains<CModuleStrut>())
			{
				CModuleStrut s = p.Modules.GetModule<CModuleStrut>();

				if((s.jointTarget != null) && (s.jointRoot != null))
				{
					jointList = s.strutJoint.joints;

					if(jointList != null)
					{
						for(int i = 0; i < jointList.Count; i++)
						{
							ConfigurableJoint j = jointList[i];

							if(j == null)
								continue;

							JointDrive strutDrive = j.angularXDrive;
							strutDrive.positionSpring = KJRJointUtils.decouplerAndClampJointStrength;
							strutDrive.maximumForce = KJRJointUtils.decouplerAndClampJointStrength;
							j.xDrive = j.yDrive = j.zDrive = j.angularXDrive = j.angularYZDrive = strutDrive;

							j.xMotion = j.yMotion = j.zMotion = ConfigurableJointMotion.Locked;
							j.angularXMotion = j.angularYMotion = j.angularZMotion = ConfigurableJointMotion.Locked;

							//float scalingFactor = (s.jointTarget.mass + s.jointTarget.GetResourceMass() + s.jointRoot.mass + s.jointRoot.GetResourceMass()) * 0.01f;

							j.breakForce = KJRJointUtils.decouplerAndClampJointStrength;
							j.breakTorque = KJRJointUtils.decouplerAndClampJointStrength;
						}

						p.attachMethod = AttachNodeMethod.LOCKED_JOINT;
					}
				}
			}
			
			jointList = p.attachJoint.joints;

			if(jointList == null)
				return;

			StringBuilder debugString = new StringBuilder();

			bool addAdditionalJointToParent = KJRJointUtils.multiPartAttachNodeReinforcement;
			//addAdditionalJointToParent &= !(p.Modules.Contains("LaunchClamp") || (p.parent.Modules.Contains("ModuleDecouple") || p.parent.Modules.Contains("ModuleAnchoredDecoupler")));
			addAdditionalJointToParent &= !p.Modules.Contains<CModuleStrut>();

			if(!KJRJointUtils.IsJointUnlockable(p)) // exclude those actions from joints that can be dynamically unlocked
			{
				float partMass = p.mass + p.GetResourceMass();
				for(int i = 0; i < jointList.Count; i++)
				{
					ConfigurableJoint j = jointList[i];
					if(j == null)
						continue;

					String jointType = j.GetType().Name;
					Rigidbody connectedBody = j.connectedBody;

					Part connectedPart = connectedBody.GetComponent<Part>() ?? p.parent;
					float parentMass = connectedPart.mass + connectedPart.GetResourceMass();

					if(partMass < KJRJointUtils.massForAdjustment || parentMass < KJRJointUtils.massForAdjustment)
					{
						if(KJRJointUtils.debug)
							Debug.Log("KJR: Part mass too low, skipping: " + p.partInfo.name + " (" + p.flightID + ")");

						continue;
					}				
				
					// Check attachment nodes for better orientation data
					AttachNode attach = p.FindAttachNodeByPart(p.parent);
					AttachNode p_attach = p.parent.FindAttachNodeByPart(p);
					AttachNode node = attach ?? p_attach;

					if(node == null)
					{
						// Check if it's a pair of coupled docking ports
						var dock1 = p.Modules.GetModule<ModuleDockingNode>();
						var dock2 = p.parent.Modules.GetModule<ModuleDockingNode>();

						//Debug.Log(dock1 + " " + (dock1 ? ""+dock1.dockedPartUId : "?") + " " + dock2 + " " + (dock2 ? ""+dock2.dockedPartUId : "?"));

						if(dock1 && dock2 && (dock1.dockedPartUId == p.parent.flightID || dock2.dockedPartUId == p.flightID))
						{
							attach = p.FindAttachNode(dock1.referenceAttachNode);
							p_attach = p.parent.FindAttachNode(dock2.referenceAttachNode);
							node = attach ?? p_attach;
						}
					}

					// If still no node and apparently surface attached, use the normal one if it's there
					if(node == null && p.attachMode == AttachModes.SRF_ATTACH)
						node = attach = p.srfAttachNode;

					if(KJRJointUtils.debug)
					{
						debugString.AppendLine("Original joint from " + p.partInfo.title + " to " + p.parent.partInfo.title);
						debugString.AppendLine("  " + p.partInfo.name + " (" + p.flightID + ") -> " + p.parent.partInfo.name + " (" + p.parent.flightID + ")");
						debugString.AppendLine("");
						debugString.AppendLine(p.partInfo.title + " Inertia Tensor: " + p.rb.inertiaTensor + " " + p.parent.partInfo.name + " Inertia Tensor: " + connectedBody.inertiaTensor);
						debugString.AppendLine("");


						debugString.AppendLine("Std. Joint Parameters");
						debugString.AppendLine("Connected Body: " + p.attachJoint.Joint.connectedBody);
						debugString.AppendLine("Attach mode: " + p.attachMode + " (was " + jointType + ")");
						if(attach != null)
							debugString.AppendLine("Attach node: " + attach.id + " - " + attach.nodeType + " " + attach.size);
						if(p_attach != null)
							debugString.AppendLine("Parent node: " + p_attach.id + " - " + p_attach.nodeType + " " + p_attach.size);
						debugString.AppendLine("Anchor: " + p.attachJoint.Joint.anchor);
						debugString.AppendLine("Axis: " + p.attachJoint.Joint.axis);
						debugString.AppendLine("Sec Axis: " + p.attachJoint.Joint.secondaryAxis);
						debugString.AppendLine("Break Force: " + p.attachJoint.Joint.breakForce);
						debugString.AppendLine("Break Torque: " + p.attachJoint.Joint.breakTorque);
						debugString.AppendLine("");

						debugString.AppendLine("Joint Motion Locked: " + Convert.ToString(p.attachJoint.Joint.xMotion == ConfigurableJointMotion.Locked));

						debugString.AppendLine("X Drive");
						debugString.AppendLine("Position Spring: " + p.attachJoint.Joint.xDrive.positionSpring);
						debugString.AppendLine("Position Damper: " + p.attachJoint.Joint.xDrive.positionDamper);
						debugString.AppendLine("Max Force: " + p.attachJoint.Joint.xDrive.maximumForce);
						debugString.AppendLine("");

						debugString.AppendLine("Y Drive");
						debugString.AppendLine("Position Spring: " + p.attachJoint.Joint.yDrive.positionSpring);
						debugString.AppendLine("Position Damper: " + p.attachJoint.Joint.yDrive.positionDamper);
						debugString.AppendLine("Max Force: " + p.attachJoint.Joint.yDrive.maximumForce);
						debugString.AppendLine("");

						debugString.AppendLine("Z Drive");
						debugString.AppendLine("Position Spring: " + p.attachJoint.Joint.zDrive.positionSpring);
						debugString.AppendLine("Position Damper: " + p.attachJoint.Joint.zDrive.positionDamper);
						debugString.AppendLine("Max Force: " + p.attachJoint.Joint.zDrive.maximumForce);
						debugString.AppendLine("");

						debugString.AppendLine("Angular X Drive");
						debugString.AppendLine("Position Spring: " + p.attachJoint.Joint.angularXDrive.positionSpring);
						debugString.AppendLine("Position Damper: " + p.attachJoint.Joint.angularXDrive.positionDamper);
						debugString.AppendLine("Max Force: " + p.attachJoint.Joint.angularXDrive.maximumForce);
						debugString.AppendLine("");

						debugString.AppendLine("Angular YZ Drive");
						debugString.AppendLine("Position Spring: " + p.attachJoint.Joint.angularYZDrive.positionSpring);
						debugString.AppendLine("Position Damper: " + p.attachJoint.Joint.angularYZDrive.positionDamper);
						debugString.AppendLine("Max Force: " + p.attachJoint.Joint.angularYZDrive.maximumForce);
						debugString.AppendLine("");


						//Debug.Log(debugString.ToString());
					}


					float breakForce = Math.Min(p.breakingForce, connectedPart.breakingForce) * KJRJointUtils.breakForceMultiplier;
					float breakTorque = Math.Min(p.breakingTorque, connectedPart.breakingTorque) * KJRJointUtils.breakTorqueMultiplier;
					Vector3 anchor = j.anchor;
					Vector3 connectedAnchor = j.connectedAnchor;
					Vector3 axis = j.axis;

					float radius = 0;
					float area = 0;
					float momentOfInertia = 0;

					if(node != null)
					{
						// Part that owns the node. For surface attachment,
						// this can only be parent if docking flips hierarchy.
						Part main = (node == attach) ? p : p.parent;

						// Orientation and position of the node in owner's local coords
						Vector3 ndir = node.orientation.normalized;
						Vector3 npos = node.position + node.offset;

						// And in the current part's local coords
						Vector3 dir = axis = p.transform.InverseTransformDirection(main.transform.TransformDirection(ndir));

						if(node.nodeType == AttachNode.NodeType.Surface)
						{
							// Guessed main axis; for parts with stack nodes should be the axis of the stack
							Vector3 up = KJRJointUtils.GuessUpVector(main).normalized;

							// if guessed up direction is same as node direction, it's basically stack
							// for instance, consider a radially-attached docking port
							if(Mathf.Abs(Vector3.Dot(up, ndir)) > 0.9f)
							{
								radius = Mathf.Min(KJRJointUtils.CalculateRadius(main, ndir), KJRJointUtils.CalculateRadius(connectedPart, ndir));
								if(radius <= 0.001)
									radius = node.size * 1.25f;
								area = Mathf.PI * radius * radius;					  //Area of cylinder
								momentOfInertia = area * radius * radius / 4;		   //Moment of Inertia of cylinder
							}
							else
							{
								// x along surface, y along ndir normal to surface, z along surface & main axis (up)
								var size1 = KJRJointUtils.CalculateExtents(main, ndir, up);

								var size2 = KJRJointUtils.CalculateExtents(connectedPart, ndir, up);

								// use average of the sides, since we don't know which one is used for attaching
								float width1 = (size1.x + size1.z) / 2;
								float width2 = (size2.x + size2.z) / 2;
								if(size1.y * width1 > size2.y * width2)
								{
									area = size1.y * width1;
									radius = Mathf.Max(size1.y, width1);
								}
								else
								{
									area = size2.y * width2;
									radius = Mathf.Max(size2.y, width2);
								}

								momentOfInertia = area * radius / 12;		  //Moment of Inertia of a rectangle bending along the longer length
							}
						}
						else
						{
							radius = Mathf.Min(KJRJointUtils.CalculateRadius(p, dir), KJRJointUtils.CalculateRadius(connectedPart, dir));
							if(radius <= 0.001)
								radius = node.size * 1.25f;
							area = Mathf.PI * radius * radius;					  //Area of cylinder
							momentOfInertia = area * radius * radius / 4;		   //Moment of Inertia of cylinder
						}
					}
					//Assume part is attached along its "up" cross section; use a cylinder to approximate properties
					else if(p.attachMode == AttachModes.STACK)
					{
						radius = Mathf.Min(KJRJointUtils.CalculateRadius(p, Vector3.up), KJRJointUtils.CalculateRadius(connectedPart, Vector3.up));
						if(radius <= 0.001)
							radius = 1.25f; // FEHLER, komisch, wieso setzen wir dann nicht alles < 1.25f auf 1.25f? -> zudem hatten wir hier sowieso einen Bug, das ist also sowieso zu hinterfragen
						area = Mathf.PI * radius * radius;					  //Area of cylinder
						momentOfInertia = area * radius * radius / 4;		   //Moment of Inertia of cylinder
					}
					else if(p.attachMode == AttachModes.SRF_ATTACH)
					{					
						// x,z sides, y along main axis
						Vector3 up1 = KJRJointUtils.GuessUpVector(p);
						var size1 = KJRJointUtils.CalculateExtents(p, up1);

						Vector3 up2 = KJRJointUtils.GuessUpVector(connectedPart);
						var size2 = KJRJointUtils.CalculateExtents(connectedPart, up2);

						// use average of the sides, since we don't know which one is used for attaching
						float width1 = (size1.x + size1.z) / 2;
						float width2 = (size2.x + size2.z) / 2;
						if(size1.y * width1 > size2.y * width2)
						{
							area = size1.y * width1;
							radius = Mathf.Max(size1.y, width1);
						}
						else
						{
							area = size2.y * width2;
							radius = Mathf.Max(size2.y, width2);
						}
						momentOfInertia = area * radius / 12;		  //Moment of Inertia of a rectangle bending along the longer length
					}

					if(KJRJointUtils.useVolumeNotArea)				//If using volume, raise al stiffness-affecting parameters to the 1.5 power
					{
						area = Mathf.Pow(area, 1.5f);
						momentOfInertia = Mathf.Pow(momentOfInertia, 1.5f);
					}


					breakForce = Mathf.Max(KJRJointUtils.breakStrengthPerArea * area, breakForce);
					breakTorque = Mathf.Max(KJRJointUtils.breakTorquePerMOI * momentOfInertia, breakTorque);

					JointDrive angDrive = j.angularXDrive;
					angDrive.positionSpring = Mathf.Max(momentOfInertia * KJRJointUtils.angularDriveSpring, angDrive.positionSpring);
					angDrive.positionDamper = Mathf.Max(momentOfInertia * KJRJointUtils.angularDriveDamper * 0.1f, angDrive.positionDamper);
					angDrive.maximumForce = breakTorque;
					/*float moi_avg = p.rb.inertiaTensor.magnitude;

					moi_avg += (p.transform.localToWorldMatrix.MultiplyPoint(p.CoMOffset) - p.parent.transform.position).sqrMagnitude * p.rb.mass;

					if(moi_avg * 2f / drive.positionDamper < 0.08f)
					{
						drive.positionDamper = moi_avg / (0.04f);

						drive.positionSpring = drive.positionDamper * drive.positionDamper / moi_avg;
					}*/
					j.angularXDrive = j.angularYZDrive = j.slerpDrive = angDrive;

					JointDrive linDrive = j.xDrive;
					linDrive.maximumForce = breakForce;
					j.xDrive = j.yDrive = j.zDrive = linDrive;

					j.linearLimit = j.angularYLimit = j.angularZLimit = j.lowAngularXLimit = j.highAngularXLimit
						= new SoftJointLimit { limit = 0, bounciness = 0 };
					j.linearLimitSpring = j.angularYZLimitSpring = j.angularXLimitSpring
						= new SoftJointLimitSpring { spring = 0, damper = 0 };

					j.targetAngularVelocity = Vector3.zero;
					j.targetVelocity = Vector3.zero;
					j.targetRotation = Quaternion.identity;
					j.targetPosition = Vector3.zero;

					j.breakForce = breakForce;
					j.breakTorque = breakTorque;
					p.attachJoint.SetBreakingForces(j.breakForce, j.breakTorque);

					p.attachMethod = AttachNodeMethod.LOCKED_JOINT;

					if(KJRJointUtils.debug)
					{
						debugString.AppendLine("Updated joint from " + p.partInfo.title + " to " + p.parent.partInfo.title);
						debugString.AppendLine("  " + p.partInfo.name + " (" + p.flightID + ") -> " + p.parent.partInfo.name + " (" + p.parent.flightID + ")");
						debugString.AppendLine("");
						debugString.AppendLine(p.partInfo.title + " Inertia Tensor: " + p.rb.inertiaTensor + " " + p.parent.partInfo.name + " Inertia Tensor: " + connectedBody.inertiaTensor);
						debugString.AppendLine("");


						debugString.AppendLine("Std. Joint Parameters");
						debugString.AppendLine("Connected Body: " + p.attachJoint.Joint.connectedBody);
						debugString.AppendLine("Attach mode: " + p.attachMode + " (was " + jointType + ")");
						if(attach != null)
							debugString.AppendLine("Attach node: " + attach.id + " - " + attach.nodeType + " " + attach.size);
						if(p_attach != null)
							debugString.AppendLine("Parent node: " + p_attach.id + " - " + p_attach.nodeType + " " + p_attach.size);
						debugString.AppendLine("Anchor: " + p.attachJoint.Joint.anchor);
						debugString.AppendLine("Axis: " + p.attachJoint.Joint.axis);
						debugString.AppendLine("Sec Axis: " + p.attachJoint.Joint.secondaryAxis);
						debugString.AppendLine("Break Force: " + p.attachJoint.Joint.breakForce);
						debugString.AppendLine("Break Torque: " + p.attachJoint.Joint.breakTorque);
						debugString.AppendLine("");

						debugString.AppendLine("Joint Motion Locked: " + Convert.ToString(p.attachJoint.Joint.xMotion == ConfigurableJointMotion.Locked));

						debugString.AppendLine("Angular Drive");
						debugString.AppendLine("Position Spring: " + angDrive.positionSpring);
						debugString.AppendLine("Position Damper: " + angDrive.positionDamper);
						debugString.AppendLine("Max Force: " + angDrive.maximumForce);
						debugString.AppendLine("");

						debugString.AppendLine("Cross Section Properties");
						debugString.AppendLine("Radius: " + radius);
						debugString.AppendLine("Area: " + area);
						debugString.AppendLine("Moment of Inertia: " + momentOfInertia);
					}
				}
			}

#if IncludeAnalyzer
			addAdditionalJointToParent &= WindowManager.Instance.BuildAdditionalJointToParent;
#endif

			if(addAdditionalJointToParent && p.parent.parent != null
			&& KJRJointUtils.IsJointAdjustmentAllowed(p.parent))	// verify that parent is not an excluded part -> we will skip this in our calculation, that's why we need to check it now
			{
				ConfigurableJoint j = p.attachJoint.Joint; // second steps uses the first/main joint as reference

				Part newConnectedPart = p.parent.parent;

				bool massRatioBelowThreshold = false;
				int numPartsFurther = 0;

				float partMaxMass = KJRJointUtils.MaximumPossiblePartMass(p);
				List<Part> partsCrossed = new List<Part>();
				List<Part> possiblePartsCrossed = new List<Part>();

				partsCrossed.Add(p.parent);

				Part connectedRbPart = newConnectedPart;

				// search the first part with an acceptable mass/mass ration to this part (joints work better then)
				do
				{
					float massRat1 = (partMaxMass < newConnectedPart.mass) ? (newConnectedPart.mass / partMaxMass) : (partMaxMass / newConnectedPart.mass);

					if(massRat1 <= KJRJointUtils.stiffeningExtensionMassRatioThreshold)
						massRatioBelowThreshold = true;
					else
					{
						float maxMass = KJRJointUtils.MaximumPossiblePartMass(newConnectedPart);
						float massRat2 = (p.mass < maxMass) ? (maxMass / p.mass) : (p.mass / maxMass);
						
						if(massRat2 <= KJRJointUtils.stiffeningExtensionMassRatioThreshold)
							massRatioBelowThreshold = true;
						else
						{
							if((newConnectedPart.parent == null)
							|| !KJRJointUtils.IsJointAdjustmentAllowed(newConnectedPart))
								break;

							newConnectedPart = newConnectedPart.parent;

							if(newConnectedPart.rb == null)
								possiblePartsCrossed.Add(newConnectedPart);
							else
							{
								connectedRbPart = newConnectedPart;
								partsCrossed.AddRange(possiblePartsCrossed);
								partsCrossed.Add(newConnectedPart);
								possiblePartsCrossed.Clear();
							}

							numPartsFurther++;
						}
					}

				} while(!massRatioBelowThreshold);// && numPartsFurther < 5);

				if(newConnectedPart.rb != null && !multiJointManager.CheckDirectJointBetweenParts(p, newConnectedPart))
				{
					ConfigurableJoint newJoint;

					if((p.mass >= newConnectedPart.mass) || (p.rb == null))
					{
						newJoint = p.gameObject.AddComponent<ConfigurableJoint>();
						newJoint.connectedBody = newConnectedPart.rb;

						newJoint.anchor = Vector3.zero;

						if(!KJRJointUtils.useOldJointCreation)
						{
							newJoint.autoConfigureConnectedAnchor = false;
							newJoint.connectedAnchor = Quaternion.Inverse(newConnectedPart.orgRot) * (p.orgPos - newConnectedPart.orgPos);

							Quaternion must = newConnectedPart.transform.rotation * (Quaternion.Inverse(newConnectedPart.orgRot) * p.orgRot);
							newJoint.SetTargetRotationLocal(Quaternion.Inverse(p.transform.rotation) * must, Quaternion.identity);
							// FEHLER, direkter machen
						}
					}
					else
					{
						newJoint = newConnectedPart.gameObject.AddComponent<ConfigurableJoint>();
						newJoint.connectedBody = p.rb;

						newJoint.anchor = Vector3.zero;

						if(!KJRJointUtils.useOldJointCreation)
						{
							newJoint.autoConfigureConnectedAnchor = false;
							newJoint.connectedAnchor = Quaternion.Inverse(p.orgRot) * (newConnectedPart.orgPos - p.orgPos);

							Quaternion must = p.transform.rotation * (Quaternion.Inverse(p.orgRot) * newConnectedPart.orgRot);
							newJoint.SetTargetRotationLocal(Quaternion.Inverse(newConnectedPart.transform.rotation) * must, Quaternion.identity);
							// FEHLER, direkter machen
						}
					}

		//			newJoint.linearLimit = newJoint.angularYLimit = newJoint.angularZLimit = newJoint.lowAngularXLimit = newJoint.highAngularXLimit
		//				= new SoftJointLimit { limit = 0, bounciness = 0 }; // FEHLER, hab das mal rausgenommen -> evtl. später doch wieder Limited arbeiten??

					newJoint.xMotion = newJoint.yMotion = newJoint.zMotion = ConfigurableJointMotion.Free;
					newJoint.angularYMotion = newJoint.angularZMotion = newJoint.angularXMotion = ConfigurableJointMotion.Free;

					newJoint.xDrive = j.xDrive; newJoint.yDrive = j.yDrive; newJoint.zDrive = j.zDrive;
					newJoint.angularXDrive = newJoint.angularYZDrive = newJoint.slerpDrive = j.angularXDrive;

					newJoint.breakForce = j.breakForce;
					newJoint.breakTorque = j.breakTorque;

					// register joint
					multiJointManager.RegisterMultiJoint(p, newJoint, true, KJRMultiJointManager.Reason.AdditionalJointToParent);
					multiJointManager.RegisterMultiJoint(newConnectedPart, newJoint, true, KJRMultiJointManager.Reason.AdditionalJointToParent);

					foreach(Part part in partsCrossed)
						multiJointManager.RegisterMultiJoint(part, newJoint, false, KJRMultiJointManager.Reason.AdditionalJointToParent);
				}
			}

			if(KJRJointUtils.debug)
				Debug.Log(debugString.ToString());
		}

		private void MultiPartJointBuildJoint(Part part, Part linkPart, KJRMultiJointManager.Reason jointReason)
		{
			if(multiJointManager.CheckDirectJointBetweenParts(part, linkPart)
			|| !multiJointManager.TrySetValidLinkedSet(part, linkPart))
				return;

			ConfigurableJoint joint = KJRJointUtils.BuildJoint(part, linkPart);

			multiJointManager.RegisterMultiJoint(part, joint, true, jointReason);
			multiJointManager.RegisterMultiJoint(linkPart, joint, true, jointReason);

			foreach(Part p in multiJointManager.linkedSet)
				multiJointManager.RegisterMultiJoint(p, joint, false, jointReason);
		}

			// FEHLER, überarbeiten... ist das nicht etwas sehr viel was wir hier aufbauen???
		private void ReinforceDecouplers(Part part)
		{
			List<Part> childParts = new List<Part>();
			List<Part> parentParts = new List<Part>();

			parentParts = KJRJointUtils.DecouplerPartStiffeningListParents(part.parent);

			foreach(Part p in part.children)
			{
				if(KJRJointUtils.IsJointAdjustmentAllowed(p))
				{
					childParts.AddRange(KJRJointUtils.DecouplerPartStiffeningListChildren(p));
					if(!childParts.Contains(p))
						childParts.Add(p);
				}
			}

			parentParts.Add(part);

			StringBuilder debugString = null;

			if(KJRJointUtils.debug)
			{
				debugString = new StringBuilder();
				debugString.AppendLine(parentParts.Count + " parts above decoupler to be connected to " + childParts.Count + " below decoupler.");
				debugString.AppendLine("The following joints added by " + part.partInfo.title + " to increase stiffness:");
			}

			foreach(Part p in parentParts)
			{
				if(p == null || p.rb == null || p.Modules.Contains("ProceduralFairingDecoupler"))
					continue;

				foreach(Part q in childParts)
				{
					if(q == null || q.rb == null || p == q || q.Modules.Contains("ProceduralFairingDecoupler"))
						continue;

					if(p.vessel != q.vessel)
						continue;

					MultiPartJointBuildJoint(p, q, KJRMultiJointManager.Reason.ReinforceDecoupler);

					if(KJRJointUtils.debug)
						debugString.AppendLine(p.partInfo.title + " connected to part " + q.partInfo.title);
				}
			}


			if(KJRJointUtils.debug)
				Debug.Log(debugString.ToString());
		}

		private void ReinforceLaunchClamps(Part part)
		{
			part.breakingForce = Mathf.Infinity;
			part.breakingTorque = Mathf.Infinity;
			part.mass = Mathf.Max(part.mass, (part.parent.mass + part.parent.GetResourceMass()) * 0.01f);		  //We do this to make sure that there is a mass ratio of 100:1 between the clamp and what it's connected to.  This helps counteract some of the wobbliness simply, but also allows some give and springiness to absorb the initial physics kick

			if(KJRJointUtils.debug)
				Debug.Log("KJR: Launch Clamp Break Force / Torque increased");

			StringBuilder debugString = null;

			if(KJRJointUtils.debug)
			{
				debugString = new StringBuilder();
				debugString.AppendLine("The following joints added by " + part.partInfo.title + " to increase stiffness:");
			}

			if(part.parent.Rigidbody != null)
				MultiPartJointBuildJoint(part, part.parent, KJRMultiJointManager.Reason.ReinforceLaunchClamp);

			if(KJRJointUtils.debug)
			{
				debugString.AppendLine(part.parent.partInfo.title + " connected to part " + part.partInfo.title);
				Debug.Log(debugString.ToString());
			}
		}

		public void MultiPartJointTreeChildren(Vessel v)
		{
			if(v.Parts.Count <= 1)
				return;

			Dictionary<Part, List<Part>> childPartsToConnectByRoot = new Dictionary<Part,List<Part>>();

			for(int i = 0; i < v.Parts.Count; ++i)
			{
				Part p = v.Parts[i];

				bool bEndPoint = (p.children.Count == 0);

				if(!bEndPoint && !KJRJointUtils.IsJointAdjustmentAllowed(p) && p.parent && (p.parent.vessel == v))
				{
					p = p.parent;

					bEndPoint = true;
					for(int j = 0; j < p.children.Count; j++)
					{
						if(KJRJointUtils.IsJointAdjustmentAllowed(p.children[j]))
						{ bEndPoint = false; break; }
					}
				}

				if(bEndPoint && !p.Modules.Contains("LaunchClamp") && KJRJointUtils.MaximumPossiblePartMass(p) > KJRJointUtils.massForAdjustment)
				{
					if(p.rb == null && p.Rigidbody != null)
						p = p.RigidBodyPart;

					Part root = p;
					while(root.parent && (root.parent.vessel == v) && KJRJointUtils.IsJointAdjustmentAllowed(root))
						root = root.parent;

					List<Part> childPartsToConnect;
					if(!childPartsToConnectByRoot.TryGetValue(root, out childPartsToConnect))
					{
						childPartsToConnect = new List<Part>();
						childPartsToConnectByRoot.Add(root, childPartsToConnect);
					}

					childPartsToConnect.Add(p);
				}
			}

			foreach(Part root in childPartsToConnectByRoot.Keys)
			{
				List<Part> childPartsToConnect = childPartsToConnectByRoot[root];

				for(int i = 0; i < childPartsToConnect.Count; ++i)
				{
					Part p = childPartsToConnect[i];

					if(!p.rb)
						continue;

					Part linkPart = childPartsToConnect[i + 1 >= childPartsToConnect.Count ? 0 : i + 1];

					if(!linkPart.Rigidbody || p.rb == linkPart.Rigidbody)
						continue;

#if IncludeAnalyzer
					if(WindowManager.Instance.BuildMultiPartJointTreeChildren)
#endif
						MultiPartJointBuildJoint(p, linkPart, KJRMultiJointManager.Reason.MultiPartJointTreeChildren);


					int part2Index = i + childPartsToConnect.Count / 2;
					if(part2Index >= childPartsToConnect.Count)
						part2Index -= childPartsToConnect.Count;

					Part linkPart2 = childPartsToConnect[part2Index];

					if(!linkPart2.Rigidbody || p.rb == linkPart2.Rigidbody)
						continue;

#if IncludeAnalyzer
					if(WindowManager.Instance.BuildMultiPartJointTreeChildren)
#endif
						MultiPartJointBuildJoint(p, linkPart2, KJRMultiJointManager.Reason.MultiPartJointTreeChildren);


					if(!root.Rigidbody || p.rb == root.Rigidbody)
						continue;

#if IncludeAnalyzer
					if(WindowManager.Instance.BuildMultiPartJointTreeChildrenRoot)
#endif
						MultiPartJointBuildJoint(p, root, KJRMultiJointManager.Reason.MultiPartJointTreeChildrenRoot);
				}
			}
		}
	}
}
