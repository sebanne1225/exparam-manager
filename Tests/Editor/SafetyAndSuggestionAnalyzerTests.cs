using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace Sebanne.ExParamManager.Editor.Tests
{
    public sealed class SafetyAndSuggestionAnalyzerTests
    {
        private readonly List<Object> _cleanup = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            for (int i = _cleanup.Count - 1; i >= 0; i--)
            {
                if (_cleanup[i] != null)
                {
                    Object.DestroyImmediate(_cleanup[i]);
                }
            }
            _cleanup.Clear();
        }

        private T Track<T>(T obj) where T : Object
        {
            _cleanup.Add(obj);
            return obj;
        }

        [Test]
        public void StrictIntRecommendation_RespectsSignedness_AnimatorMargin_AndWriterRequirement()
        {
            var root = Track(new GameObject("TestAvatar"));
            var desc = root.AddComponent<VRCAvatarDescriptor>();

            var expParams = Track(ScriptableObject.CreateInstance<VRCExpressionParameters>());
            expParams.parameters = new[]
            {
                // 0..2 with Greater 1 (-> margin 2) => Unsigned 2 bit (0..3) OK
                new VRCExpressionParameters.Parameter
                {
                    name = "UnsignedIntOk",
                    valueType = VRCExpressionParameters.ValueType.Int,
                    networkSynced = true,
                    bitWidth = 8,
                    flippedRepresentation = false,
                },
                // Signed 2 bit is -2..1, so value 2 requires 3 bit (-4..3)
                new VRCExpressionParameters.Parameter
                {
                    name = "SignedIntNeeds3Bit",
                    valueType = VRCExpressionParameters.ValueType.Int,
                    networkSynced = true,
                    bitWidth = 8,
                    flippedRepresentation = true,
                },
                // No menu writer => excluded from strict recommendation
                new VRCExpressionParameters.Parameter
                {
                    name = "NoWriterInt",
                    valueType = VRCExpressionParameters.ValueType.Int,
                    networkSynced = true,
                    bitWidth = 8,
                },
            };
            desc.expressionParameters = expParams;

            var menu = Track(ScriptableObject.CreateInstance<VRCExpressionsMenu>());
            menu.controls.Add(new VRCExpressionsMenu.Control
            {
                name = "SetUnsigned2",
                type = VRCExpressionsMenu.Control.ControlType.Toggle,
                parameter = new VRCExpressionsMenu.Control.Parameter { name = "UnsignedIntOk" },
                value = 2f,
            });
            menu.controls.Add(new VRCExpressionsMenu.Control
            {
                name = "SetSigned2",
                type = VRCExpressionsMenu.Control.ControlType.Toggle,
                parameter = new VRCExpressionsMenu.Control.Parameter { name = "SignedIntNeeds3Bit" },
                value = 2f,
            });
            desc.expressionsMenu = menu;

            var ctrl = Track(new AnimatorController());
            ctrl.AddLayer("Base");
            AnimatorStateMachine sm = ctrl.layers[0].stateMachine;
            AnimatorState s1 = sm.AddState("S1");
            AnimatorState s2 = sm.AddState("S2");
            var tr = s1.AddTransition(s2);
            tr.AddCondition(AnimatorConditionMode.Greater, 1f, "UnsignedIntOk");

            desc.customizeAnimationLayers = true;
            desc.baseAnimationLayers = new[]
            {
                new VRCAvatarDescriptor.CustomAnimLayer
                {
                    type = VRCAvatarDescriptor.AnimLayerType.FX,
                    animatorController = ctrl,
                },
            };

            AvatarScanResult scan = AvatarScanner.Scan(desc);

            ParameterSnapshot uSnap = scan.Parameters.Find(p => p.EffectiveName == "UnsignedIntOk");
            Assert.IsTrue(uSnap.Suggestion.HasStrictIntRecommendation);
            Assert.AreEqual(2, uSnap.Suggestion.RecommendedIntBitWidth);
            Assert.AreEqual(6, uSnap.Suggestion.IntBitSavings);

            ParameterSnapshot sSnap = scan.Parameters.Find(p => p.EffectiveName == "SignedIntNeeds3Bit");
            Assert.IsTrue(sSnap.Suggestion.HasStrictIntRecommendation);
            Assert.AreEqual(3, sSnap.Suggestion.RecommendedIntBitWidth);
            Assert.AreEqual(5, sSnap.Suggestion.IntBitSavings);

            ParameterSnapshot noWriter = scan.Parameters.Find(p => p.EffectiveName == "NoWriterInt");
            Assert.IsFalse(noWriter.Suggestion.HasStrictIntRecommendation);
            Assert.IsTrue((noWriter.ProtectionFlags & ProtectionFlags.ExternalInputSuspected) != 0);
        }

        [Test]
        public void ProtectionFlags_And_RadialFloatFilter_And_DefaultSort_WorkAsDesigned()
        {
            var root = Track(new GameObject("TestAvatar"));
            var desc = root.AddComponent<VRCAvatarDescriptor>();

            var expParams = Track(ScriptableObject.CreateInstance<VRCExpressionParameters>());
            expParams.parameters = new[]
            {
                new VRCExpressionParameters.Parameter { name = "VRCEmote", valueType = VRCExpressionParameters.ValueType.Int, networkSynced = true, bitWidth = 8 },
                new VRCExpressionParameters.Parameter { name = "QPro/Tongue", valueType = VRCExpressionParameters.ValueType.Float, networkSynced = true, bitWidth = 8 },
                new VRCExpressionParameters.Parameter { name = "Go/Float", valueType = VRCExpressionParameters.ValueType.Float, networkSynced = true, bitWidth = 8 },
                new VRCExpressionParameters.Parameter { name = "TailSize", valueType = VRCExpressionParameters.ValueType.Float, networkSynced = true, bitWidth = 8 },
                new VRCExpressionParameters.Parameter { name = "UnusedBool", valueType = VRCExpressionParameters.ValueType.Bool, networkSynced = true },
            };
            desc.expressionParameters = expParams;

            var menu = Track(ScriptableObject.CreateInstance<VRCExpressionsMenu>());
            menu.controls.Add(new VRCExpressionsMenu.Control
            {
                name = "TailRadial",
                type = VRCExpressionsMenu.Control.ControlType.RadialPuppet,
                subParameters = new[] { new VRCExpressionsMenu.Control.Parameter { name = "TailSize" } },
            });
            menu.controls.Add(new VRCExpressionsMenu.Control
            {
                name = "GoRadial",
                type = VRCExpressionsMenu.Control.ControlType.RadialPuppet,
                subParameters = new[] { new VRCExpressionsMenu.Control.Parameter { name = "Go/Float" } },
            });
            desc.expressionsMenu = menu;

            AvatarScanResult scan = AvatarScanner.Scan(desc);

            ParameterSnapshot emote = scan.Parameters.Find(p => p.EffectiveName == "VRCEmote");
            Assert.IsTrue((emote.ProtectionFlags & ProtectionFlags.SdkDefaultTemplate) != 0);

            ParameterSnapshot qpro = scan.Parameters.Find(p => p.EffectiveName == "QPro/Tongue");
            Assert.IsTrue((qpro.ProtectionFlags & ProtectionFlags.ExternalInputSuspected) != 0);

            ParameterSnapshot go = scan.Parameters.Find(p => p.EffectiveName == "Go/Float");
            Assert.IsTrue((go.ProtectionFlags & ProtectionFlags.ExternalInputSuspected) != 0);
            Assert.IsFalse(go.Suggestion.IsRadialFloatReferenceCandidate);

            ParameterSnapshot tail = scan.Parameters.Find(p => p.EffectiveName == "TailSize");
            Assert.IsTrue(tail.Suggestion.IsRadialFloatReferenceCandidate);
            Assert.AreEqual(4, tail.Suggestion.RadialFloat4BitSavings);
            Assert.AreEqual(3, tail.Suggestion.RadialFloat5BitSavings);

            ParameterSnapshot unused = scan.Parameters.Find(p => p.EffectiveName == "UnusedBool");
            Assert.IsTrue(unused.Suggestion.IsUnusedCandidate);

            List<ParameterSnapshot> sorted = ParameterSortHelper.Sort(scan.Parameters, ParameterSortMode.BitCostDescending);
            Assert.AreEqual(8, sorted[0].EffectiveBitCost);
            Assert.AreEqual("UnusedBool", sorted[sorted.Count - 1].EffectiveName);
        }
    }
}
