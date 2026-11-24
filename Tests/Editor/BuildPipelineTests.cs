using System.Collections.Generic;
using System.Linq;
using nadena.dev.ndmf;
using NUnit.Framework;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace Sebanne.ExParamManager.Editor.Tests
{
    public sealed class BuildPipelineTests
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
        public void Pass1AndPass2_SurvivesComponentRemoval_AppliesRemoveLocalResize_AndWritesBackAnimatorDefaults()
        {
            var root = Track(new GameObject("AvatarRoot"));
            var desc = root.AddComponent<VRCAvatarDescriptor>();

            var expParams = Track(ScriptableObject.CreateInstance<VRCExpressionParameters>());
            expParams.parameters = new[]
            {
                new VRCExpressionParameters.Parameter { name = "RemoveMe", valueType = VRCExpressionParameters.ValueType.Int, networkSynced = true, defaultValue = 3f, bitWidth = 8 },
                new VRCExpressionParameters.Parameter { name = "LocalMe", valueType = VRCExpressionParameters.ValueType.Bool, networkSynced = true },
                new VRCExpressionParameters.Parameter { name = "ResizeMe", valueType = VRCExpressionParameters.ValueType.Int, networkSynced = true, defaultValue = 10f, bitWidth = 8, flippedRepresentation = false },
                new VRCExpressionParameters.Parameter { name = "InternalSkip", valueType = VRCExpressionParameters.ValueType.Bool, networkSynced = true },
            };
            desc.expressionParameters = expParams;

            var ctrl = Track(new AnimatorController());
            ctrl.AddParameter("RemoveMe", AnimatorControllerParameterType.Int);
            desc.customizeAnimationLayers = true;
            desc.baseAnimationLayers = new[]
            {
                new VRCAvatarDescriptor.CustomAnimLayer { type = VRCAvatarDescriptor.AnimLayerType.FX, animatorController = ctrl },
            };

            var comp = root.AddComponent<ExParamManagerComponent>();
            comp.rules.Add(new ParameterRule { parameterName = "RemoveMe", action = ParameterAction.Remove, hideMenuControlsOnRemove = true });
            comp.rules.Add(new ParameterRule { parameterName = "LocalMe", action = ParameterAction.MakeLocal });
            comp.rules.Add(new ParameterRule { parameterName = "ResizeMe", action = ParameterAction.ResizeBitWidth, targetBitWidth = 2 }); // Unsigned 2bit max = 3
            comp.rules.Add(new ParameterRule { parameterName = "InternalSkip", action = ParameterAction.Remove, isInternalParameter = true });

            var ctx = new BuildContext(root, null, isClone: true);

            ExParamBuildProcessor.RunPass1(ctx);
            // Simulate INDMFEditorOnly strip before Pass 2
            Object.DestroyImmediate(comp);
            ExParamBuildProcessor.RunPass2(ctx);

            VRCExpressionParameters resultParams = desc.expressionParameters;
            Assert.IsFalse(resultParams.parameters.Any(p => p.name == "RemoveMe"));
            Assert.IsFalse(resultParams.parameters.First(p => p.name == "LocalMe").networkSynced);

            var resized = resultParams.parameters.First(p => p.name == "ResizeMe");
            Assert.AreEqual(2, resized.bitWidth);
            Assert.AreEqual(3f, resized.defaultValue); // clamped to 0..3

            // internalParameter rule is skipped in v1.0
            Assert.IsTrue(resultParams.parameters.Any(p => p.name == "InternalSkip"));

            // Animator defaultValue write-back
            Assert.AreEqual(3, ctrl.parameters.First(p => p.name == "RemoveMe").defaultInt);
        }

        [Test]
        public void Pass1_AbortsWithError_WhenMultipleComponentsExist()
        {
            var root = Track(new GameObject("AvatarRoot"));
            root.AddComponent<VRCAvatarDescriptor>();
            var child = new GameObject("Child");
            child.transform.SetParent(root.transform);

            root.AddComponent<ExParamManagerComponent>();
            child.AddComponent<ExParamManagerComponent>();

            var ctx = new BuildContext(root, null, isClone: true);
            ExParamBuildProcessor.RunPass1(ctx);

            ExParamBuildState state = ctx.GetState<ExParamBuildState>();
            Assert.IsTrue(state.AbortedByDuplicateComponents);
            Assert.IsFalse(state.ShouldRun);
            Assert.IsTrue(state.RecordedReports.Any(r => r.Severity == ErrorSeverity.Error));
        }

        [Test]
        public void Pass2_MenuRemoval_PrunesOnlyNewlyEmptiedSubMenus_AndWarnsBrokenMultiAxis()
        {
            var root = Track(new GameObject("AvatarRoot"));
            var desc = root.AddComponent<VRCAvatarDescriptor>();

            var expParams = Track(ScriptableObject.CreateInstance<VRCExpressionParameters>());
            expParams.parameters = new[]
            {
                new VRCExpressionParameters.Parameter { name = "KillA", valueType = VRCExpressionParameters.ValueType.Bool, networkSynced = true },
                new VRCExpressionParameters.Parameter { name = "KeepB", valueType = VRCExpressionParameters.ValueType.Float, networkSynced = true },
            };
            desc.expressionParameters = expParams;

            var rootMenu = Track(ScriptableObject.CreateInstance<VRCExpressionsMenu>());
            var newlyEmptySub = Track(ScriptableObject.CreateInstance<VRCExpressionsMenu>());
            var originallyEmptySub = Track(ScriptableObject.CreateInstance<VRCExpressionsMenu>());
            rootMenu.name = "Root";
            newlyEmptySub.name = "NewlyEmpty";
            originallyEmptySub.name = "OriginallyEmpty";

            newlyEmptySub.controls.Add(new VRCExpressionsMenu.Control
            {
                name = "ToggleKillA",
                type = VRCExpressionsMenu.Control.ControlType.Toggle,
                parameter = new VRCExpressionsMenu.Control.Parameter { name = "KillA" },
                value = 1f,
            });

            rootMenu.controls.Add(new VRCExpressionsMenu.Control
            {
                name = "SubNewlyEmpty",
                type = VRCExpressionsMenu.Control.ControlType.SubMenu,
                subMenu = newlyEmptySub,
            });
            rootMenu.controls.Add(new VRCExpressionsMenu.Control
            {
                name = "SubOrigEmpty",
                type = VRCExpressionsMenu.Control.ControlType.SubMenu,
                subMenu = originallyEmptySub,
            });
            rootMenu.controls.Add(new VRCExpressionsMenu.Control
            {
                name = "TwoAxis",
                type = VRCExpressionsMenu.Control.ControlType.TwoAxisPuppet,
                subParameters = new[]
                {
                    new VRCExpressionsMenu.Control.Parameter { name = "KillA" },
                    new VRCExpressionsMenu.Control.Parameter { name = "KeepB" },
                },
            });
            desc.expressionsMenu = rootMenu;

            var comp = root.AddComponent<ExParamManagerComponent>();
            comp.pruneEmptySubMenus = true;
            comp.rules.Add(new ParameterRule { parameterName = "KillA", action = ParameterAction.Remove, hideMenuControlsOnRemove = true });

            var ctx = new BuildContext(root, null, isClone: true);
            ExParamBuildProcessor.RunPass1(ctx);
            ExParamBuildProcessor.RunPass2(ctx);

            Assert.IsFalse(desc.expressionsMenu.controls.Any(c => c.name == "SubNewlyEmpty"));
            Assert.IsTrue(desc.expressionsMenu.controls.Any(c => c.name == "SubOrigEmpty"));

            var twoAxis = desc.expressionsMenu.controls.First(c => c.name == "TwoAxis");
            Assert.AreEqual(string.Empty, twoAxis.subParameters[0].name);
            Assert.AreEqual("KeepB", twoAxis.subParameters[1].name);
            Assert.IsTrue(ctx.GetState<ExParamBuildState>().RecordedReports.Any(r => r.Severity == ErrorSeverity.NonFatal));
        }

        [Test]
        public void BitBarProjection_TreatsResizeAsZeroSavingsOnOldSdk()
        {
            var root = Track(new GameObject("AvatarRoot"));
            var desc = root.AddComponent<VRCAvatarDescriptor>();
            var expParams = Track(ScriptableObject.CreateInstance<VRCExpressionParameters>());
            expParams.parameters = new[]
            {
                new VRCExpressionParameters.Parameter { name = "IntParam", valueType = VRCExpressionParameters.ValueType.Int, networkSynced = true, bitWidth = 8 },
            };
            desc.expressionParameters = expParams;

            var comp = root.AddComponent<ExParamManagerComponent>();
            comp.rules.Add(new ParameterRule { parameterName = "IntParam", action = ParameterAction.ResizeBitWidth, targetBitWidth = 3 });

            AvatarScanResult scan = AvatarScanner.Scan(desc);

            BitProjection sdk3106 = BitBarDrawer.CalculateProjection(scan, comp, allowBitWidthResize: true);
            Assert.AreEqual(8, sdk3106.BeforeBits);
            Assert.AreEqual(3, sdk3106.AfterBits);
            Assert.AreEqual(5, sdk3106.SavedBits);

            BitProjection sdk3105 = BitBarDrawer.CalculateProjection(scan, comp, allowBitWidthResize: false);
            Assert.AreEqual(8, sdk3105.BeforeBits);
            Assert.AreEqual(8, sdk3105.AfterBits);
            Assert.AreEqual(0, sdk3105.SavedBits);
            Assert.AreEqual(1, sdk3105.SkippedResizeOnOldSdkCount);
        }
    }
}
