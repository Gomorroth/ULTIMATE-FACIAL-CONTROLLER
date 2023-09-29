using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using nadena.dev.modular_avatar.core;
using nadena.dev.ndmf;
using nadena.dev.ndmf.util;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using Object = UnityEngine.Object;
using DriverParameter = VRC.SDKBase.VRC_AvatarParameterDriver.Parameter;

[assembly: ExportsPlugin(typeof(gomoru.su.UltimateFacialController.UltimateFacialControllerCore))]

namespace gomoru.su.UltimateFacialController
{
    public sealed partial class UltimateFacialControllerCore : Plugin<UltimateFacialControllerCore>
    {
        public override string DisplayName => "Ultimate Facial Controller";

        public override string QualifiedName => "gomoru.su.ultimate-facial-controller";

        protected override void Configure()
        {
            InPhase(BuildPhase.Generating)
                .BeforePlugin("nadena.dev.modular-avatar")
                .Run("Generate Facial Controller", Run);
        }

        private const string ControlTargetParameterName = "_ULTIMATEFACECONTROLLER_/ControlTarget";
        private const string ControlValueParameterName = "_ULTIMATEFACECONTROLLER_/ControlValue";

        private void Run(BuildContext context)
        {
            var component = context.AvatarRootObject.GetComponentInChildren<UltimateFacialController>();
            if (component == null)
                return;

            var obj = new GameObject("Facial");
            obj.transform.parent = context.AvatarRootTransform;
            var mp = obj.AddComponent<ModularAvatarParameters>();

            mp.parameters.Add(MAParameter<int>(ControlTargetParameterName));
            mp.parameters.Add(MAParameter<float>(ControlValueParameterName));

            var controller = new AnimatorController().AddTo(context);
            var controlLayer = controller.CreateLayer("Control");

            var list = new List<Control>();

            var renderer = component.GetComponent<SkinnedMeshRenderer>();
            foreach (var (name, weight) in renderer.EnumerateBlendshapes().Where(x => !x.Name.StartsWith(component.BlendshapeSeparatorText, System.StringComparison.OrdinalIgnoreCase)))
            {
                var control = new AnimationClip() { name = name }.AddTo(context);
                var defaultAnim = new AnimationClip() { name = $"{name} Default" }.AddTo(context);
                var bind = new EditorCurveBinding() { path = renderer.AvatarRootPath(), propertyName = $"blendShape.{name}", type = typeof(SkinnedMeshRenderer) };
                AnimationUtility.SetEditorCurve(control, bind, AnimationCurve.Linear(0, 0, 1f / 60, 100));
                AnimationUtility.SetEditorCurve(defaultAnim, bind, AnimationCurve.Constant(0, 0, weight));

                list.Add(new Control() { Name = name, DefaultValue = weight, ControlAnimation = control, DefualtValueAnimation = defaultAnim });
            }

            var blank = new AnimationClip().AddTo(context);
            {
                var settings = AnimationUtility.GetAnimationClipSettings(blank);
                settings.loopTime = false;
                settings.stopTime = 0f;
                AnimationUtility.SetAnimationClipSettings(blank, settings);
            }
            var span = list.AsSpan();

            {
                var layer = controlLayer;
                var idle = layer.stateMachine.AddState("Idle", layer.stateMachine.entryPosition + new Vector3(0, 40));
                idle.writeDefaultValues = false;
                idle.motion = blank;

                for (int i = 0; i < span.Length; i++)
                {
                    var control = span[i];

                    var state = layer.stateMachine.AddState(control.Name, layer.stateMachine.entryPosition + new Vector3(200 * (i / 64 + 1), 40 * (i % 64 + 1)));
                    state.motion = control.ControlAnimation;
                    state.timeParameterActive = true;
                    state.timeParameter = ControlValueParameterName;
                    state.writeDefaultValues = false;

                    var d = state.AddStateMachineBehaviour<VRCAvatarParameterDriver>();
                    d.parameters.Add(new DriverParameter() { type = VRC.SDKBase.VRC_AvatarParameterDriver.ChangeType.Copy, source = control.Name, name = ControlValueParameterName });

                    var s = idle.AddTransition(state);
                    s.conditions = new[] { new AnimatorCondition() { parameter = ControlTargetParameterName, mode = AnimatorConditionMode.Equals, threshold = i + 1 } };
                    s.hasExitTime = false;
                    s.duration = 0;

                    s = state.AddTransition(idle);
                    s.conditions = new[] { new AnimatorCondition() { parameter = ControlTargetParameterName, mode = AnimatorConditionMode.NotEqual, threshold = i + 1 } };
                    s.hasExitTime = false;
                    s.duration = 0;

                    s = state.AddTransition(state);
                    s.hasExitTime = true;
                    s.exitTime = 0f;
                    s.duration = 0;

                    mp.parameters.Add(MAParameter(control.Name, control.DefaultValue));
                }
            }

            foreach(var param in mp.parameters)
            {
                var p = new AnimatorControllerParameter()
                {
                    name = param.nameOrPrefix,
                };
                if (param.syncType == ParameterSyncType.Int)
                {
                    p.type = AnimatorControllerParameterType.Int;
                    p.defaultInt = (int)param.defaultValue;
                }
                else if (param.syncType == ParameterSyncType.Float)
                {
                    p.type = AnimatorControllerParameterType.Float;
                    p.defaultFloat = param.defaultValue;
                }
                else if (param.syncType == ParameterSyncType.Bool)
                {
                    p.type = AnimatorControllerParameterType.Bool;
                    p.defaultBool = param.defaultValue == 0 ? false : true;
                }
                controller.AddParameter(p);
            }

            var mm = obj.AddComponent<ModularAvatarMenuInstaller>();
            var mi = obj.AddComponent<ModularAvatarMenuItem>();
            mi.Control.type = VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionsMenu.Control.ControlType.SubMenu;
            mi.MenuSource = SubmenuSource.Children;

            GameObject page = null;
            int pageNum = 1;

            for (int i = 0; i < span.Length; i++)
            {
                var control = span[i];

                if (page == null || page.transform.childCount >= 8)
                {
                    page = new GameObject($"Page {pageNum++}");
                    page.transform.parent = obj.transform; 
                    mi = page.AddComponent<ModularAvatarMenuItem>();
                    mi.Control.type = VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionsMenu.Control.ControlType.SubMenu;
                    mi.MenuSource = SubmenuSource.Children;
                }

                var o = new GameObject(control.Name);
                o.transform.parent = page.transform;
                var item = o.AddComponent<ModularAvatarMenuItem>();
                item.Control.type = VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionsMenu.Control.ControlType.RadialPuppet;
                item.Control.parameter = new VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionsMenu.Control.Parameter() { name = ControlTargetParameterName };
                item.Control.value = i + 1;
                item.Control.subParameters = new[] { new VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionsMenu.Control.Parameter() { name = control.Name } };
            }

            var ma = obj.AddComponent<ModularAvatarMergeAnimator>();
            ma.layerType = VRCAvatarDescriptor.AnimLayerType.FX;
            ma.pathMode = MergeAnimatorPathMode.Absolute;
            ma.animator = controller;
            ma.matchAvatarWriteDefaults = false;
        }

        private static ParameterConfig MAParameter<T>(string name, T defaultValue = default, bool isSaved = false, bool isLocalOnly = true, bool isInternal = false) where T : struct
        {
            var param = new ParameterConfig()
            {
                nameOrPrefix = name,
                saved = isSaved,
                localOnly = isLocalOnly,
                internalParameter = isInternal,
            };
            
            if (typeof(T) == typeof(int))
            {
                param.syncType = ParameterSyncType.Int;
                param.defaultValue = (int)(object)defaultValue;
            }
            else if (typeof(T) == typeof(float))
            {
                param.syncType = ParameterSyncType.Float;
                param.defaultValue = (float)(object)defaultValue;
            }
            else if (typeof(T) == typeof(bool))
            {
                param.syncType = ParameterSyncType.Bool;
                param.defaultValue = (bool)(object)defaultValue ? 1 : 0;
            }
            else
            {
                param.syncType = ParameterSyncType.NotSynced;
            }

            return param;
        }

        private static Vector3 Circle(float angle)
        {
            float rad = (angle % 360) * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(rad), Mathf.Cos(rad));
        }

        private class Control
        {
            public string Name;
            public float DefaultValue;

            public AnimationClip ControlAnimation;
            public AnimationClip DefualtValueAnimation;
            public AnimatorState State;
        }
    }

    internal static class Extension
    {
        public static T AddTo<T>(this T obj, BuildContext context) where T : Object
        {
            AssetDatabase.AddObjectToAsset(obj, context.AssetContainer);
            return obj;
        }

        public static AnimatorControllerLayer CreateLayer(this AnimatorController controller, string name)
        {
            AnimatorControllerLayer animatorControllerLayer = new AnimatorControllerLayer();
            animatorControllerLayer.name = controller.MakeUniqueLayerName(name);
            animatorControllerLayer.stateMachine = new AnimatorStateMachine();
            animatorControllerLayer.stateMachine.name = animatorControllerLayer.name;
            animatorControllerLayer.stateMachine.hideFlags = HideFlags.HideInHierarchy;
            animatorControllerLayer.defaultWeight = 1f;
            if (AssetDatabase.GetAssetPath(controller) != "")
            {
                AssetDatabase.AddObjectToAsset(animatorControllerLayer.stateMachine, AssetDatabase.GetAssetPath(controller));
            }

            controller.AddLayer(animatorControllerLayer);
            return animatorControllerLayer;
        }

        public static IEnumerable<(string Name, float Weight)> EnumerateBlendshapes(this SkinnedMeshRenderer renderer)
        {
            var mesh = renderer.sharedMesh;
            if (mesh == null) 
                yield break;

            int count = mesh.blendShapeCount;
            for (int i = 0; i < count; i++)
            {
                var name = mesh.GetBlendShapeName(i);
                var weight = renderer.GetBlendShapeWeight(i);
                yield return (name, weight);
            }
        }

        public static Span<T> AsSpan<T>(this List<T> list)
        {
            var a = Unsafe.As<List<T>, DummyList<T>>(ref list);
            return new Span<T>(a.Item, 0, a.Size);
        }

        private class DummyList<T>
        {
            public T[] Item;
            public int Size;

            public Span<T> AsSpan() => Item.AsSpan(0, Size);
        }
    }
}
