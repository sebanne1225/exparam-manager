using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDK3.Dynamics.Contact.Components;
using VRC.SDK3.Dynamics.PhysBone.Components;

namespace Sebanne.ExParamManager.Editor.Tests
{
    public sealed class SnapshotAndReferenceScannerTests
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
        public void Collect_ReadsSdk3106BitWidthAndFlippedRepresentation_AndBuildsPrefabGroups()
        {
            var root = Track(new GameObject("TestAvatar"));
            var desc = root.AddComponent<VRCAvatarDescriptor>();

            var expParams = Track(ScriptableObject.CreateInstance<VRCExpressionParameters>());
            expParams.parameters = new[]
            {
                new VRCExpressionParameters.Parameter
                {
                    name = "SignedInt",
                    valueType = VRCExpressionParameters.ValueType.Int,
                    networkSynced = true,
                    defaultValue = -1f,
                    bitWidth = 3,
                    flippedRepresentation = true,
                },
                new VRCExpressionParameters.Parameter
                {
                    name = "RadialFloat",
                    valueType = VRCExpressionParameters.ValueType.Float,
                    networkSynced = true,
                    bitWidth = 0, // 0 は 8 bit 扱い
                    flippedRepresentation = false,
                },
            };
            desc.expressionParameters = expParams;

            AvatarScanResult scan = AvatarScanner.Scan(desc);

            Assert.AreEqual(2, scan.Parameters.Count);
            Assert.AreEqual(11, scan.TotalSyncedBits); // 3 + 8
            Assert.GreaterOrEqual(scan.PrefabGroups.Count, 1);

            ParameterSnapshot intSnap = scan.Parameters.Find(p => p.EffectiveName == "SignedInt");
            Assert.IsNotNull(intSnap);
            Assert.AreEqual(3, intSnap.CurrentBitWidth);
            Assert.AreEqual(ParameterSignednessKind.Signed, intSnap.Signedness);

            ParameterSnapshot floatSnap = scan.Parameters.Find(p => p.EffectiveName == "RadialFloat");
            Assert.IsNotNull(floatSnap);
            Assert.AreEqual(8, floatSnap.CurrentBitWidth);
            Assert.AreEqual(ParameterSignednessKind.Signed, floatSnap.Signedness);
        }

        [Test]
        public void ReferenceScanner_HandlesCyclicMenus_AndMaxDepth()
        {
            var root = Track(new GameObject("TestAvatar"));
            var desc = root.AddComponent<VRCAvatarDescriptor>();

            var expParams = Track(ScriptableObject.CreateInstance<VRCExpressionParameters>());
            expParams.parameters = new[]
            {
                new VRCExpressionParameters.Parameter
                {
                    name = "LoopParam",
                    valueType = VRCExpressionParameters.ValueType.Bool,
                    networkSynced = true,
                },
            };
            desc.expressionParameters = expParams;

            var menuA = Track(ScriptableObject.CreateInstance<VRCExpressionsMenu>());
            var menuB = Track(ScriptableObject.CreateInstance<VRCExpressionsMenu>());
            menuA.name = "MenuA";
            menuB.name = "MenuB";

            menuA.controls.Add(new VRCExpressionsMenu.Control
            {
                name = "GoB",
                type = VRCExpressionsMenu.Control.ControlType.SubMenu,
                subMenu = menuB,
            });
            menuB.controls.Add(new VRCExpressionsMenu.Control
            {
                name = "BackToA",
                type = VRCExpressionsMenu.Control.ControlType.SubMenu,
                subMenu = menuA,
            });
            menuB.controls.Add(new VRCExpressionsMenu.Control
            {
                name = "ToggleLoop",
                type = VRCExpressionsMenu.Control.ControlType.Toggle,
                parameter = new VRCExpressionsMenu.Control.Parameter { name = "LoopParam" },
                value = 1f,
            });
            desc.expressionsMenu = menuA;

            AvatarScanResult scan = AvatarScanner.Scan(desc);
            ParameterSnapshot snap = scan.Parameters.Find(p => p.EffectiveName == "LoopParam");
            Assert.IsNotNull(snap);
            Assert.IsTrue(snap.HasMenuWriter);
            Assert.AreEqual(1, snap.References.Count);
        }

        [Test]
        public void ReferenceScanner_ScansAnimator5Places_IgnoresInactiveStateProps_AndScansDriverCopySource()
        {
            var root = Track(new GameObject("TestAvatar"));
            var desc = root.AddComponent<VRCAvatarDescriptor>();

            var expParams = Track(ScriptableObject.CreateInstance<VRCExpressionParameters>());
            expParams.parameters = new[]
            {
                new VRCExpressionParameters.Parameter { name = "TransParam", valueType = VRCExpressionParameters.ValueType.Int, networkSynced = true },
                new VRCExpressionParameters.Parameter { name = "AnyParam", valueType = VRCExpressionParameters.ValueType.Bool, networkSynced = true },
                new VRCExpressionParameters.Parameter { name = "BlendParam", valueType = VRCExpressionParameters.ValueType.Float, networkSynced = true },
                new VRCExpressionParameters.Parameter { name = "ActiveSpeed", valueType = VRCExpressionParameters.ValueType.Float, networkSynced = true },
                new VRCExpressionParameters.Parameter { name = "GhostSpeed", valueType = VRCExpressionParameters.ValueType.Float, networkSynced = true },
                new VRCExpressionParameters.Parameter { name = "DriverDest", valueType = VRCExpressionParameters.ValueType.Int, networkSynced = true },
                new VRCExpressionParameters.Parameter { name = "DriverCopySrc", valueType = VRCExpressionParameters.ValueType.Int, networkSynced = true },
            };
            desc.expressionParameters = expParams;

            var ctrl = Track(new AnimatorController());
            ctrl.name = "FX";
            ctrl.AddLayer("Base");
            AnimatorStateMachine sm = ctrl.layers[0].stateMachine;
            AnimatorState s1 = sm.AddState("S1");
            AnimatorState s2 = sm.AddState("S2");

            // 1. Transition condition
            var tr = s1.AddTransition(s2);
            tr.AddCondition(AnimatorConditionMode.Equals, 2f, "TransParam");

            // 2. AnyState condition
            var anyTr = sm.AddAnyStateTransition(s2);
            anyTr.AddCondition(AnimatorConditionMode.If, 0f, "AnyParam");

            // 3. BlendTree
            var bt = Track(new BlendTree());
            bt.blendType = BlendTreeType.Simple1D;
            bt.blendParameter = "BlendParam";
            s1.motion = bt;

            // 4. State properties: ActiveSpeed (active=true) vs GhostSpeed (active=false)
            s1.speedParameterActive = true;
            s1.speedParameter = "ActiveSpeed";
            s2.speedParameterActive = false;
            s2.speedParameter = "GhostSpeed";

            // 5. VRCAvatarParameterDriver (Write & Copy Source)
            var driver = s1.AddStateMachineBehaviour<VRCAvatarParameterDriver>();
            driver.parameters.Add(new VRC.SDKBase.VRC_AvatarParameterDriver.Parameter
            {
                type = VRC.SDKBase.VRC_AvatarParameterDriver.ChangeType.Copy,
                source = "DriverCopySrc",
                name = "DriverDest",
            });

            desc.customizeAnimationLayers = true;
            desc.baseAnimationLayers = new[]
            {
                new VRCAvatarDescriptor.CustomAnimLayer
                {
                    type = VRCAvatarDescriptor.AnimLayerType.FX,
                    isEnabled = true,
                    isDefault = false,
                    animatorController = ctrl,
                },
            };

            AvatarScanResult scan = AvatarScanner.Scan(desc);

            Assert.GreaterOrEqual(scan.Parameters.Find(p => p.EffectiveName == "TransParam").References.Count, 1);
            Assert.GreaterOrEqual(scan.Parameters.Find(p => p.EffectiveName == "AnyParam").References.Count, 1);
            Assert.GreaterOrEqual(scan.Parameters.Find(p => p.EffectiveName == "BlendParam").References.Count, 1);
            Assert.GreaterOrEqual(scan.Parameters.Find(p => p.EffectiveName == "ActiveSpeed").References.Count, 1);
            Assert.AreEqual(0, scan.Parameters.Find(p => p.EffectiveName == "GhostSpeed").References.Count);

            ParameterSnapshot destSnap = scan.Parameters.Find(p => p.EffectiveName == "DriverDest");
            Assert.IsTrue(destSnap.HasParameterDriverWrite);
            ParameterSnapshot srcSnap = scan.Parameters.Find(p => p.EffectiveName == "DriverCopySrc");
            Assert.IsTrue(srcSnap.HasParameterDriverRead);
        }

        [Test]
        public void ReferenceScanner_PhysBoneMatchesExact5SuffixesOnly_AndDetectsContactReceiver()
        {
            var root = Track(new GameObject("TestAvatar"));
            var desc = root.AddComponent<VRCAvatarDescriptor>();

            var expParams = Track(ScriptableObject.CreateInstance<VRCExpressionParameters>());
            expParams.parameters = new[]
            {
                new VRCExpressionParameters.Parameter { name = "Ear_Angle", valueType = VRCExpressionParameters.ValueType.Float, networkSynced = true },
                new VRCExpressionParameters.Parameter { name = "Ear_CustomOther", valueType = VRCExpressionParameters.ValueType.Float, networkSynced = true },
                new VRCExpressionParameters.Parameter { name = "HeadPat", valueType = VRCExpressionParameters.ValueType.Bool, networkSynced = true },
            };
            desc.expressionParameters = expParams;

            var pb = root.AddComponent<VRCPhysBone>();
            pb.parameter = "Ear";

            var cr = root.AddComponent<VRCContactReceiver>();
            cr.parameter = "HeadPat";

            AvatarScanResult scan = AvatarScanner.Scan(desc);

            Assert.IsTrue(scan.Parameters.Find(p => p.EffectiveName == "Ear_Angle").HasPhysBoneOrContact);
            Assert.IsFalse(scan.Parameters.Find(p => p.EffectiveName == "Ear_CustomOther").HasPhysBoneOrContact);
            Assert.IsTrue(scan.Parameters.Find(p => p.EffectiveName == "HeadPat").HasPhysBoneOrContact);
        }
    }
}
