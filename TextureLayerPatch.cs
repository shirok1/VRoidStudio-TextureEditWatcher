using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace Shiroki.VRoidStudioPlugin.TextureEditWatcher
{
    [BepInPlugin(PluginGuid, "Texture Edit Watcher", "1.0")]
    public class TextureLayerPatch : BaseUnityPlugin
    {
        private const string PluginGuid = "Shiroki.VRoidStudioPlugin.TextureEditWatcher";

        private static string ExportCachePath =>
            Path.Combine(Application.temporaryCachePath, nameof(TextureEditWatcher));

        private static Harmony _harmonyInstance;
        private static ManualLogSource _logger;

        private static ConfigEntry<string> _imageEditorPathConfig;

        public void Awake()
        {
            _imageEditorPathConfig = Config.Bind("Config", "ImageEditorPath",
                @"C:\Windows\System32\mspaint.exe", "Path to image editor executable");
        }

        public void Start()
        {
            var directoryInfo = new DirectoryInfo(ExportCachePath);
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
            var matchedMethods =
                AccessTools.TypeByName("VRoidStudio.GUI.AvatarEditor.TextureEditor.TextureEditorViewModel")
                    .GetNestedTypes(AccessTools.all)
                    .SelectMany(type => type.GetMethods(AccessTools.allDeclared))
                    .Where(method => method.GetParameters().Length != 0
                                     && method.GetParameters()[0]
                                         .ParameterType == typeof(SketchUniversal.IResponsePayload)
                                     && method.Name.Contains("<ExportLayerCoroutine>")).ToArray();
            if (matchedMethods.Any())
            {
                foreach (var method in matchedMethods)
                {
                    _harmonyInstance.Patch(method,
                        transpiler: new HarmonyMethod(typeof(MyPatch), nameof(MyPatch.ElcCallbackPatch)));
                    Logger.LogMessage($"Patched '{method.Name}' in '{method.DeclaringType?.Name ?? "null"}'");
                }
            }
            else
            {
                Logger.LogError("Can't find 'EnqueueCommand' callback!");
            }
        }

        private static class MyPatch
        {
            private static readonly Dictionary<string, FileChangeWatcherExcludeFirst> Watchers =
                new Dictionary<string, FileChangeWatcherExcludeFirst>();

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
                                            Path.Combine(
                                                ExportCachePath,
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
                MethodInfo fakeWriteAllBytesInfo =
                    AccessTools.Method(typeof(MyPatch), nameof(FakeWriteAllBytesForElcCallback));
                foreach (CodeInstruction code in instructions)
                    if (code.opcode == OpCodes.Call && (MethodInfo) code.operand == writeAllBytesInfo)
                        yield return new CodeInstruction(OpCodes.Call, fakeWriteAllBytesInfo);
                    else
                        yield return code;
            }

            public static void FakeWriteAllBytesForElcCallback(string path, byte[] bytes)
            {
                File.WriteAllBytes(path, bytes);
                if (Watchers.ContainsKey(new FileInfo(path).Name))
                {
                    Process.Start(_imageEditorPathConfig.Value, "\"" + path + "\"");
                    _logger.LogMessage("Starting external image editor");
                }
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


                var watcher = new FileChangeWatcherExcludeFirst(path);
                Watchers.Add(fileInfo.Name, watcher);
                watcher.Modified += delegate
                {
                    _logger.LogMessage($"{path} was changed externally, Now importing...");
                    watcher.Watcher.EnableRaisingEvents = false;
                    coroutineStarter.GetValue(
                        importLayerCoroutine.GetValue(
                            layerRefInfo.GetValue(), new[] {path}));
                    watcher.Dispose();
                    Watchers.Remove(fileInfo.Name);
                };
                _logger.LogMessage($"Start watching {path}");

                yield return exportLayerCoroutine.GetValue<IEnumerator>(layerRefInfo.GetValue(), path);
            }
        }
    }
}