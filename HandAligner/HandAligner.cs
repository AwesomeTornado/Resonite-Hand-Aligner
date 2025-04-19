
using System.Collections.Generic;
using System.Linq;
using FrooxEngine;
using Elements.Core;
using HarmonyLib;
using ResoniteModLoader;
using System;

namespace HandAligner;
//More info on creating mods can be found https://github.com/resonite-modding-group/ResoniteModLoader/wiki/Creating-Mods
public class HandAligner : ResoniteMod {
	internal const string VERSION_CONSTANT = "1.0.0"; //Changing the version here updates it in all locations needed
	public override string Name => "HandAligner";
	public override string Author => "__Choco__";
	public override string Version => VERSION_CONSTANT;
	public override string Link => "https://github.com/AwesomeTornado/Resonite-Hand-Aligner";

	public override void OnEngineInit() {
		Harmony harmony = new Harmony("com.__Choco__.HandAligner");
		harmony.Patch(AccessTools.Method(typeof(AvatarCreator), "AlignHands"), postfix: AccessTools.Method(typeof(AlignmentPatchMethods), "AlignHands"));
		harmony.Patch(AccessTools.Method(typeof(AvatarCreator), "TryGetBipedFromHead"), postfix: AccessTools.Method(typeof(AlignmentPatchMethods), "trygetbipedfromhead"));
		Msg("HandAligner loaded.");
		harmony.PatchAll();
	}

	class AlignmentPatchMethods {

		static BipedRig biped_cache;

		static void AlignHands(AvatarCreator __instance) {
			Warn("Remember to turn off symmetry!!!! (this message always appears)");

			BipedRig biped = biped_cache;
			Msg("Instantiated \"biped\"");
			if (biped == null) {
				Error("Invalid BipedRig, null");
				return;
			}
			if (!biped.IsValid) {
				Error("Invalid BipedRig, invalid");
				return;
			}
			Msg("var \"biped\" passed null and validity checks successfully.");

			SetAviCreatorHandRotation(biped, __instance, float3.One, true);
			SetAviCreatorHandRotation(biped, __instance, float3.One, false);

			Msg("Function ran successfully!");
		}

		static void trygetbipedfromhead(AvatarCreator __instance, ref BipedRig __result) {
			if (__result is null) {
				Error("Biped rig is null");
				return;
			}
			if (!__result.IsValid) {
				Error("Biped rig is invalid");
				return;
			}
			Msg("trygetbipedfromhead success, caching");
			//this is... the wrong way to get the biped rig.
			//Regardless, it works, so it stays.
			biped_cache = __result;
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
			//Msg("got new finger tip midpoint:" + newDirToFingerTipMidpoint.Normalized);
			//Msg("target direction is        :" + localAviTipRefsMidpoint.Normalized);

			//this code used to be all floats and float3s, but then the iterative solution
			//would sometimes produce a better answer due to rounding errors.
			//Now that eveyrthing is a double, I thought that would be fixed.
			//Unfortunatley, there is still some error. The analytical solution usualy wins though,
			//and only loses by numbers that are as small as XX*10^-5

			//define our starting variables.
			double3 Axis = localAviTipRefsMidpoint;
			double3 pointAGoal = avatarCreatorHand.GlobalPointToLocal(globalFingerTipRef1);
			double3 pointBGoal = avatarCreatorHand.GlobalPointToLocal(globalFingerTipRef2);
			double3 pointAReal = localAviCreatorTipRef1;
			double3 pointBReal = localAviCreatorTipRef2;

			//these vectors are all in plane
			double3 pointAReal_to_Axis = PointToVector(pointAReal, Axis);
			double3 pointBReal_to_Axis = PointToVector(pointBReal, Axis);
			double3 pointAGoal_to_Axis = PointToVector(pointAGoal, Axis);
			double3 pointBGoal_to_Axis = PointToVector(pointBGoal, Axis);

			//I am using a custom implementation of MathX.AngleRad here because 
			//mathx.anglerad can not return negative angles.
			//this calculates the distance each point needs to rotate to have ideal orientation
			double angleA = VectorsToAngle(pointAReal_to_Axis, pointAGoal_to_Axis, Axis);
			double angleB = VectorsToAngle(pointBReal_to_Axis, pointBGoal_to_Axis, Axis);

			//Error("angleA = " + (float)(angleA * ((float)180f / Math.PI)) + " angleB = " + (float)(angleB * ((float)180f / Math.PI)));

			//calculate the best possible rotation and the worst possible rotation
			doubleQ angleABestRotation = doubleQ.AxisAngleRad(Axis, angleA );
			doubleQ angleAWorstRotation = doubleQ.AxisAngleRad(Axis, angleA + MathX.PI);
			doubleQ angleBBestRotation = doubleQ.AxisAngleRad(Axis, angleB );
			doubleQ angleBWorstRotation = doubleQ.AxisAngleRad(Axis, angleB + MathX.PI);

			//find the real world values of rotating the points by the earlier values
			//This will be used for weighting later.
			double3 pointABest = angleABestRotation * pointAReal; //quat math isn't commutative lol
			double3 pointAWorst = angleAWorstRotation * pointAReal;
			double3 pointBBest = angleBBestRotation * pointBReal;
			double3 pointBWorst = angleBWorstRotation * pointBReal;
			//Error("close a " + pointABest + " furth a " + pointAWorst + " close b " + pointBBest + " furth b " + pointBWorst);

			//find the distance to goal point for best and worst case.
			//This will be used for weighting later.
			double3 pointABest_to_goal = pointAGoal - pointABest;
			double3 pointAWorst_to_goal = pointAGoal - pointAWorst;
			//Warn("pointABest_to_goal.mag = " + pointABest_to_goal.Magnitude + " pointAWorst_to_goal.mag = " + pointAWorst_to_goal.Magnitude);
			if(pointABest_to_goal.Magnitude > pointAWorst.Magnitude) {
				Error(" ERROR: pointABest.mag is greater than pointAWorst.mag");
			}

			//find the distance to goal point for best and worst case.
			//This will be used for weighting later.
			double3 pointBBest_to_goal = pointBGoal - pointBBest;
			double3 pointBWorst_to_goal = pointBGoal - pointBWorst;
			//Warn("pointBBest_to_goal.mag = " + pointBBest_to_goal.Magnitude + " pointBWorst_to_goal.mag = " + pointBWorst_to_goal.Magnitude);
			if (pointBBest_to_goal.Magnitude > pointBWorst.Magnitude) {
				Error(" ERROR: pointBBest.mag is greater than pointBWorst.mag");
			}

			//the power, or "significance" of each point is equal to its potential to affect the score.
			//potential to affect the score is defined as the worst score minus the best score.
			double powerA = pointAWorst_to_goal.Magnitude - pointABest_to_goal.Magnitude;
			double powerB = pointBWorst_to_goal.Magnitude - pointBBest_to_goal.Magnitude;
			if (powerA < 0 || powerB < 0) {
				Error(" ERROR: One or more powers are negative");
				Error("power A = " + powerA);
				Error("power B = " + powerB);
			}

			//turn the powers into ratios so that we can use them to weight the rotations
			double sum = powerA + powerB;
			double ratioA = powerA / sum;
			double ratioB = powerB / sum;
			//Error("Ratio A = " + ratioA);
			//Error("Ratio B = " + ratioB);
			if (MathX.Abs(ratioA + ratioB - 1) > 0.0001) {
				Error("Ratios added together DO NOT EQUAL ONE!!!!!! ERROR: ratios added are: " + (ratioA + ratioB));
				Error("power A = " + powerA);
				Error("power B = " + powerB);
			}

			//calculate the average angle weighted by the importance to the score
			double averageAngle = (double)(angleA * ratioA + angleB * ratioB);
			doubleQ averageRotation = doubleQ.AxisAngleRad(Axis, averageAngle);

			//finally, actually evaluate what these rotations mean for our points.
			double3 finalpointA = averageRotation * pointAReal;
			double3 finalpointB = averageRotation * pointBReal;

			float3 finalpointA_global = avatarCreatorHand.LocalPointToGlobal((float3)finalpointA);
			float3 finalpointB_global = avatarCreatorHand.LocalPointToGlobal((float3)finalpointB);

			float myScore = (finalpointA_global - globalFingerTipRef1).Magnitude + (finalpointB_global - globalFingerTipRef2).Magnitude;
			float degreesAngleForPrint = (float)(averageAngle * (180f / Math.PI));
			Msg("Your score was::" + myScore + " with angle " + degreesAngleForPrint);
			Msg("First point was " + finalpointA_global + " Second point was " + finalpointB_global);

			avatarCreatorHand.LocalRotation = avatarCreatorHand.LocalRotation * (floatQ)averageRotation;
			/*
			// now the midpoint is lined up, we just need to rotate around vecToFingerTipMidpoint until the two points are best aligned
			// there's probably an analytic solution (feel free to PR such a solution) but iterative is good enough for a one-time thing
			int ITERS = 2000;
			float score = 0;
			float angleRotation = 0;
			float bestAngle = 0;
			float minScore = float.MaxValue;
			float3 bestpoint1 = float3.Zero;
			float3 bestpoint2 = float3.Zero;
			floatQ bestRotation = floatQ.Identity;
			floatQ baseLocalRotation = avatarCreatorHand.LocalRotation;
			//float3 localPosition = avatarCreatorHand.LocalPosition;
			//float3 globalPosition = avatarCreatorHand.Parent.LocalPointToGlobal(localPosition);
			//float3 globalScale = avatarCreatorHand.Parent.LocalScaleToGlobal(localScale);
			for (int i = 0; i < ITERS; i++) {
				angleRotation = 360f * (i / (float)(ITERS - 1));
				floatQ localRotation = baseLocalRotation * floatQ.AxisAngle(localAviTipRefsMidpoint, angleRotation);
				avatarCreatorHand.LocalRotation = localRotation;
				float3 point1 = avatarCreatorHand.LocalPointToGlobal(localAviCreatorTipRef1);
				float3 point2 = avatarCreatorHand.LocalPointToGlobal(localAviCreatorTipRef2);

				score = (point1 - globalFingerTipRef1).Magnitude + (point2 - globalFingerTipRef2).Magnitude;

				//Msg("Got score:" + score + " with angle " + angleRotation);
				//Msg("fingerTipRef1: " + globalFingerTipRef1);
				//Msg("avi tip ref  : " + avatarCreatorHand.LocalPointToGlobal(localAviCreatorTipRef1));
				//Msg("fingerTipRef1: " + globalFingerTipRef2);
				//Msg("avi tip ref  : " + avatarCreatorHand.LocalPointToGlobal(localAviCreatorTipRef2));
				if (score < minScore) {
					minScore = score;
					bestRotation = localRotation;
					bestAngle = angleRotation;
					bestpoint1 = point1;
					bestpoint2 = point2;
				}
			}
			Msg("Got best score:" + minScore + " with angle " + bestAngle);
			Msg("First point was " + bestpoint1 + " Second point was " + bestpoint2);
			if(myScore > minScore) {
				Warn("Score comparison: Iterative wins!");
			} else if (myScore < minScore) {
				Warn("Score comparison: Analytical wins!");
			} else {
				Warn("Score comparison: Its a tie!");
			}
			Warn("Iterative: " + minScore + " Analytical: " + myScore + " Marigin: " + (myScore - minScore));
			Warn("Distances: Point ONE = " + (bestpoint1 - finalpointA_global).Magnitude + " Point TWO = " + (bestpoint2 - finalpointB_global).Magnitude);
			avatarCreatorHand.LocalRotation = bestRotation;
			*/
		}


		static double VectorsToAngle(double3 Vec1, double3 Vec2, double3 Axis) {
			double dot = MathX.Dot(Vec1, Vec2);
			double magnitudes = Vec1.Magnitude * Vec2.Magnitude;
			double dotOverMagnitudes = dot / magnitudes;
			double angle = MathX.Acos(dotOverMagnitudes); //RADIANS

			//we define vec1 as UP, or Y
			double3 XVector = MathX.Cross(Vec1, Axis);
			double XDot = MathX.Dot(XVector, Vec2);
			//if the dot product is negative, the angle is obtuse
			//if the angle from the x axis is obtuse, the vector is in the negative X region
			//I don't know why I am inverting this, but it seems to give me the correct answer
			//It should be > instead of <, but... it works?
			double invert = (XDot < 0) ? 1 : -1;
			//Warn("VectorsToAngle: invert = " + invert + " angle = " + angle + " XDot = " + XDot);
			return angle * invert;
		}

		static double3 PointToVector(double3 point, double3 Axis) {
			//float3's with _to_ in their name are vectors
			//float3's without that are points
			double AxisSquared = Axis.SqrMagnitude;
			double origin_to_pointAReal_DOT_Axis = MathX.Dot(point, Axis);
			double scalarDistanceAlong_Axis = origin_to_pointAReal_DOT_Axis / AxisSquared;
			double3 pointARealClosestAxisPoint = scalarDistanceAlong_Axis * Axis;

			double3 pointAReal_to_Axis = pointARealClosestAxisPoint - point;

			return pointAReal_to_Axis;
		}

		static Slot[] GetFingerTips(BipedRig bipedRig, bool rightSide, out float3[] tipRefs, bool includeThumb) {
			BodyNode[][] fingerOrders = rightSide ? rightFingerOrders : leftFingerOrders;
			float3[] handTipRefs = rightSide ? relativeFingerPositionsRight : relativeFingerPositionsLeft;
			List<Slot> fingerTips = new List<Slot>();
			List<float3> tipRefsList = new List<float3>();
			for (int i = 0; i < fingerOrders.Length; i++) {
				//Msg("Finger order:" + fingerOrders[i]);
				BodyNode[] fingerOrder = fingerOrders[i];
				// todo: lookup positions for left hand
				if (!includeThumb && fingerOrder[0].ToString().ToLower().Contains("thumb")) {
					continue;
				}
				// get furthest part of finger available
				Slot fingerTip = null;
				foreach (BodyNode fingerPart in fingerOrder) {
					//Msg("Finger part:" + fingerPart);
					Slot curTip = bipedRig.TryGetBone(fingerPart);
					if (curTip != null) {
						fingerTip = curTip;
					}
				}
				if (fingerTip != null) {
					//Msg("Got tip for finger:" + fingerTip + " with i " + i);
					tipRefsList.Add(handTipRefs[i]);
					//Msg("set tip for finger:" + fingerTip);
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
