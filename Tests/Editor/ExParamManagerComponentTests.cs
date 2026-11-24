using System.Reflection;
using nadena.dev.ndmf;
using NUnit.Framework;
using UnityEngine;
using VRC.SDKBase;

namespace Sebanne.ExParamManager.Editor.Tests
{
    public sealed class ExParamManagerComponentTests
    {
        [Test]
        public void Component_ImplementsINDMFEditorOnly_AndHasExpectedAttributes()
        {
            var type = typeof(ExParamManagerComponent);

#if HAS_NDMF_EDITOR_ONLY
            Assert.IsTrue(typeof(INDMFEditorOnly).IsAssignableFrom(type));
#endif
            Assert.IsTrue(typeof(IEditorOnly).IsAssignableFrom(type));
            Assert.IsNotNull(type.GetCustomAttribute<DisallowMultipleComponent>());

            var menuAttr = type.GetCustomAttribute<AddComponentMenu>();
            Assert.IsNotNull(menuAttr);
            Assert.AreEqual("ExParam Manager/ExParam Manager", menuAttr.componentMenu);
        }

        [Test]
        public void Component_DefaultValues_MatchPlanSpec()
        {
            var go = new GameObject("AvatarRoot");
            try
            {
                var comp = go.AddComponent<ExParamManagerComponent>();
                Assert.IsTrue(comp.enabledOnBuild);
                Assert.IsTrue(comp.pruneEmptySubMenus);
                Assert.IsFalse(comp.allowOverrideWithSyncSequence);
                Assert.IsFalse(comp.verboseLogging);
                Assert.IsNotNull(comp.rules);
                Assert.AreEqual(0, comp.rules.Count);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void ParameterRule_ClampsBitWidth_AndClonesAllFields()
        {
            var go = new GameObject("SourceObj");
            try
            {
                var rule = new ParameterRule
                {
                    parameterName = "TestInt",
                    sourceComponent = go.transform,
                    sourceObjectPath = "AvatarRoot/SourceObj",
                    isInternalParameter = true,
                    originalName = "OrigInt",
                    action = ParameterAction.ResizeBitWidth,
                    targetBitWidth = 1,
                    hideMenuControlsOnRemove = false,
                };

                Assert.IsFalse(rule.disabled);
                Assert.IsTrue(rule.IsEnabled);
                Assert.IsTrue(rule.IsActive);

                rule.disabled = true;
                Assert.IsFalse(rule.IsEnabled);
                Assert.IsFalse(rule.IsActive);

                Assert.AreEqual(2, rule.GetClampedBitWidth());
                rule.targetBitWidth = 9;
                Assert.AreEqual(8, rule.GetClampedBitWidth());
                rule.targetBitWidth = 5;
                Assert.AreEqual(5, rule.GetClampedBitWidth());

                ParameterRule cloned = rule.Clone();
                Assert.AreNotSame(rule, cloned);
                Assert.AreEqual(rule.parameterName, cloned.parameterName);
                Assert.AreSame(rule.sourceComponent, cloned.sourceComponent);
                Assert.AreEqual(rule.sourceObjectPath, cloned.sourceObjectPath);
                Assert.AreEqual(rule.isInternalParameter, cloned.isInternalParameter);
                Assert.AreEqual(rule.originalName, cloned.originalName);
                Assert.AreEqual(rule.action, cloned.action);
                Assert.AreEqual(rule.targetBitWidth, cloned.targetBitWidth);
                Assert.AreEqual(rule.hideMenuControlsOnRemove, cloned.hideMenuControlsOnRemove);
                Assert.AreEqual(rule.disabled, cloned.disabled);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void FindAndGetOrCreateRule_PrioritizesSourceComponent_AndFallsBackToName()
        {
            var root = new GameObject("AvatarRoot");
            var childA = new GameObject("ChildA");
            var childB = new GameObject("ChildB");
            childA.transform.SetParent(root.transform);
            childB.transform.SetParent(root.transform);

            try
            {
                var comp = root.AddComponent<ExParamManagerComponent>();
                ParameterRule rA = comp.GetOrCreateRule("SharedParam", childA.transform, "ChildA");
                ParameterRule rB = comp.GetOrCreateRule("SharedParam", childB.transform, "ChildB");

                Assert.AreNotSame(rA, rB);
                Assert.AreSame(rA, comp.FindRule("SharedParam", childA.transform));
                Assert.AreSame(rB, comp.FindRule("SharedParam", childB.transform));
                Assert.AreSame(rA, comp.FindRule("SharedParam"));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void FindRule_DoesNotCollideAcrossDifferentParameters_OnSameSourceComponent()
        {
            var root = new GameObject("AvatarRoot");
            try
            {
                var comp = root.AddComponent<ExParamManagerComponent>();
                ParameterRule breastRule = comp.GetOrCreateRule("Breast_Size", root.transform);
                breastRule.action = ParameterAction.Remove;

                // 同じ sourceComponent を持つ別パラメータは null を返さなければならない
                Assert.IsNull(comp.FindRule("FacialSet", root.transform));
                Assert.IsNull(comp.FindRule("Apron_Ribbon", root.transform));
                Assert.AreSame(breastRule, comp.FindRule("Breast_Size", root.transform));

                ParameterRule facialRule = comp.GetOrCreateRule("FacialSet", root.transform);
                facialRule.action = ParameterAction.ResizeBitWidth;
                facialRule.targetBitWidth = 2;

                Assert.AreSame(breastRule, comp.FindRule("Breast_Size", root.transform));
                Assert.AreSame(facialRule, comp.FindRule("FacialSet", root.transform));
                Assert.IsNull(comp.FindRule("Apron_Ribbon", root.transform));

                Assert.IsTrue(comp.RemoveRule("Breast_Size", root.transform));
                Assert.IsNull(comp.FindRule("Breast_Size", root.transform));
                Assert.AreSame(facialRule, comp.FindRule("FacialSet", root.transform));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void MaAdapter_AutoRegisters_AndDetectsSyncSequence_FalseOnEmptyObject()
        {
#if HAS_MODULAR_AVATAR
            Assert.IsTrue(MaAdapterRegistry.IsAvailable);
            Assert.IsNotNull(MaAdapterRegistry.Current);

            var go = new GameObject("EmptyAvatar");
            try
            {
                Assert.IsFalse(MaAdapterRegistry.Current.HasSyncParameterSequence(go));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
#else
            Assert.IsFalse(MaAdapterRegistry.IsAvailable);
            Assert.IsNull(MaAdapterRegistry.Current);
#endif
        }

        [Test]
        public void EnvironmentCapabilities_DetectsSdk3106_Ndmf115_AndDynamicsTypes()
        {
#if HAS_VRCSDK3_AVATARS_3_10_6
            Assert.IsTrue(EnvironmentCapabilities.HasVrcSdk3106);
#else
            Assert.IsFalse(EnvironmentCapabilities.HasVrcSdk3106);
#endif
#if HAS_NDMF_1_15
            Assert.IsTrue(EnvironmentCapabilities.HasNdmf115);
#else
            Assert.IsFalse(EnvironmentCapabilities.HasNdmf115);
#endif
            Assert.IsTrue(EnvironmentCapabilities.SupportsDynamicsTypes);
        }
    }
}
