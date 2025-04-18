
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
	public override string Author => "ExampleAuthor";
	public override string Version => VERSION_CONSTANT;
	public override string Link => "https://github.com/resonite-modding-group/HandAligner/";

	public override void OnEngineInit() {
		Harmony harmony = new Harmony("com.example.HandAligner");
		harmony.PatchAll();
	}

	//Example of how a HarmonyPatch can be formatted, Note that the following isn't a real patch and will not compile.
	[HarmonyPatch(typeof(AvatarCreator), "AlignHands")]
	class AlignAvatarHands {
		static void Postfix(AvatarCreator __instance) {
			Msg("Prefix from HandAligner");
			
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
	}
}
