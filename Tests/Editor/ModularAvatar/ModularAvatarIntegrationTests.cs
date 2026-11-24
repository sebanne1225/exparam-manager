using System.Collections.Generic;
using System.Linq;
using nadena.dev.modular_avatar.core;
using nadena.dev.ndmf;
using NUnit.Framework;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace Sebanne.ExParamManager.Editor.ModularAvatar.Tests
{
    public sealed class ModularAvatarIntegrationTests
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
        public void Scanner_ResolvesInternalParameterRemapping_AndExtractMenuItems_AndExternalMenuInstaller()
        {
            var root = Track(new GameObject("AvatarRoot"));
            var desc = root.AddComponent<VRCAvatarDescriptor>();
            desc.expressionParameters = Track(ScriptableObject.CreateInstance<VRCExpressionParameters>());

            var gimmick = new GameObject("Gimmick");
            gimmick.transform.SetParent(root.transform);

            var maParams = gimmick.AddComponent<ModularAvatarParameters>();
            maParams.parameters.Add(new ParameterConfig
            {
                nameOrPrefix = "LocalFlag",
                internalParameter = true,
                syncType = ParameterSyncType.Bool,
                localOnly = false,
            });

            var menuGroup = gimmick.AddComponent<ModularAvatarMenuGroup>();
            var menuItemObj = new GameObject("ToggleItem");
            menuItemObj.transform.SetParent(gimmick.transform);
            var maMenuItem = menuItemObj.AddComponent<ModularAvatarMenuItem>();
            maMenuItem.Control = new VRCExpressionsMenu.Control
            {
                name = "Toggle",
                type = VRCExpressionsMenu.Control.ControlType.Toggle,
                parameter = new VRCExpressionsMenu.Control.Parameter { name = "LocalFlag" },
                value = 1f,
            };

            // External menu via MenuInstaller
            var extMenu = Track(ScriptableObject.CreateInstance<VRCExpressionsMenu>());
            extMenu.controls.Add(new VRCExpressionsMenu.Control
            {
                name = "ExtToggle",
                type = VRCExpressionsMenu.Control.ControlType.Toggle,
                parameter = new VRCExpressionsMenu.Control.Parameter { name = "ExtParam" },
                value = 1f,
            });
            var installer = gimmick.AddComponent<ModularAvatarMenuInstaller>();
            installer.menuToAppend = extMenu;

            maParams.parameters.Add(new ParameterConfig
            {
                nameOrPrefix = "ExtParam",
                internalParameter = false,
                syncType = ParameterSyncType.Bool,
            });

            AvatarScanResult scan = AvatarScanner.Scan(desc);

            ParameterSnapshot internalSnap = scan.Parameters.FirstOrDefault(p => p.OriginalName == "LocalFlag");
            Assert.IsNotNull(internalSnap);
            Assert.IsTrue(internalSnap.IsInternalParameter);
            Assert.IsTrue((internalSnap.ProtectionFlags & ProtectionFlags.InternalParameter) != 0);
            Assert.IsTrue(internalSnap.References.Any(r => r.Kind == ReferenceKind.MaMenuItem && r.IsExtractMenuObject && r.TargetObject == menuItemObj));

            ParameterSnapshot extSnap = scan.Parameters.FirstOrDefault(p => p.EffectiveName == "ExtParam");
            Assert.IsNotNull(extSnap);
            Assert.IsTrue(extSnap.References.Any(r => r.Kind == ReferenceKind.ExpressionsMenuAsset && r.TargetObject == extMenu));
        }

        [Test]
        public void Pass2_BlocksChangesWhenSyncParameterSequencePresent_UnlessOptedIn()
        {
            var root = Track(new GameObject("AvatarRoot"));
            var desc = root.AddComponent<VRCAvatarDescriptor>();

            var expParams = Track(ScriptableObject.CreateInstance<VRCExpressionParameters>());
            expParams.parameters = new[]
            {
                new VRCExpressionParameters.Parameter { name = "KeepTarget", valueType = VRCExpressionParameters.ValueType.Bool, networkSynced = true },
            };
            desc.expressionParameters = expParams;

            root.AddComponent<ModularAvatarSyncParameterSequence>();
            var comp = root.AddComponent<ExParamManagerComponent>();
            comp.allowOverrideWithSyncSequence = false;
            comp.rules.Add(new ParameterRule { parameterName = "KeepTarget", action = ParameterAction.Remove });

            var ctx1 = new BuildContext(root, null, isClone: true);
            ExParamBuildProcessor.RunPass1(ctx1);
            ExParamBuildProcessor.RunPass2(ctx1);

            // Blocked by default
            Assert.IsTrue(desc.expressionParameters.parameters.Any(p => p.name == "KeepTarget"));
            Assert.IsTrue(ctx1.GetState<ExParamBuildState>().RecordedReports.Any(r => r.Severity == ErrorSeverity.NonFatal));

            // Opt-in override allows removal
            comp.allowOverrideWithSyncSequence = true;
            var ctx2 = new BuildContext(root, null, isClone: true);
            ExParamBuildProcessor.RunPass1(ctx2);
            ExParamBuildProcessor.RunPass2(ctx2);

            Assert.IsFalse(desc.expressionParameters.parameters.Any(p => p.name == "KeepTarget"));
        }
    }
}
