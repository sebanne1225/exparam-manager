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
                new VRCExpressionParameters.Parameter { name = "RemoveMe", valueType = VRCExpressionParameters.ValueType.Int, networkSynced = true, defaultValue = 3f },
                new VRCExpressionParameters.Parameter { name = "LocalMe", valueType = VRCExpressionParameters.ValueType.Bool, networkSynced = true },
                new VRCExpressionParameters.Parameter { name = "ResizeMe", valueType = VRCExpressionParameters.ValueType.Int, networkSynced = true, defaultValue = 10f },
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
#if HAS_VRCSDK3_AVATARS_3_10_6
            Assert.AreEqual(2, resized.bitWidth);
            Assert.AreEqual(3f, resized.defaultValue); // clamped to 0..3
#else
            Assert.AreEqual(10f, resized.defaultValue);
            Assert.IsTrue(ctx.GetState<ExParamBuildState>().RecordedReports.Any(r => r.Severity == ErrorSeverity.Information));
#endif

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
                new VRCExpressionParameters.Parameter { name = "IntParam", valueType = VRCExpressionParameters.ValueType.Int, networkSynced = true },
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

        [Test]
        public void Pass2_DoesNotCloneUnmodifiedMenu_AndHandlesCircularMenuRemoval()
        {
            var root = Track(new GameObject("AvatarRoot"));
            var desc = root.AddComponent<VRCAvatarDescriptor>();

            var expParams = Track(ScriptableObject.CreateInstance<VRCExpressionParameters>());
            expParams.parameters = new[]
            {
                new VRCExpressionParameters.Parameter { name = "RemoveP", valueType = VRCExpressionParameters.ValueType.Bool, networkSynced = true },
                new VRCExpressionParameters.Parameter { name = "OtherP", valueType = VRCExpressionParameters.ValueType.Bool, networkSynced = true },
            };
            desc.expressionParameters = expParams;

            // Unmodified menu case
            var untouchedMenu = Track(ScriptableObject.CreateInstance<VRCExpressionsMenu>());
            untouchedMenu.controls.Add(new VRCExpressionsMenu.Control
            {
                name = "KeepOther",
                type = VRCExpressionsMenu.Control.ControlType.Toggle,
                parameter = new VRCExpressionsMenu.Control.Parameter { name = "OtherP" },
            });
            desc.expressionsMenu = untouchedMenu;

            var comp = root.AddComponent<ExParamManagerComponent>();
            comp.rules.Add(new ParameterRule { parameterName = "RemoveP", action = ParameterAction.Remove, hideMenuControlsOnRemove = true });

            var ctx1 = new BuildContext(root, null, isClone: true);
            ExParamBuildProcessor.RunPass1(ctx1);
            ExParamBuildProcessor.RunPass2(ctx1);
            Assert.AreSame(untouchedMenu, desc.expressionsMenu);

            // Circular menu removal case
            var menuA = Track(ScriptableObject.CreateInstance<VRCExpressionsMenu>());
            var menuB = Track(ScriptableObject.CreateInstance<VRCExpressionsMenu>());
            menuA.controls.Add(new VRCExpressionsMenu.Control { name = "SubB", type = VRCExpressionsMenu.Control.ControlType.SubMenu, subMenu = menuB });
            menuB.controls.Add(new VRCExpressionsMenu.Control { name = "SubA", type = VRCExpressionsMenu.Control.ControlType.SubMenu, subMenu = menuA });
            menuB.controls.Add(new VRCExpressionsMenu.Control
            {
                name = "KillToggle",
                type = VRCExpressionsMenu.Control.ControlType.Toggle,
                parameter = new VRCExpressionsMenu.Control.Parameter { name = "RemoveP" },
            });
            desc.expressionsMenu = menuA;
            expParams.parameters = new[]
            {
                new VRCExpressionParameters.Parameter { name = "RemoveP", valueType = VRCExpressionParameters.ValueType.Bool, networkSynced = true },
            };

            var ctx2 = new BuildContext(root, null, isClone: true);
            ExParamBuildProcessor.RunPass1(ctx2);
            ExParamBuildProcessor.RunPass2(ctx2);
            Assert.IsFalse(menuB.controls.Any(c => c.name == "KillToggle"));
        }

        [Test]
        public void Pass2_Reports256BitOverflow_WhenAfterBitsExceedsLimit()
        {
            var root = Track(new GameObject("AvatarRoot"));
            var desc = root.AddComponent<VRCAvatarDescriptor>();

            var list = new List<VRCExpressionParameters.Parameter>();
            for (int i = 0; i < 33; i++) // 33 * 8 = 264 > 256
            {
                list.Add(new VRCExpressionParameters.Parameter
                {
                    name = "Int_" + i,
                    valueType = VRCExpressionParameters.ValueType.Int,
                    networkSynced = true,
                });
            }
            var expParams = Track(ScriptableObject.CreateInstance<VRCExpressionParameters>());
            expParams.parameters = list.ToArray();
            desc.expressionParameters = expParams;

            var comp = root.AddComponent<ExParamManagerComponent>();
            var ctx = new BuildContext(root, null, isClone: true);
            ExParamBuildProcessor.RunPass1(ctx);
            ExParamBuildProcessor.RunPass2(ctx);

            Assert.IsTrue(ctx.GetState<ExParamBuildState>().RecordedReports.Any(
                r => (r.Severity == ErrorSeverity.Error || r.Severity == ErrorSeverity.NonFatal)
                     && r.Title.Contains("256")));
        }

        [Test]
        public void Pass2_ProtectsPersistentAssets_LeavesOriginalAssetsUnchanged()
        {
            if (!Application.dataPath.Contains("ToolDevWorkspace"))
            {
                Assert.Pass("Persistent temp-asset test runs only in ToolDevWorkspace.");
                return;
            }

            const string tempFolder = "Assets/_ExParamTestTemp_AutoDelete";
            if (!UnityEditor.AssetDatabase.IsValidFolder(tempFolder))
            {
                UnityEditor.AssetDatabase.CreateFolder("Assets", "_ExParamTestTemp_AutoDelete");
            }

            try
            {
                var origParams = ScriptableObject.CreateInstance<VRCExpressionParameters>();
                origParams.parameters = new[]
                {
                    new VRCExpressionParameters.Parameter { name = "TargetP", valueType = VRCExpressionParameters.ValueType.Int, networkSynced = true, defaultValue = 4f },
                    new VRCExpressionParameters.Parameter { name = "KeepP", valueType = VRCExpressionParameters.ValueType.Bool, networkSynced = true },
                };
                UnityEditor.AssetDatabase.CreateAsset(origParams, tempFolder + "/OrigParams.asset");

                var origMenu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
                origMenu.controls.Add(new VRCExpressionsMenu.Control
                {
                    name = "TargetToggle",
                    type = VRCExpressionsMenu.Control.ControlType.Toggle,
                    parameter = new VRCExpressionsMenu.Control.Parameter { name = "TargetP" },
                });
                UnityEditor.AssetDatabase.CreateAsset(origMenu, tempFolder + "/OrigMenu.asset");

                var origCtrl = AnimatorController.CreateAnimatorControllerAtPath(tempFolder + "/OrigCtrl.controller");
                origCtrl.AddParameter("TargetP", AnimatorControllerParameterType.Int);
                UnityEditor.AssetDatabase.SaveAssets();

                var root = Track(new GameObject("AvatarRoot"));
                var desc = root.AddComponent<VRCAvatarDescriptor>();
                desc.expressionParameters = origParams;
                desc.expressionsMenu = origMenu;
                desc.customizeAnimationLayers = true;
                desc.baseAnimationLayers = new[]
                {
                    new VRCAvatarDescriptor.CustomAnimLayer { type = VRCAvatarDescriptor.AnimLayerType.FX, animatorController = origCtrl },
                };

                var comp = root.AddComponent<ExParamManagerComponent>();
                comp.rules.Add(new ParameterRule { parameterName = "TargetP", action = ParameterAction.Remove, hideMenuControlsOnRemove = true });

                var ctx = new BuildContext(root, null, isClone: true);
                ExParamBuildProcessor.RunPass1(ctx);
                ExParamBuildProcessor.RunPass2(ctx);

                // Replaced with clones
                Assert.AreNotSame(origParams, desc.expressionParameters);
                Assert.AreNotSame(origMenu, desc.expressionsMenu);
                Assert.AreNotSame(origCtrl, desc.baseAnimationLayers[0].animatorController);

                // Originals remain completely untouched
                Assert.AreEqual(2, origParams.parameters.Length);
                Assert.AreEqual(1, origMenu.controls.Count);
                Assert.AreEqual(0, origCtrl.parameters[0].defaultInt);
            }
            finally
            {
                UnityEditor.AssetDatabase.DeleteAsset(tempFolder);
                UnityEditor.AssetDatabase.Refresh();
            }
        }

        [Test]
        public void Pass1AndPass2_SkipsDisabledRules_AndKeepsParameterIntact()
        {
            var root = Track(new GameObject("AvatarRoot"));
            var desc = root.AddComponent<VRCAvatarDescriptor>();

            var expParams = Track(ScriptableObject.CreateInstance<VRCExpressionParameters>());
            expParams.parameters = new[]
            {
                new VRCExpressionParameters.Parameter { name = "ActiveRemove", valueType = VRCExpressionParameters.ValueType.Bool, networkSynced = true },
                new VRCExpressionParameters.Parameter { name = "PausedRemove", valueType = VRCExpressionParameters.ValueType.Int, networkSynced = true },
            };
            desc.expressionParameters = expParams;

            var comp = root.AddComponent<ExParamManagerComponent>();
            comp.rules.Add(new ParameterRule { parameterName = "ActiveRemove", action = ParameterAction.Remove, disabled = false });
            comp.rules.Add(new ParameterRule { parameterName = "PausedRemove", action = ParameterAction.Remove, disabled = true });

            var ctx = new BuildContext(root, null, isClone: true);
            ExParamBuildProcessor.RunPass1(ctx);
            ExParamBuildProcessor.RunPass2(ctx);

            VRCExpressionParameters resultParams = desc.expressionParameters;
            Assert.IsFalse(resultParams.parameters.Any(p => p.name == "ActiveRemove"));
            Assert.IsTrue(resultParams.parameters.Any(p => p.name == "PausedRemove"));
        }

        [Test]
        public void Pass1AndPass2_SkipsAllRules_WhenEnabledOnBuildIsFalse()
        {
            var root = Track(new GameObject("AvatarRoot"));
            var desc = root.AddComponent<VRCAvatarDescriptor>();

            var expParams = Track(ScriptableObject.CreateInstance<VRCExpressionParameters>());
            expParams.parameters = new[]
            {
                new VRCExpressionParameters.Parameter { name = "KeepByGlobalOff1", valueType = VRCExpressionParameters.ValueType.Bool, networkSynced = true },
                new VRCExpressionParameters.Parameter { name = "KeepByGlobalOff2", valueType = VRCExpressionParameters.ValueType.Int, networkSynced = true },
            };
            desc.expressionParameters = expParams;

            var comp = root.AddComponent<ExParamManagerComponent>();
            comp.enabledOnBuild = false;
            comp.rules.Add(new ParameterRule { parameterName = "KeepByGlobalOff1", action = ParameterAction.Remove });
            comp.rules.Add(new ParameterRule { parameterName = "KeepByGlobalOff2", action = ParameterAction.MakeLocal });

            var ctx = new BuildContext(root, null, isClone: true);
            ExParamBuildProcessor.RunPass1(ctx);
            ExParamBuildProcessor.RunPass2(ctx);

            VRCExpressionParameters resultParams = desc.expressionParameters;
            Assert.IsTrue(resultParams.parameters.Any(p => p.name == "KeepByGlobalOff1"));
            Assert.IsTrue(resultParams.parameters.First(p => p.name == "KeepByGlobalOff2").networkSynced);
        }

        [Test]
        public void BitBarProjection_ExcludesDisabledRules_AndGlobalDisabledState()
        {
            var root = Track(new GameObject("AvatarRoot"));
            var desc = root.AddComponent<VRCAvatarDescriptor>();
            var expParams = Track(ScriptableObject.CreateInstance<VRCExpressionParameters>());
            expParams.parameters = new[]
            {
                new VRCExpressionParameters.Parameter { name = "ActiveBool", valueType = VRCExpressionParameters.ValueType.Bool, networkSynced = true },
                new VRCExpressionParameters.Parameter { name = "PausedInt", valueType = VRCExpressionParameters.ValueType.Int, networkSynced = true },
            };
            desc.expressionParameters = expParams;

            var comp = root.AddComponent<ExParamManagerComponent>();
            comp.rules.Add(new ParameterRule { parameterName = "ActiveBool", action = ParameterAction.Remove, disabled = false });
            comp.rules.Add(new ParameterRule { parameterName = "PausedInt", action = ParameterAction.MakeLocal, disabled = true });

            AvatarScanResult scan = AvatarScanner.Scan(desc);

            BitProjection partial = BitBarDrawer.CalculateProjection(scan, comp, allowBitWidthResize: true);
            Assert.AreEqual(9, partial.BeforeBits);
            Assert.AreEqual(8, partial.AfterBits);
            Assert.AreEqual(1, partial.SavedBits);
            Assert.AreEqual(1, partial.RemoveRuleCount);
            Assert.AreEqual(0, partial.MakeLocalRuleCount);
            Assert.AreEqual(1, partial.DisabledRuleCount);
            Assert.IsFalse(partial.IsGloballyDisabled);

            comp.enabledOnBuild = false;
            BitProjection globalOff = BitBarDrawer.CalculateProjection(scan, comp, allowBitWidthResize: true);
            Assert.AreEqual(9, globalOff.BeforeBits);
            Assert.AreEqual(9, globalOff.AfterBits);
            Assert.AreEqual(0, globalOff.SavedBits);
            Assert.AreEqual(1, globalOff.DisabledRuleCount);
            Assert.IsTrue(globalOff.IsGloballyDisabled);
        }
    }
}
