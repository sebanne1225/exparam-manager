using System.Collections.Generic;
using System.Linq;
using nadena.dev.ndmf;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDKBase;

namespace Sebanne.ExParamManager.Editor.Tests
{
    public sealed class RealAvatarEndToEndTests
    {
        private readonly List<Object> _cleanup = new List<Object>();
        private readonly List<string> _generatedAssetPaths = new List<string>();

        [TearDown]
        public void TearDown()
        {
            for (int i = _cleanup.Count - 1; i >= 0; i--)
            {
                Object obj = _cleanup[i];
                if (obj != null && !EditorUtility.IsPersistent(obj) && !AssetDatabase.Contains(obj))
                {
                    Object.DestroyImmediate(obj);
                }
            }
            _cleanup.Clear();

            for (int i = _generatedAssetPaths.Count - 1; i >= 0; i--)
            {
                string path = _generatedAssetPaths[i];
                if (!string.IsNullOrEmpty(path) && AssetDatabase.LoadMainAssetAtPath(path) != null)
                {
                    AssetDatabase.DeleteAsset(path);
                }
            }
            _generatedAssetPaths.Clear();
        }

        private T Track<T>(T obj) where T : Object
        {
            _cleanup.Add(obj);
            return obj;
        }

        private void TrackGeneratedAssetIfTemporary(Object asset)
        {
            if (asset == null)
            {
                return;
            }

            string path = AssetDatabase.GetAssetPath(asset);
            if (!string.IsNullOrEmpty(path) && (path.Contains("__Generated") || path.Contains("ZZZ_GeneratedAssets")))
            {
                _generatedAssetPaths.Add(path);
            }
        }

        [Test]
        public void FullNdmfPipeline_ProcessesSyntheticAvatarEndToEnd()
        {
            var avatar = Track(new GameObject("SyntheticAvatar"));
            var desc = avatar.AddComponent<VRCAvatarDescriptor>();

            var expParams = Track(ScriptableObject.CreateInstance<VRCExpressionParameters>());
            expParams.parameters = new[]
            {
                new VRCExpressionParameters.Parameter { name = "UnusedBool", valueType = VRCExpressionParameters.ValueType.Bool, networkSynced = true },
                new VRCExpressionParameters.Parameter { name = "LocalBool", valueType = VRCExpressionParameters.ValueType.Bool, networkSynced = true },
                new VRCExpressionParameters.Parameter { name = "SmallInt", valueType = VRCExpressionParameters.ValueType.Int, networkSynced = true },
            };
            desc.expressionParameters = expParams;

            var menu = Track(ScriptableObject.CreateInstance<VRCExpressionsMenu>());
            menu.controls.Add(new VRCExpressionsMenu.Control
            {
                name = "UnusedToggle",
                type = VRCExpressionsMenu.Control.ControlType.Toggle,
                parameter = new VRCExpressionsMenu.Control.Parameter { name = "UnusedBool" },
                value = 1f,
            });
            desc.expressionsMenu = menu;

            var ctrl = Track(new AnimatorController());
            ctrl.AddLayer("Base");
            ctrl.AddParameter("UnusedBool", AnimatorControllerParameterType.Bool);
            desc.customizeAnimationLayers = true;
            desc.baseAnimationLayers = new[]
            {
                new VRCAvatarDescriptor.CustomAnimLayer { type = VRCAvatarDescriptor.AnimLayerType.FX, animatorController = ctrl },
            };
            desc.specialAnimationLayers = new VRCAvatarDescriptor.CustomAnimLayer[0];

            var comp = avatar.AddComponent<ExParamManagerComponent>();
            comp.rules.Add(new ParameterRule { parameterName = "UnusedBool", action = ParameterAction.Remove, hideMenuControlsOnRemove = true });
            comp.rules.Add(new ParameterRule { parameterName = "LocalBool", action = ParameterAction.MakeLocal });
            comp.rules.Add(new ParameterRule { parameterName = "SmallInt", action = ParameterAction.ResizeBitWidth, targetBitWidth = 2 });

            AvatarProcessor.ProcessAvatar(avatar);

            TrackGeneratedAssetIfTemporary(desc.expressionParameters);
            TrackGeneratedAssetIfTemporary(desc.expressionsMenu);

            // AvatarProcessor.ProcessAvatar runs NDMF phases only; VRChat SDK's RemoveAvatarEditorOnly (-1024) strips IEditorOnly components.
            ExParamManagerComponent remainingComp = avatar.GetComponent<ExParamManagerComponent>();
            if (remainingComp != null)
            {
                Assert.IsTrue(remainingComp is IEditorOnly);
            }

            VRCExpressionParameters builtParams = desc.expressionParameters;
            Assert.IsFalse(builtParams.parameters.Any(p => p.name == "UnusedBool"));
            Assert.IsFalse(builtParams.parameters.First(p => p.name == "LocalBool").networkSynced);
            Assert.AreEqual(0, desc.expressionsMenu.controls.Count);

#if HAS_VRCSDK3_AVATARS_3_10_6
            Assert.AreEqual(2, builtParams.parameters.First(p => p.name == "SmallInt").bitWidth);
            Assert.AreEqual(2, builtParams.CalcTotalCost());
#else
            Assert.AreEqual(8, builtParams.CalcTotalCost());
#endif
        }

        [Test]
        public void FullNdmfPipeline_ProcessesRealRirikaPrefabClone_WithoutMutatingOriginal()
        {
            const string prefabPath = "Assets/_TestAvatars/IKUSIA/ririka/Prefab/ririka.prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                Assert.Pass("Real test prefab not present in this project.");
                return;
            }

            var origDesc = prefab.GetComponent<VRCAvatarDescriptor>();
            Assert.IsNotNull(origDesc);
            VRCExpressionParameters origParamsAsset = origDesc.expressionParameters;
            int origParamCount = origParamsAsset.parameters.Length;
            int origCost = origParamsAsset.CalcTotalCost();

            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            Track(instance);

            var desc = instance.GetComponent<VRCAvatarDescriptor>();
            AvatarScanResult scan = AvatarScanner.Scan(desc);
            Assert.Greater(scan.Parameters.Count, 10);
            Assert.Greater(scan.TotalSyncedBits, 30);

            ParameterSnapshot target = scan.Parameters.FirstOrDefault(p =>
                p.IsSynced && p.ValueKind == ParameterValueKind.Bool && !p.IsInternalParameter && p.ProtectionFlags == ProtectionFlags.None);
            Assert.IsNotNull(target);

            var comp = instance.AddComponent<ExParamManagerComponent>();
            comp.rules.Add(new ParameterRule
            {
                parameterName = target.EffectiveName,
                sourceComponent = target.SourceComponent,
                action = ParameterAction.Remove,
                hideMenuControlsOnRemove = true,
            });

            AvatarProcessor.ProcessAvatar(instance);

            TrackGeneratedAssetIfTemporary(desc.expressionParameters);
            TrackGeneratedAssetIfTemporary(desc.expressionsMenu);

            Assert.AreNotSame(origParamsAsset, desc.expressionParameters);
            Assert.Less(desc.expressionParameters.CalcTotalCost(), origCost);
            Assert.AreEqual(origParamCount, origParamsAsset.parameters.Length);
            Assert.AreEqual(origCost, origParamsAsset.CalcTotalCost());
        }
    }
}
