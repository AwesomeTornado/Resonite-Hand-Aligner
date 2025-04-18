
using System.Collections.Generic;
using System.Linq;

using FrooxEngine;
using FrooxEngine.FinalIK;
using Elements.Core;
using HarmonyLib;

using ResoniteModLoader;

namespace HandAligner;
//More info on creating mods can be found https://github.com/resonite-modding-group/ResoniteModLoader/wiki/Creating-Mods
public class HandAligner : ResoniteMod {
	internal const string VERSION_CONSTANT = "1.0.0"; //Changing the version here updates it in all locations needed
	public override string Name => "HandAligner";
	public override string Author => "__Choco__";
	public override string Version => VERSION_CONSTANT;
	public override string Link => "https://github.com/resonite-modding-group/HandAligner/";//FIX THIS

	public override void OnEngineInit() {
		Harmony harmony = new Harmony("com.__Choco__.HandAligner");
		Msg("HandAligner loaded.");
		harmony.PatchAll();
	}

	//Example of how a HarmonyPatch can be formatted, Note that the following isn't a real patch and will not compile.
	[HarmonyPatch(typeof(AvatarCreator), "AlignHands", MethodType.Normal)]
	
	class AlignAvatarHands {
		static bool Prefix(AvatarCreator __instance) {
			Msg("Prefix from HandAligner");
			//Traverse might help with this?
			
			List<Slot> list = new List<Slot>();
			var avatarCreator_2 = Traverse.Create(__instance);
			BipedRig biped = (BipedRig)avatarCreator_2.Method("TryGetBipedFromHead", new object[] { list }).GetValue();

			if (biped == null || !biped.IsValid) {
				Error("Invalid BipedRig, either null or invalid");
				return true;
			}
			VRIK ik = biped.Slot.GetComponentInChildrenOrParents<VRIK>(null, false);
			if (ik == null) {
				Error("No VRIK found in BipedRig");
				return true;
			}
			Slot leftRef = (avatarCreator_2.Field("_leftReference").GetValue() as SyncRef<Slot>).Target; //__instance._leftReference.Target;
			Slot target = (avatarCreator_2.Field("_rightReference").GetValue() as SyncRef<Slot>).Target; //__instance._rightReference.Target;
			Slot leftHand = biped[BodyNode.LeftHand];
			Slot rightHand = biped[BodyNode.RightHand];
			IKSolverVR.Arm leftArmIk = ik.Solver.leftArm;
			IKSolverVR.Arm rightArmIk = ik.Solver.rightArm;
			leftRef.GlobalPosition = leftHand.GlobalPosition;
			target.GlobalPosition = rightHand.GlobalPosition;
			Msg("Function ran successfully!");
			return false;
		}

		static void SetAviCreatorHandRotation(BipedRig bipedRig, AvatarCreator avatarCreator, float3 localScale, bool rightSide) {
			float3 globalFingerTipRef1;
			float3 globalFingerTipRef2;
			float3 localAviCreatorTipRef1;
			float3 localAviCreatorTipRef2;
			Slot[] nonThumbFingerTips = GetFingerTips(bipedRig, rightSide, out float3[] noThumbAviCreatorTipRefs, includeThumb: false);
			Slot avatarCreatorHand = avatarCreator.Slot.FindChild(rightSide ? "RightHand" : "LeftHand", matchSubstring: false, ignoreCase: false, maxDepth: 0);
			// only one finger, need to also use thumb for other ref
			if (nonThumbFingerTips.Length <= 1) {
				Slot[] withThumbFingerTips = GetFingerTips(bipedRig, rightSide, out float3[] aviCreatorTipRefs, includeThumb: true);
				if (withThumbFingerTips.Length <= 1) {
					Slot handSlot = bipedRig.TryGetBone(rightSide ? BodyNode.RightHand : BodyNode.LeftHand);
					if (handSlot == null) {
						// idk what u want from me
						return;
					}
					Slot fingerSlot = null;
					if (withThumbFingerTips.Length == 0) {
						// sometimes they have a finger end slot we can use
						Slot[] childFingers =
							handSlot.GetAllChildren()
							.Where(x => !x.LocalPosition.Approximately(float3.Zero, 0.0001f))
							.ToArray();
						if (childFingers.Length > 0) {
							fingerSlot = childFingers[0];
						}
					} else {
						fingerSlot = withThumbFingerTips[0];
					}
					if (fingerSlot == null) {
						// just align with hand rotation, not much else we can do
						avatarCreatorHand.GlobalRotation = handSlot.GlobalRotation;
						return;
					} else {
						float3 fingerDir = avatarCreatorHand.GlobalPosition - fingerSlot.GlobalPosition;
						// it's just zero
						if (fingerDir.Approximately(float3.Zero, 0.00001f)) {
							// just align with hand rotation, not much else we can do
							avatarCreatorHand.GlobalRotation = handSlot.GlobalRotation;
							return;
						} else {
							// make two fake fingers going out from cross of up and dir from hand to finger
							float3 fanOutDir = MathX.Cross(fingerDir.Normalized, float3.Up);
							globalFingerTipRef1 = fingerSlot.GlobalPosition - fanOutDir * 0.01f;
							globalFingerTipRef2 = fingerSlot.GlobalPosition + fanOutDir * 0.01f;
							localAviCreatorTipRef1 = rightSide
								? relativeFingerPositionsRight[1]
								: relativeFingerPositionsLeft[1]; // index finger
							localAviCreatorTipRef2 = rightSide
								? relativeFingerPositionsRight[3]
								: relativeFingerPositionsLeft[3]; // ring finger
						}
					}
				} else {
					globalFingerTipRef1 = withThumbFingerTips[0].GlobalPosition;
					globalFingerTipRef2 = withThumbFingerTips[1].GlobalPosition;
					localAviCreatorTipRef1 = aviCreatorTipRefs[0];
					localAviCreatorTipRef2 = aviCreatorTipRefs[1];
				}
			} else {
				// do first and last (non-thumb) finger, these are furthest apart which makes aligning nicer
				globalFingerTipRef1 = nonThumbFingerTips[0].GlobalPosition;
				globalFingerTipRef2 = nonThumbFingerTips[nonThumbFingerTips.Length - 1].GlobalPosition;
				localAviCreatorTipRef1 = noThumbAviCreatorTipRefs[0];
				localAviCreatorTipRef2 = noThumbAviCreatorTipRefs[nonThumbFingerTips.Length - 1];
			}
			// now we have two finger points (fingerTipRef1 and fingerTipRef2)
			// and two points on the avatar creator glove that we want to align to those points
			// so we want to find a rotation for avatarCreatorHand such that
			// Distance(
			//   avatarCreatorHand.LocalPointToGlobal(aviCreatorTipRef1),
			//    fingerTipRef1
			// ) +
			// Distance(
			//   avatarCreatorHand.LocalPointToGlobal(aviCreatorTipRef2),
			//    fingerTipRef2
			// )
			// is minimized

			// to do this, first find the rotation that aligns the midpoints:
			float3 globalFingerTipMidpoint = (globalFingerTipRef1 + globalFingerTipRef2) / 2.0f;
			float3 localFingerTipMidpoint = avatarCreatorHand.GlobalPointToLocal(globalFingerTipMidpoint);
			float3 localAviTipRefsMidpoint = (localAviCreatorTipRef1 + localAviCreatorTipRef2) / 2.0f;
			floatQ lineUpMidpointRotation = floatQ.FromToRotation(localAviTipRefsMidpoint.Normalized, localFingerTipMidpoint.Normalized);
			avatarCreatorHand.LocalRotation = avatarCreatorHand.LocalRotation * lineUpMidpointRotation;
			// double check we did it right
			float3 newDirToFingerTipMidpoint = avatarCreatorHand.GlobalPointToLocal(globalFingerTipMidpoint);
			//ImportFromUnityLib.DebugLog("got new finger tip midpoint:" + newDirToFingerTipMidpoint.Normalized);
			//ImportFromUnityLib.DebugLog("target direction is        :" + localAviTipRefsMidpoint.Normalized);

			// now the midpoint is lined up, we just need to rotate around vecToFingerTipMidpoint until the two points are best aligned
			// there's probably an analytic solution (feel free to PR such a solution) but iterative is good enough for a one-time thing
			int ITERS = 2000;

			float minScore = float.MaxValue;
			floatQ bestRotation = floatQ.Identity;
			floatQ baseLocalRotation = avatarCreatorHand.LocalRotation;
			float3 localPosition = avatarCreatorHand.LocalPosition;
			float3 globalPosition = avatarCreatorHand.Parent.LocalPointToGlobal(localPosition);
			float3 globalScale = avatarCreatorHand.Parent.LocalScaleToGlobal(localScale);
			for (int i = 0; i < ITERS; i++) {
				float angleRotation = 360f * (i / (float)(ITERS - 1));
				floatQ localRotation = baseLocalRotation * floatQ.AxisAngle(localAviTipRefsMidpoint, angleRotation);
				avatarCreatorHand.LocalRotation = localRotation;
				float score = (
				   avatarCreatorHand.LocalPointToGlobal(localAviCreatorTipRef1) -
				   globalFingerTipRef1
				).Magnitude + (
				   avatarCreatorHand.LocalPointToGlobal(localAviCreatorTipRef2) -
				   globalFingerTipRef2
				).Magnitude;
				//ImportFromUnityLib.DebugLog("Got score:" + score + " with angle " + angleRotation);
				//ImportFromUnityLib.DebugLog("fingerTipRef1: " + globalFingerTipRef1);
				//ImportFromUnityLib.DebugLog("avi tip ref  : " + avatarCreatorHand.LocalPointToGlobal(localAviCreatorTipRef1));
				//ImportFromUnityLib.DebugLog("fingerTipRef1: " + globalFingerTipRef2);
				//ImportFromUnityLib.DebugLog("avi tip ref  : " + avatarCreatorHand.LocalPointToGlobal(localAviCreatorTipRef2));
				if (score < minScore) {
					//ImportFromUnityLib.DebugLog("Got best score:" + score + " with angle " + angleRotation);
					minScore = score;
					bestRotation = localRotation;
				}
			}
			avatarCreatorHand.LocalRotation = bestRotation;
		}

		static Slot[] GetFingerTips(BipedRig bipedRig, bool rightSide, out float3[] tipRefs, bool includeThumb) {
			BodyNode[][] fingerOrders = rightSide ? rightFingerOrders : leftFingerOrders;
			float3[] handTipRefs = rightSide ? relativeFingerPositionsRight : relativeFingerPositionsLeft;
			List<Slot> fingerTips = new List<Slot>();
			List<float3> tipRefsList = new List<float3>();
			for (int i = 0; i < fingerOrders.Length; i++) {
				//ImportFromUnityLib.DebugLog("Finger order:" + fingerOrders[i]);
				BodyNode[] fingerOrder = fingerOrders[i];
				// todo: lookup positions for left hand
				if (!includeThumb && fingerOrder[0].ToString().ToLower().Contains("thumb")) {
					continue;
				}
				// get furthest part of finger available
				Slot fingerTip = null;
				foreach (BodyNode fingerPart in fingerOrder) {
					//ImportFromUnityLib.DebugLog("Finger part:" + fingerPart);
					Slot curTip = bipedRig.TryGetBone(fingerPart);
					if (curTip != null) {
						fingerTip = curTip;
					}
				}
				if (fingerTip != null) {
					//ImportFromUnityLib.DebugLog("Got tip for finger:" + fingerTip + " with i " + i);
					tipRefsList.Add(handTipRefs[i]);
					//ImportFromUnityLib.DebugLog("set tip for finger:" + fingerTip);
					fingerTips.Add(fingerTip);
				}
			}
			tipRefs = tipRefsList.ToArray();
			return fingerTips.ToArray();
		}

		static float3[] relativeFingerPositionsRight = new float3[] {
			new float3(-0.0944553f, -0.06033006f, 0.1202253f), // thumb
            new float3(-0.03632067f, -0.0295704f, 0.2140587f), // index
            new float3(-0.01105062f, -0.02654553f, 0.2155553f), // middle
            new float3(0.01396004f, -0.02654572f, 0.2100046f), // ring
            new float3(0.03692787f, -0.02956969f, 0.1954267f) // pinky
        };

		static float3[] relativeFingerPositionsLeft = new float3[]
		{
			new float3(0.09763367f, -0.06523453f, 0.1208142f), // thumb
            new float3(0.03690476f, -0.02722489f, 0.209686f), // index
            new float3(0.01103727f, -0.02597604f, 0.2162421f), // middle
            new float3(-0.0133688f, -0.0280386f, 0.2088304f), // ring
            new float3(-0.03593383f, -0.03025665f, 0.1963694f) // pinky
        };

		static BodyNode[][] leftFingerOrders = new BodyNode[][]
		{
			new BodyNode[]
			{
				BodyNode.LeftThumb_Metacarpal,
				BodyNode.LeftThumb_Proximal,
				BodyNode.LeftThumb_Distal,
				BodyNode.LeftThumb_Tip,
			},
			new BodyNode[]
			{
				BodyNode.LeftIndexFinger_Metacarpal,
				BodyNode.LeftIndexFinger_Proximal,
				BodyNode.LeftIndexFinger_Intermediate,
				BodyNode.LeftIndexFinger_Distal,
				BodyNode.LeftIndexFinger_Tip,
			},
			new BodyNode[]
			{
				BodyNode.LeftMiddleFinger_Metacarpal,
				BodyNode.LeftMiddleFinger_Proximal,
				BodyNode.LeftMiddleFinger_Intermediate,
				BodyNode.LeftMiddleFinger_Distal,
				BodyNode.LeftMiddleFinger_Tip,
			},
			new BodyNode[]
			{
				BodyNode.LeftRingFinger_Metacarpal,
				BodyNode.LeftRingFinger_Proximal,
				BodyNode.LeftRingFinger_Intermediate,
				BodyNode.LeftRingFinger_Distal,
				BodyNode.LeftRingFinger_Tip,
		   },
			new BodyNode[]
			{
				BodyNode.LeftPinky_Metacarpal,
				BodyNode.LeftPinky_Proximal,
				BodyNode.LeftPinky_Intermediate,
				BodyNode.LeftPinky_Distal,
				BodyNode.LeftPinky_Tip,
			},
		};

		static BodyNode[][] rightFingerOrders = new BodyNode[][]
		{
			new BodyNode[]
			{
				BodyNode.RightThumb_Metacarpal,
				BodyNode.RightThumb_Proximal,
				BodyNode.RightThumb_Distal,
				BodyNode.RightThumb_Tip,
			},
			new BodyNode[]
			{
				BodyNode.RightIndexFinger_Metacarpal,
				BodyNode.RightIndexFinger_Proximal,
				BodyNode.RightIndexFinger_Intermediate,
				BodyNode.RightIndexFinger_Distal,
				BodyNode.RightIndexFinger_Tip,
			},
			new BodyNode[]
			{
				BodyNode.RightMiddleFinger_Metacarpal,
				BodyNode.RightMiddleFinger_Proximal,
				BodyNode.RightMiddleFinger_Intermediate,
				BodyNode.RightMiddleFinger_Distal,
				BodyNode.RightMiddleFinger_Tip,
			},
			new BodyNode[]
			{
				BodyNode.RightRingFinger_Metacarpal,
				BodyNode.RightRingFinger_Proximal,
				BodyNode.RightRingFinger_Intermediate,
				BodyNode.RightRingFinger_Distal,
				BodyNode.RightRingFinger_Tip,
		   },
			new BodyNode[]
			{
				BodyNode.RightPinky_Metacarpal,
				BodyNode.RightPinky_Proximal,
				BodyNode.RightPinky_Intermediate,
				BodyNode.RightPinky_Distal,
				BodyNode.RightPinky_Tip,
			},
		};

	}
}
