using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using nadena.dev.ndmf;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDK3.Dynamics.Contact.Components;
using VRC.SDK3.Dynamics.PhysBone.Components;

namespace Sebanne.ExParamManager.Editor
{
    /// <summary>
    /// メニュー（Extract Menu 済み・未実行両対応）、Animator 5 か所、VRCAvatarParameterDriver、
    /// PhysBone（5 サフィックス完全一致）、ContactReceiver、MA Reactive を逆引き走査する。
    /// MA スコープ内のローカル名は <see cref="ParameterInfo.ForUI.GetParameterRemappingsAt(GameObject)"/> で実名解決する。
    /// </summary>
    public static class ReferenceScanner
    {
        private const int MaxMenuDepth = 16;

        private static readonly string[] PhysBoneSuffixes =
        {
            "_IsGrabbed",
            "_IsPosed",
            "_Angle",
            "_Stretch",
            "_Squish",
        };

        public static void Scan(VRCAvatarDescriptor descriptor, AvatarScanResult result)
        {
            if (descriptor == null || result == null)
            {
                return;
            }

            var byName = new Dictionary<string, ParameterSnapshot>(StringComparer.Ordinal);
            for (int i = 0; i < result.Parameters.Count; i++)
            {
                ParameterSnapshot p = result.Parameters[i];
                if (!string.IsNullOrEmpty(p.EffectiveName) && !byName.ContainsKey(p.EffectiveName))
                {
                    byName[p.EffectiveName] = p;
                }
            }

            var remapCache = new Dictionary<GameObject, ImmutableDictionary<(ParameterNamespace, string), ParameterMapping>>();
            var visitedMenus = new HashSet<VRCExpressionsMenu>();

            // 1. VRCExpressionsMenu（本体 + MA Menu Installer 外部メニュー）
            if (descriptor.expressionsMenu != null)
            {
                ScanMenuRecursive(descriptor.expressionsMenu, 0, visitedMenus, byName);
            }

            IMaAdapter ma = MaAdapterRegistry.Current;
            if (ma != null)
            {
                IReadOnlyList<MaMenuInstallerInfo> installers = ma.CollectMenuInstallers(descriptor.gameObject);
                for (int i = 0; i < installers.Count; i++)
                {
                    MaMenuInstallerInfo inst = installers[i];
                    if (inst.MenuToAppend != null)
                    {
                        ScanMenuRecursive(inst.MenuToAppend, 0, visitedMenus, byName);
                    }

                    if (inst.InstallTargetMenu != null)
                    {
                        ScanMenuRecursive(inst.InstallTargetMenu, 0, visitedMenus, byName);
                    }
                }

                // 2. MA Menu Item（Extract Menu 済みオブジェクト含む）
                IReadOnlyList<MaMenuItemInfo> maMenuItems = ma.CollectMenuItems(descriptor.gameObject);
                for (int i = 0; i < maMenuItems.Count; i++)
                {
                    MaMenuItemInfo item = maMenuItems[i];
                    if (item.SubMenuAsset != null)
                    {
                        ScanMenuRecursive(item.SubMenuAsset, 0, visitedMenus, byName);
                    }

                    if (!string.IsNullOrEmpty(item.ParameterName))
                    {
                        string resolved = ResolveParameterName(item.GameObject, item.ParameterName, ParameterNamespace.Animator, remapCache);
                        RecordMenuControlRef(
                            byName,
                            resolved,
                            ReferenceKind.MaMenuItem,
                            item.DisplayName,
                            item.GameObject,
                            item.ControlType,
                            item.Value,
                            item.IsExtractMenuObject,
                            item.SubParameters.Count > 1);
                    }

                    for (int s = 0; s < item.SubParameters.Count; s++)
                    {
                        string spName = item.SubParameters[s];
                        string resolved = ResolveParameterName(item.GameObject, spName, ParameterNamespace.Animator, remapCache);
                        RecordMenuControlRef(
                            byName,
                            resolved,
                            ReferenceKind.MaMenuItem,
                            item.DisplayName,
                            item.GameObject,
                            item.ControlType,
                            null,
                            item.IsExtractMenuObject,
                            item.ControlType == (int)VRCExpressionsMenu.Control.ControlType.TwoAxisPuppet
                                || item.ControlType == (int)VRCExpressionsMenu.Control.ControlType.FourAxisPuppet);
                    }
                }
            }

            // 3. Animator 5 か所 + VRCAvatarParameterDriver（base / special / MA MergeAnimator）
            var scannedControllers = new HashSet<AnimatorController>();
            ScanPlayableLayers(descriptor.baseAnimationLayers, scannedControllers, null, remapCache, byName);
            ScanPlayableLayers(descriptor.specialAnimationLayers, scannedControllers, null, remapCache, byName);

            if (ma != null)
            {
                IReadOnlyList<MaMergeAnimatorInfo> mergers = ma.CollectMergeAnimators(descriptor.gameObject);
                for (int i = 0; i < mergers.Count; i++)
                {
                    MaMergeAnimatorInfo m = mergers[i];
                    AnimatorController ctrl = UnwrapController(m.Animator);
                    if (ctrl != null)
                    {
                        ScanAnimatorController(ctrl, m.GameObject, remapCache, byName);
                    }
                }
            }

            // 4. VRCPhysBone（5 サフィックス完全一致） & VRCContactReceiver
            ScanPhysBonesAndContacts(descriptor.gameObject, remapCache, byName);

            // 5. MA Reactive
            if (ma != null)
            {
                IReadOnlyList<MaReactiveInfo> reactives = ma.CollectReactiveComponents(descriptor.gameObject);
                for (int i = 0; i < reactives.Count; i++)
                {
                    MaReactiveInfo r = reactives[i];
                    if (string.IsNullOrEmpty(r.LinkedMenuParameterName))
                    {
                        continue;
                    }

                    string resolved = ResolveParameterName(r.GameObject, r.LinkedMenuParameterName, ParameterNamespace.Animator, remapCache);
                    if (byName.TryGetValue(resolved, out ParameterSnapshot snap))
                    {
                        snap.HasMaReactive = true;
                        snap.References.Add(new ParameterReference
                        {
                            Kind = ReferenceKind.MaReactive,
                            DisplayLabel = r.ComponentTypeName + " (" + r.GameObject.name + ")",
                            TargetObject = r.Component,
                        });
                    }
                }
            }

            // 6. 各パラメータの集計フラグを確定
            FinalizeParameterReferenceFlags(result.Parameters);
        }

        private static void ScanMenuRecursive(
            VRCExpressionsMenu menu,
            int depth,
            HashSet<VRCExpressionsMenu> visited,
            Dictionary<string, ParameterSnapshot> byName)
        {
            if (menu == null || depth > MaxMenuDepth || !visited.Add(menu) || menu.controls == null)
            {
                return;
            }

            for (int i = 0; i < menu.controls.Count; i++)
            {
                VRCExpressionsMenu.Control ctrl = menu.controls[i];
                if (ctrl == null)
                {
                    continue;
                }

                int controlType = (int)ctrl.type;
                string paramName = ctrl.parameter != null ? ctrl.parameter.name : string.Empty;
                if (!string.IsNullOrEmpty(paramName))
                {
                    RecordMenuControlRef(
                        byName,
                        paramName,
                        ReferenceKind.ExpressionsMenuAsset,
                        menu.name + " / " + ctrl.name,
                        menu,
                        controlType,
                        ctrl.value,
                        false,
                        ctrl.subParameters != null && ctrl.subParameters.Length > 1);
                }

                if (ctrl.subParameters != null)
                {
                    bool isMultiAxis = ctrl.type == VRCExpressionsMenu.Control.ControlType.TwoAxisPuppet
                        || ctrl.type == VRCExpressionsMenu.Control.ControlType.FourAxisPuppet;
                    for (int s = 0; s < ctrl.subParameters.Length; s++)
                    {
                        string spName = ctrl.subParameters[s]?.name;
                        if (!string.IsNullOrEmpty(spName))
                        {
                            RecordMenuControlRef(
                                byName,
                                spName,
                                ReferenceKind.ExpressionsMenuAsset,
                                menu.name + " / " + ctrl.name,
                                menu,
                                controlType,
                                null,
                                false,
                                isMultiAxis);
                        }
                    }
                }

                if (ctrl.subMenu != null)
                {
                    ScanMenuRecursive(ctrl.subMenu, depth + 1, visited, byName);
                }
            }
        }

        private static void RecordMenuControlRef(
            Dictionary<string, ParameterSnapshot> byName,
            string paramName,
            ReferenceKind kind,
            string displayLabel,
            UnityEngine.Object target,
            int controlType,
            float? writeValue,
            bool isExtractMenu,
            bool isMultiAxis)
        {
            if (string.IsNullOrEmpty(paramName) || !byName.TryGetValue(paramName, out ParameterSnapshot snap))
            {
                return;
            }

            bool isButtonOrToggle = controlType == (int)VRCExpressionsMenu.Control.ControlType.Button
                || controlType == (int)VRCExpressionsMenu.Control.ControlType.Toggle;
            if (isButtonOrToggle && writeValue.HasValue)
            {
                snap.HasMenuWriter = true;
                snap.MenuWriteValues.Add(writeValue.Value);
            }

            if (isMultiAxis)
            {
                snap.IsUsedInMultiAxis = true;
            }

            snap.References.Add(new ParameterReference
            {
                Kind = kind,
                DisplayLabel = displayLabel,
                TargetObject = target,
                MenuControlName = displayLabel,
                MenuControlType = controlType,
                MenuWriteValue = writeValue,
                IsExtractMenuObject = isExtractMenu,
            });
        }

        private static void ScanPlayableLayers(
            VRCAvatarDescriptor.CustomAnimLayer[] layers,
            HashSet<AnimatorController> scannedControllers,
            GameObject scopeObject,
            Dictionary<GameObject, ImmutableDictionary<(ParameterNamespace, string), ParameterMapping>> remapCache,
            Dictionary<string, ParameterSnapshot> byName)
        {
            if (layers == null)
            {
                return;
            }

            for (int i = 0; i < layers.Length; i++)
            {
                AnimatorController ctrl = UnwrapController(layers[i].animatorController);
                if (ctrl != null && scannedControllers.Add(ctrl))
                {
                    ScanAnimatorController(ctrl, scopeObject, remapCache, byName);
                }
            }
        }

        public static AnimatorController UnwrapController(RuntimeAnimatorController runtimeController)
        {
            if (runtimeController is AnimatorController ac)
            {
                return ac;
            }

            if (runtimeController is AnimatorOverrideController aoc)
            {
                return aoc.runtimeAnimatorController as AnimatorController;
            }

            return null;
        }

        private static void ScanAnimatorController(
            AnimatorController controller,
            GameObject scopeObject,
            Dictionary<GameObject, ImmutableDictionary<(ParameterNamespace, string), ParameterMapping>> remapCache,
            Dictionary<string, ParameterSnapshot> byName)
        {
            if (controller == null)
            {
                return;
            }

            var visitedSms = new HashSet<AnimatorStateMachine>();
            var visitedTrees = new HashSet<BlendTree>();

            AnimatorControllerLayer[] layers = controller.layers;
            for (int i = 0; i < layers.Length; i++)
            {
                AnimatorStateMachine sm = layers[i].stateMachine;
                if (sm != null)
                {
                    ScanStateMachineRecursive(controller, sm, scopeObject, visitedSms, visitedTrees, remapCache, byName);
                }
            }
        }

        private static void ScanStateMachineRecursive(
            AnimatorController controller,
            AnimatorStateMachine sm,
            GameObject scopeObject,
            HashSet<AnimatorStateMachine> visitedSms,
            HashSet<BlendTree> visitedTrees,
            Dictionary<GameObject, ImmutableDictionary<(ParameterNamespace, string), ParameterMapping>> remapCache,
            Dictionary<string, ParameterSnapshot> byName)
        {
            if (sm == null || !visitedSms.Add(sm))
            {
                return;
            }

            // AnyState Transitions & Entry Transitions
            ScanTransitions(controller, sm.anyStateTransitions, ReferenceKind.AnimatorAnyStateTransition, sm.name, scopeObject, remapCache, byName);
            ScanTransitions(controller, sm.entryTransitions, ReferenceKind.AnimatorTransition, sm.name + " (Entry)", scopeObject, remapCache, byName);

            // StateMachine Behaviours (VRCAvatarParameterDriver)
            ScanBehaviours(controller, sm.behaviours, sm.name, scopeObject, remapCache, byName);

            ChildAnimatorState[] states = sm.states;
            for (int i = 0; i < states.Length; i++)
            {
                AnimatorState st = states[i].state;
                if (st == null)
                {
                    continue;
                }

                // ① Transition Conditions
                ScanTransitions(controller, st.transitions, ReferenceKind.AnimatorTransition, st.name, scopeObject, remapCache, byName);

                // ③ BlendTree 再帰
                if (st.motion is BlendTree bt)
                {
                    ScanBlendTreeRecursive(controller, bt, st.name, visitedTrees, scopeObject, remapCache, byName);
                }

                // ④ State の *ParameterActive == true の場合のみ記録（幽霊参照防止）
                if (st.timeParameterActive && !string.IsNullOrEmpty(st.timeParameter))
                {
                    RecordAnimatorPropRef(byName, ResolveParameterName(scopeObject, st.timeParameter, ParameterNamespace.Animator, remapCache), controller, st.name + " (MotionTime)");
                }
                if (st.speedParameterActive && !string.IsNullOrEmpty(st.speedParameter))
                {
                    RecordAnimatorPropRef(byName, ResolveParameterName(scopeObject, st.speedParameter, ParameterNamespace.Animator, remapCache), controller, st.name + " (Speed)");
                }
                if (st.cycleOffsetParameterActive && !string.IsNullOrEmpty(st.cycleOffsetParameter))
                {
                    RecordAnimatorPropRef(byName, ResolveParameterName(scopeObject, st.cycleOffsetParameter, ParameterNamespace.Animator, remapCache), controller, st.name + " (CycleOffset)");
                }
                if (st.mirrorParameterActive && !string.IsNullOrEmpty(st.mirrorParameter))
                {
                    RecordAnimatorPropRef(byName, ResolveParameterName(scopeObject, st.mirrorParameter, ParameterNamespace.Animator, remapCache), controller, st.name + " (Mirror)");
                }

                // ⑤ State Behaviours (VRCAvatarParameterDriver)
                ScanBehaviours(controller, st.behaviours, st.name, scopeObject, remapCache, byName);
            }

            ChildAnimatorStateMachine[] subSms = sm.stateMachines;
            for (int i = 0; i < subSms.Length; i++)
            {
                if (subSms[i].stateMachine != null)
                {
                    ScanStateMachineRecursive(controller, subSms[i].stateMachine, scopeObject, visitedSms, visitedTrees, remapCache, byName);
                }
            }
        }

        private static void ScanTransitions(
            AnimatorController controller,
            AnimatorTransitionBase[] transitions,
            ReferenceKind kind,
            string contextName,
            GameObject scopeObject,
            Dictionary<GameObject, ImmutableDictionary<(ParameterNamespace, string), ParameterMapping>> remapCache,
            Dictionary<string, ParameterSnapshot> byName)
        {
            if (transitions == null)
            {
                return;
            }

            for (int t = 0; t < transitions.Length; t++)
            {
                AnimatorTransitionBase tr = transitions[t];
                if (tr == null || tr.conditions == null)
                {
                    continue;
                }

                AnimatorCondition[] conds = tr.conditions;
                for (int c = 0; c < conds.Length; c++)
                {
                    string rawName = conds[c].parameter;
                    if (string.IsNullOrEmpty(rawName))
                    {
                        continue;
                    }

                    string resolved = ResolveParameterName(scopeObject, rawName, ParameterNamespace.Animator, remapCache);
                    if (byName.TryGetValue(resolved, out ParameterSnapshot snap))
                    {
                        snap.AnimatorConditions.Add(new AnimatorConditionObservation((int)conds[c].mode, conds[c].threshold));
                        snap.References.Add(new ParameterReference
                        {
                            Kind = kind,
                            DisplayLabel = controller.name + " / " + contextName,
                            Detail = conds[c].mode + " " + conds[c].threshold,
                            TargetObject = controller,
                        });
                    }
                }
            }
        }

        private static void ScanBlendTreeRecursive(
            AnimatorController controller,
            BlendTree tree,
            string stateName,
            HashSet<BlendTree> visitedTrees,
            GameObject scopeObject,
            Dictionary<GameObject, ImmutableDictionary<(ParameterNamespace, string), ParameterMapping>> remapCache,
            Dictionary<string, ParameterSnapshot> byName)
        {
            if (tree == null || !visitedTrees.Add(tree))
            {
                return;
            }

            RecordBlendTreeParam(byName, ResolveParameterName(scopeObject, tree.blendParameter, ParameterNamespace.Animator, remapCache), controller, stateName, tree.name);
            if (tree.blendType != BlendTreeType.Simple1D && tree.blendType != BlendTreeType.Direct)
            {
                RecordBlendTreeParam(byName, ResolveParameterName(scopeObject, tree.blendParameterY, ParameterNamespace.Animator, remapCache), controller, stateName, tree.name);
            }

            ChildMotion[] children = tree.children;
            for (int i = 0; i < children.Length; i++)
            {
                if (tree.blendType == BlendTreeType.Direct && !string.IsNullOrEmpty(children[i].directBlendParameter))
                {
                    RecordBlendTreeParam(
                        byName,
                        ResolveParameterName(scopeObject, children[i].directBlendParameter, ParameterNamespace.Animator, remapCache),
                        controller,
                        stateName,
                        tree.name);
                }

                if (children[i].motion is BlendTree childTree)
                {
                    ScanBlendTreeRecursive(controller, childTree, stateName, visitedTrees, scopeObject, remapCache, byName);
                }
            }
        }

        private static void RecordBlendTreeParam(
            Dictionary<string, ParameterSnapshot> byName,
            string paramName,
            AnimatorController controller,
            string stateName,
            string treeName)
        {
            if (string.IsNullOrEmpty(paramName) || !byName.TryGetValue(paramName, out ParameterSnapshot snap))
            {
                return;
            }

            snap.References.Add(new ParameterReference
            {
                Kind = ReferenceKind.AnimatorBlendTree,
                DisplayLabel = controller.name + " / " + stateName + " (" + treeName + ")",
                TargetObject = controller,
            });
        }

        private static void RecordAnimatorPropRef(
            Dictionary<string, ParameterSnapshot> byName,
            string paramName,
            AnimatorController controller,
            string label)
        {
            if (string.IsNullOrEmpty(paramName) || !byName.TryGetValue(paramName, out ParameterSnapshot snap))
            {
                return;
            }

            snap.References.Add(new ParameterReference
            {
                Kind = ReferenceKind.AnimatorStateProperty,
                DisplayLabel = controller.name + " / " + label,
                TargetObject = controller,
            });
        }

        private static void ScanBehaviours(
            AnimatorController controller,
            StateMachineBehaviour[] behaviours,
            string contextName,
            GameObject scopeObject,
            Dictionary<GameObject, ImmutableDictionary<(ParameterNamespace, string), ParameterMapping>> remapCache,
            Dictionary<string, ParameterSnapshot> byName)
        {
            if (behaviours == null)
            {
                return;
            }

            for (int i = 0; i < behaviours.Length; i++)
            {
                if (!(behaviours[i] is VRCAvatarParameterDriver driver) || driver.parameters == null)
                {
                    continue;
                }

                for (int p = 0; p < driver.parameters.Count; p++)
                {
                    var entry = driver.parameters[p];
                    if (!string.IsNullOrEmpty(entry.name))
                    {
                        string destResolved = ResolveParameterName(scopeObject, entry.name, ParameterNamespace.Animator, remapCache);
                        if (byName.TryGetValue(destResolved, out ParameterSnapshot destSnap))
                        {
                            destSnap.HasParameterDriverWrite = true;
                            destSnap.References.Add(new ParameterReference
                            {
                                Kind = ReferenceKind.ParameterDriverWrite,
                                DisplayLabel = controller.name + " / " + contextName + " (Driver Write)",
                                Detail = entry.type.ToString(),
                                TargetObject = controller,
                            });
                        }
                    }

                    if (!string.IsNullOrEmpty(entry.source))
                    {
                        string srcResolved = ResolveParameterName(scopeObject, entry.source, ParameterNamespace.Animator, remapCache);
                        if (byName.TryGetValue(srcResolved, out ParameterSnapshot srcSnap))
                        {
                            srcSnap.HasParameterDriverRead = true;
                            srcSnap.References.Add(new ParameterReference
                            {
                                Kind = ReferenceKind.ParameterDriverCopySource,
                                DisplayLabel = controller.name + " / " + contextName + " (Driver Copy元)",
                                TargetObject = controller,
                            });
                        }
                    }
                }
            }
        }

        private static void ScanPhysBonesAndContacts(
            GameObject avatarRoot,
            Dictionary<GameObject, ImmutableDictionary<(ParameterNamespace, string), ParameterMapping>> remapCache,
            Dictionary<string, ParameterSnapshot> byName)
        {
            VRCPhysBone[] physBones = avatarRoot.GetComponentsInChildren<VRCPhysBone>(true);
            for (int i = 0; i < physBones.Length; i++)
            {
                VRCPhysBone pb = physBones[i];
                if (pb == null || string.IsNullOrEmpty(pb.parameter))
                {
                    continue;
                }

                string prefix = ResolveParameterName(pb.gameObject, pb.parameter, ParameterNamespace.PhysBonesPrefix, remapCache);

                // 完全一致: プレフィックス本体 + 派生 5 サフィックスのみ
                RecordPhysBoneExact(byName, prefix, pb);
                for (int s = 0; s < PhysBoneSuffixes.Length; s++)
                {
                    RecordPhysBoneExact(byName, prefix + PhysBoneSuffixes[s], pb);
                }
            }

            VRCContactReceiver[] contacts = avatarRoot.GetComponentsInChildren<VRCContactReceiver>(true);
            for (int i = 0; i < contacts.Length; i++)
            {
                VRCContactReceiver cr = contacts[i];
                if (cr == null || string.IsNullOrEmpty(cr.parameter))
                {
                    continue;
                }

                string resolved = ResolveParameterName(cr.gameObject, cr.parameter, ParameterNamespace.Animator, remapCache);
                if (byName.TryGetValue(resolved, out ParameterSnapshot snap))
                {
                    snap.HasPhysBoneOrContact = true;
                    snap.References.Add(new ParameterReference
                    {
                        Kind = ReferenceKind.ContactReceiver,
                        DisplayLabel = "ContactReceiver (" + cr.gameObject.name + ")",
                        TargetObject = cr,
                    });
                }
            }
        }

        private static void RecordPhysBoneExact(
            Dictionary<string, ParameterSnapshot> byName,
            string exactName,
            VRCPhysBone pb)
        {
            if (byName.TryGetValue(exactName, out ParameterSnapshot snap))
            {
                snap.HasPhysBoneOrContact = true;
                snap.References.Add(new ParameterReference
                {
                    Kind = ReferenceKind.PhysBone,
                    DisplayLabel = "PhysBone (" + pb.gameObject.name + ")",
                    TargetObject = pb,
                });
            }
        }

        private static void FinalizeParameterReferenceFlags(List<ParameterSnapshot> parameters)
        {
            for (int i = 0; i < parameters.Count; i++)
            {
                ParameterSnapshot p = parameters[i];
                bool hasRadial = false;
                bool hasNonRadialMenu = false;

                for (int r = 0; r < p.References.Count; r++)
                {
                    ParameterReference rf = p.References[r];
                    if (rf.Kind != ReferenceKind.ExpressionsMenuAsset && rf.Kind != ReferenceKind.MaMenuItem)
                    {
                        continue;
                    }

                    if (rf.MenuControlType == (int)VRCExpressionsMenu.Control.ControlType.RadialPuppet)
                    {
                        hasRadial = true;
                    }
                    else
                    {
                        hasNonRadialMenu = true;
                    }
                }

                p.IsRadialOnlyFloat = p.ValueKind == ParameterValueKind.Float && hasRadial && !hasNonRadialMenu;
            }
        }

        private static string ResolveParameterName(
            GameObject scopeObject,
            string rawName,
            ParameterNamespace ns,
            Dictionary<GameObject, ImmutableDictionary<(ParameterNamespace, string), ParameterMapping>> cache)
        {
            if (scopeObject == null || string.IsNullOrEmpty(rawName))
            {
                return rawName ?? string.Empty;
            }

            if (!cache.TryGetValue(scopeObject, out ImmutableDictionary<(ParameterNamespace, string), ParameterMapping> remaps))
            {
                remaps = ParameterInfo.ForUI.GetParameterRemappingsAt(scopeObject);
                cache[scopeObject] = remaps;
            }

            if (remaps != null && remaps.TryGetValue((ns, rawName), out ParameterMapping mapping) && !string.IsNullOrEmpty(mapping.ParameterName))
            {
                return mapping.ParameterName;
            }

            return rawName;
        }
    }
}
