using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using SketchUniversal;
using UnityEngine;

namespace Shiroki.VRoidStudioPlugin.TextureEditWatcher
{
    [BepInPlugin(PluginGuid, "Texture Edit Watcher", "1.0")]
    public class TextureLayerPatch : BaseUnityPlugin
    {
        private const string PluginGuid = "Shiroki.VRoidStudioPlugin.TextureEditWatcher";
        private const string ImageEditorPath = @"C:\Windows\System32\mspaint.exe";
        private static Harmony _harmonyInstance;
        private static ManualLogSource _logger;

        public void Start()
        {
            var directoryInfo =
                new DirectoryInfo(Path.Combine(Application.temporaryCachePath, nameof(TextureEditWatcher)));
            if (!directoryInfo.Exists) directoryInfo.Create();

            _logger = Logger;
            _harmonyInstance = new Harmony(PluginGuid);

            // Find the contextMenuItems initialize method and add plugin function behind
            Logger.LogMessage("Adding plugin button into menu...");
            _harmonyInstance.Patch(
                AccessTools.Method(
                    AccessTools.TypeByName("VRoidStudio.GUI.AvatarEditor.TextureEditor.TextureEditorTextureLayer"),
                    "OnViewModelChanged"),
                postfix: new HarmonyMethod(typeof(MyPatch)
                    .GetMethod(nameof(MyPatch.OnViewModelChangedPostfix))));
            Logger.LogMessage("Patched!");


            // Find the anonymous SketchPlugin.EnqueueCommand callback function in ExportLayerCoroutine
            Logger.LogMessage("Patching 'EnqueueCommand' callback in 'ExportLayerCoroutine'...");
            Type vmType = AccessTools.TypeByName("VRoidStudio.GUI.AvatarEditor.TextureEditor.TextureEditorViewModel");
            MethodInfo elcCallback = null;
            foreach (Type nestType in vmType.GetNestedTypes(AccessTools.all))
            foreach (MethodInfo method in nestType.GetMethods(AccessTools.allDeclared))
                if (method.GetParameters().Length != 0
                    && method.GetParameters()[0].ParameterType == typeof(IResponsePayload)
                    && method.Name.Contains("<ExportLayerCoroutine>"))
                {
                    Logger.LogMessage($"Anonymous callback found in class: {nestType} method:{method.Name}");
                    elcCallback = method;
                }

            if (elcCallback == null)
            {
                Logger.LogError("Can't find 'EnqueueCommand' callback!");
            }
            else
            {
                _harmonyInstance.Patch(elcCallback,
                    transpiler: new HarmonyMethod(typeof(MyPatch), nameof(MyPatch.ElcCallbackPatch)));
                Logger.LogMessage("Patched 'ExportLayerCoroutine'");
            }
        }

        private static class MyPatch
        {
            private static FileChangeWatcherExcludeFirst _currentWatcher;

            public static void OnViewModelChangedPostfix(object sender)
            {
                var textureLayerInstance = Traverse.Create(sender);
                textureLayerInstance.Field("contextMenuItems")
                    .Method("Add", typeof(object)).GetValue(
                        textureLayerInstance.Method("ConstructMenuButton", new[] {typeof(string), typeof(Action)})
                            .GetValue("Edit Externally Once",
                                (Action) delegate
                                {
                                    Traverse observableTextureLayer = textureLayerInstance.Property("BindingContext");
                                    Traverse layerRefInfo = observableTextureLayer.Property("ReferenceInfo");
                                    Traverse startCoroutine = textureLayerInstance.Property("ViewModel")
                                        .Method("StartCoroutine", new[] {typeof(IEnumerator)});
                                    startCoroutine.GetValue(
                                        ExternalEditCoroutine(layerRefInfo,
                                            GenerateTempPath(
                                                observableTextureLayer.Property("DisplayName").GetValue<string>()
                                                + "."
                                                + observableTextureLayer.Property("Info").Field("id").GetValue<string>()
                                                + ".png"),
                                            textureLayerInstance.Property("ViewModel"),
                                            startCoroutine));
                                }));
            }

            public static IEnumerable<CodeInstruction> ElcCallbackPatch(IEnumerable<CodeInstruction> instructions)
            {
                MethodInfo writeAllBytesInfo = AccessTools.Method(typeof(File), nameof(File.WriteAllBytes));
                MethodInfo fakeWriteAllBytesInfo = AccessTools.Method(typeof(MyPatch), nameof(FakeWriteAllBytes));
                foreach (CodeInstruction code in instructions)
                    if (code.opcode == OpCodes.Call && (MethodInfo) code.operand == writeAllBytesInfo)
                        yield return new CodeInstruction(OpCodes.Call, fakeWriteAllBytesInfo);
                    else
                        yield return code;
            }

            public static void FakeWriteAllBytes(string path, byte[] bytes)
            {
                File.WriteAllBytes(path, bytes);
                if (_currentWatcher != null)
                {
                    Process.Start(ImageEditorPath, "\"" + path + "\"");
                    _logger.LogMessage("Starting external image editor");
                }
            }

            private static string GenerateTempPath(string fileName)
            {
                return Path.Combine(Application.temporaryCachePath, nameof(TextureEditWatcher), fileName);
            }

            private static IEnumerator ExternalEditCoroutine
                (Traverse layerRefInfo, string path, Traverse viewModel, Traverse coroutineStarter)
            {
                var fileInfo = new FileInfo(path);
                path = fileInfo.FullName;

                Traverse exportLayerCoroutine = viewModel.Method("ExportLayerCoroutine",
                    new[] {AccessTools.TypeByName("TextureLayerReferenceInfo"), typeof(string)});
                Traverse importLayerCoroutine = viewModel.Method("ImportLayerCoroutine",
                    new[] {AccessTools.TypeByName("TextureLayerReferenceInfo"), typeof(string[])});


                _currentWatcher = new FileChangeWatcherExcludeFirst(path);
                _currentWatcher.Modified += delegate
                {
                    _logger.LogMessage($"{path} was changed externally, Now importing...");
                    _currentWatcher.Watcher.EnableRaisingEvents = false;
                    coroutineStarter.GetValue(
                        importLayerCoroutine.GetValue(
                            layerRefInfo.GetValue(), new[] {path}));
                    _currentWatcher.Dispose();
                    _currentWatcher = null;
                };
                _logger.LogMessage($"Start watching {path}");

                yield return exportLayerCoroutine.GetValue<IEnumerator>(layerRefInfo.GetValue(), path);
            }
        }
    }
}