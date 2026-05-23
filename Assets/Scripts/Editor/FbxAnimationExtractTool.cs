#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace ActiveRagdoll.Editor
{
    /// <summary>
    /// 从 FBX 内嵌 AnimationClip 批量复制为独立 .anim 资产
    /// Batch-copy embedded AnimationClips from FBX into standalone .anim assets
    /// </summary>
    public static class FbxAnimationExtractUtility
    {
        public const string DefaultSourceFolder = "Assets/MassiveGreatSword_AnimSet";
        public const string DefaultOutputFolder = "Assets/MassiveGreatSword_AnimSet/ExtractedClips";

        public sealed class ExtractResult
        {
            public int fbxScanned;
            public int clipsExtracted;
            public int clipsSkipped;
            public int failures;
            public List<string> logLines = new();
        }

        /// <summary>
        /// 执行批量提取
        /// Run batch extraction
        /// </summary>
        public static ExtractResult Extract(
            string sourceFolder,
            string outputFolder,
            bool preserveSubfolders,
            bool skipExisting)
        {
            var result = new ExtractResult();

            if (!ValidateAssetFolder(sourceFolder, "Source", out var sourceError))
            {
                result.logLines.Add(sourceError);
                result.failures++;
                return result;
            }

            if (!TryEnsureOutputFolder(outputFolder, out var outputError))
            {
                result.logLines.Add(outputError);
                result.failures++;
                return result;
            }

            sourceFolder = NormalizeAssetPath(sourceFolder);
            outputFolder = NormalizeAssetPath(outputFolder);

            if (!sourceFolder.EndsWith("/", StringComparison.Ordinal))
                sourceFolder += "/";

            var fbxPaths = FindFbxAssetPaths(sourceFolder);
            result.fbxScanned = fbxPaths.Count;

            try
            {
                AssetDatabase.StartAssetEditing();

                foreach (var fbxPath in fbxPaths)
                {
                    try
                    {
                        ExtractClipsFromFbx(
                            fbxPath,
                            sourceFolder,
                            outputFolder,
                            preserveSubfolders,
                            skipExisting,
                            result);
                    }
                    catch (Exception ex)
                    {
                        result.failures++;
                        AddLog(result, $"[FAIL] {fbxPath}: {ex.Message}");
                        Debug.LogException(ex);
                    }
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            AddLog(result,
                $"[DONE] FBX={result.fbxScanned} extracted={result.clipsExtracted} skipped={result.clipsSkipped} failed={result.failures}");

            return result;
        }

        static List<string> FindFbxAssetPaths(string sourceFolder)
        {
            var paths = new List<string>();
            var guids = AssetDatabase.FindAssets("t:Model", new[] { sourceFolder.TrimEnd('/') });

            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path))
                    continue;

                var ext = Path.GetExtension(path);
                if (!ext.Equals(".fbx", StringComparison.OrdinalIgnoreCase))
                    continue;

                paths.Add(NormalizeAssetPath(path));
            }

            paths.Sort(StringComparer.OrdinalIgnoreCase);
            return paths;
        }

        static void ExtractClipsFromFbx(
            string fbxPath,
            string sourceFolder,
            string outputFolder,
            bool preserveSubfolders,
            bool skipExisting,
            ExtractResult result)
        {
            var clips = CollectExtractableClips(fbxPath);
            if (clips.Count == 0)
            {
                AddLog(result, $"[WARN] No extractable clip: {fbxPath}");
                return;
            }

            var fbxFileName = Path.GetFileNameWithoutExtension(fbxPath);
            var useClipNameSuffix = clips.Count > 1;

            foreach (var clip in clips)
            {
                var outPath = BuildOutputPath(
                    fbxPath,
                    sourceFolder,
                    outputFolder,
                    preserveSubfolders,
                    fbxFileName,
                    clip.name,
                    useClipNameSuffix);

                if (AssetDatabase.LoadAssetAtPath<AnimationClip>(outPath) != null)
                {
                    if (skipExisting)
                    {
                        result.clipsSkipped++;
                        AddLog(result, $"[SKIP] {outPath}");
                        continue;
                    }

                    if (!AssetDatabase.DeleteAsset(outPath))
                    {
                        result.failures++;
                        AddLog(result, $"[FAIL] Could not delete existing: {outPath}");
                        continue;
                    }
                }

                EnsureAssetFolderExists(outPath);

                var copy = UnityEngine.Object.Instantiate(clip);
                copy.name = Path.GetFileNameWithoutExtension(outPath);

                AssetDatabase.CreateAsset(copy, outPath);
                EditorUtility.SetDirty(copy);

                result.clipsExtracted++;
                AddLog(result, $"[OK] {outPath}");
            }
        }

        static List<AnimationClip> CollectExtractableClips(string fbxPath)
        {
            var clips = new List<AnimationClip>();
            var seen = new HashSet<AnimationClip>();

            // 优先用 Representations：Unity 6 下 FBX 内嵌 clip 更稳定
            // Prefer representations — embedded FBX clips are more reliable on Unity 6
            CollectFromLoadedAssets(AssetDatabase.LoadAllAssetRepresentationsAtPath(fbxPath), clips, seen);

            if (clips.Count == 0)
                CollectFromLoadedAssets(AssetDatabase.LoadAllAssetsAtPath(fbxPath), clips, seen);

            return clips;
        }

        static void CollectFromLoadedAssets(
            UnityEngine.Object[] subAssets,
            List<AnimationClip> clips,
            HashSet<AnimationClip> seen)
        {
            if (subAssets == null)
                return;

            foreach (var obj in subAssets)
            {
                if (obj is not AnimationClip clip)
                    continue;

                if (!IsExtractableAnimationClip(clip))
                    continue;

                if (!seen.Add(clip))
                    continue;

                clips.Add(clip);
            }
        }

        /// <summary>
        /// 过滤可导出的动画片段（仅排除预览命名；不按 HideFlags 排除）
        /// Filter exportable clips (preview names only; do not filter by HideFlags)
        /// </summary>
        public static bool IsExtractableAnimationClip(AnimationClip clip)
        {
            if (clip == null)
                return false;

            // FBX 内嵌 clip 在 Unity 6 常带 NotEditable / HideInHierarchy，仍应提取
            // Embedded FBX clips are often NotEditable/HideInHierarchy but are still valid

            var name = clip.name;
            if (string.IsNullOrEmpty(name))
                return false;

            if (name.StartsWith("__", StringComparison.Ordinal))
                return false;

            return true;
        }

        static string BuildOutputPath(
            string fbxPath,
            string sourceFolder,
            string outputFolder,
            bool preserveSubfolders,
            string fbxFileName,
            string clipName,
            bool useClipNameSuffix)
        {
            var fileName = useClipNameSuffix
                ? $"{fbxFileName}_{SanitizeFileName(clipName)}.anim"
                : $"{fbxFileName}.anim";

            if (preserveSubfolders)
            {
                var relative = fbxPath.StartsWith(sourceFolder, StringComparison.OrdinalIgnoreCase)
                    ? fbxPath.Substring(sourceFolder.Length)
                    : Path.GetFileName(fbxPath);

                relative = relative.TrimStart('/', '\\');
                var relativeDir = Path.GetDirectoryName(relative);
                relativeDir = relativeDir?.Replace('\\', '/') ?? string.Empty;

                if (string.IsNullOrEmpty(relativeDir))
                    return $"{outputFolder}/{fileName}";

                return $"{outputFolder}/{relativeDir}/{fileName}";
            }

            return $"{outputFolder}/{fileName}";
        }

        static string SanitizeFileName(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');

            return name;
        }

        static void EnsureAssetFolderExists(string assetPath)
        {
            var dir = Path.GetDirectoryName(assetPath);
            if (string.IsNullOrEmpty(dir))
                return;

            dir = dir.Replace('\\', '/');
            if (AssetDatabase.IsValidFolder(dir))
                return;

            var parts = dir.Split('/');
            if (parts.Length < 2 || parts[0] != "Assets")
                return;

            var current = parts[0];
            for (var i = 1; i < parts.Length; i++)
            {
                var next = $"{current}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);

                current = next;
            }
        }

        public static bool ValidateAssetFolder(string path, string label, out string error)
        {
            error = null;

            if (string.IsNullOrWhiteSpace(path))
            {
                error = $"[{label}] Path is empty.";
                return false;
            }

            path = NormalizeAssetPath(path);

            if (!path.StartsWith("Assets/", StringComparison.Ordinal) && path != "Assets")
            {
                error = $"[{label}] Path must start with Assets/: {path}";
                return false;
            }

            if (!AssetDatabase.IsValidFolder(path))
            {
                error = $"[{label}] Folder not found: {path}";
                return false;
            }

            return true;
        }

        /// <summary>
        /// 确保输出根目录存在（不存在则创建）
        /// Ensure output root exists (create if missing)
        /// </summary>
        public static bool TryEnsureOutputFolder(string path, out string error)
        {
            error = null;

            if (string.IsNullOrWhiteSpace(path))
            {
                error = "[Output] Path is empty.";
                return false;
            }

            path = NormalizeAssetPath(path);

            if (!path.StartsWith("Assets/", StringComparison.Ordinal))
            {
                error = $"[Output] Path must start with Assets/: {path}";
                return false;
            }

            if (AssetDatabase.IsValidFolder(path))
                return true;

            EnsureAssetFolderExists($"{path}/placeholder.anim");
            if (!AssetDatabase.IsValidFolder(path))
            {
                error = $"[Output] Could not create folder: {path}";
                return false;
            }

            return true;
        }

        public static string NormalizeAssetPath(string path)
        {
            return path.Replace('\\', '/').TrimEnd('/');
        }

        /// <summary>
        /// 绝对路径转 Assets 相对路径；失败返回 null
        /// Convert absolute path to Assets-relative; null on failure
        /// </summary>
        public static string TryAbsolutePathToAssetsPath(string absolutePath)
        {
            if (string.IsNullOrWhiteSpace(absolutePath))
                return null;

            absolutePath = Path.GetFullPath(absolutePath).Replace('\\', '/');
            var dataPath = Application.dataPath.Replace('\\', '/');

            if (!absolutePath.StartsWith(dataPath, StringComparison.OrdinalIgnoreCase))
                return null;

            var relative = "Assets" + absolutePath.Substring(dataPath.Length);
            return NormalizeAssetPath(relative);
        }

        static void AddLog(ExtractResult result, string line)
        {
            result.logLines.Add(line);
            Debug.Log($"[FbxAnimationExtract] {line}");
        }
    }

    /// <summary>
    /// FBX 动画提取工具窗口
    /// FBX animation extraction tool window
    /// </summary>
    public sealed class FbxAnimationExtractWindow : EditorWindow
    {
        const string PrefsPrefix = "ActiveRagdoll.FbxExtract.";

        string _sourceFolder = FbxAnimationExtractUtility.DefaultSourceFolder;
        string _outputFolder = FbxAnimationExtractUtility.DefaultOutputFolder;
        bool _preserveSubfolders = true;
        bool _skipExisting;
        Vector2 _logScroll;
        FbxAnimationExtractUtility.ExtractResult _lastResult;
        List<string> _displayLogLines = new();

        [MenuItem("Tools/Active Ragdoll/Extract MassiveGreatSword Animations")]
        static void OpenWindow()
        {
            var window = GetWindow<FbxAnimationExtractWindow>(true, "FBX Animation Extract", true);
            window.minSize = new Vector2(480f, 420f);
            window.LoadPrefs();
            window.Show();
        }

        void OnEnable() => LoadPrefs();

        void LoadPrefs()
        {
            _sourceFolder = EditorPrefs.GetString(PrefsPrefix + "Source", FbxAnimationExtractUtility.DefaultSourceFolder);
            _outputFolder = EditorPrefs.GetString(PrefsPrefix + "Output", FbxAnimationExtractUtility.DefaultOutputFolder);
            _preserveSubfolders = EditorPrefs.GetBool(PrefsPrefix + "PreserveSubfolders", true);
            _skipExisting = EditorPrefs.GetBool(PrefsPrefix + "SkipExisting", false);
        }

        void SavePrefs()
        {
            EditorPrefs.SetString(PrefsPrefix + "Source", _sourceFolder);
            EditorPrefs.SetString(PrefsPrefix + "Output", _outputFolder);
            EditorPrefs.SetBool(PrefsPrefix + "PreserveSubfolders", _preserveSubfolders);
            EditorPrefs.SetBool(PrefsPrefix + "SkipExisting", _skipExisting);
        }

        void OnGUI()
        {
            EditorGUILayout.LabelField("FBX Animation Extract / FBX 动画提取", EditorStyles.boldLabel);
            EditorGUILayout.Space(4f);

            DrawFolderField("Source Folder / 源目录", ref _sourceFolder);
            DrawFolderField("Output Folder / 输出目录", ref _outputFolder);

            EditorGUILayout.Space(4f);
            _preserveSubfolders = EditorGUILayout.Toggle(
                new GUIContent(
                    "Preserve Subfolders / 保留子目录结构",
                    "Mirror relative paths under the output folder (recommended).\n在输出目录下镜像源目录相对路径（推荐）。"),
                _preserveSubfolders);

            _skipExisting = EditorGUILayout.Toggle(
                new GUIContent(
                    "Skip Existing / 仅新增",
                    "Do not overwrite existing .anim files.\n不覆盖已存在的 .anim 文件。"),
                _skipExisting);

            if (!_preserveSubfolders)
            {
                EditorGUILayout.HelpBox(
                    "平铺输出可能导致同名 .anim 互相覆盖，仅适合快速试验。\n" +
                    "Flat output may overwrite duplicate filenames — quick tests only.",
                    MessageType.Warning);
            }

            EditorGUILayout.Space(8f);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Reset Defaults / 恢复默认", GUILayout.Height(28f)))
            {
                _sourceFolder = FbxAnimationExtractUtility.DefaultSourceFolder;
                _outputFolder = FbxAnimationExtractUtility.DefaultOutputFolder;
                _preserveSubfolders = true;
                _skipExisting = false;
            }

            GUI.enabled = !string.IsNullOrWhiteSpace(_sourceFolder) && !string.IsNullOrWhiteSpace(_outputFolder);
            if (GUILayout.Button("Extract / 提取", GUILayout.Height(28f)))
                RunExtract();
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();

            DrawSummaryAndLog();
        }

        void DrawFolderField(string label, ref string folderPath)
        {
            EditorGUILayout.BeginHorizontal();
            folderPath = EditorGUILayout.TextField(label, folderPath);
            if (GUILayout.Button("Browse", GUILayout.Width(64f)))
            {
                var absolute = EditorUtility.OpenFolderPanel(label, Application.dataPath, "");
                if (!string.IsNullOrEmpty(absolute))
                {
                    var assetsPath = FbxAnimationExtractUtility.TryAbsolutePathToAssetsPath(absolute);
                    if (assetsPath != null)
                        folderPath = assetsPath;
                    else
                        EditorUtility.DisplayDialog(
                            "Invalid Folder / 无效文件夹",
                            "请选择项目 Assets 目录内的文件夹。\nPlease select a folder inside the project Assets directory.",
                            "OK");
                }
            }

            EditorGUILayout.EndHorizontal();
        }

        void RunExtract()
        {
            SavePrefs();

            _sourceFolder = FbxAnimationExtractUtility.NormalizeAssetPath(_sourceFolder);
            _outputFolder = FbxAnimationExtractUtility.NormalizeAssetPath(_outputFolder);

            if (!FbxAnimationExtractUtility.ValidateAssetFolder(_sourceFolder, "Source", out var sourceError))
            {
                EditorUtility.DisplayDialog("Extract Failed / 提取失败", sourceError, "OK");
                return;
            }

            _outputFolder = FbxAnimationExtractUtility.NormalizeAssetPath(_outputFolder);
            if (!FbxAnimationExtractUtility.TryEnsureOutputFolder(_outputFolder, out var outputError))
            {
                EditorUtility.DisplayDialog("Extract Failed / 提取失败", outputError, "OK");
                return;
            }

            if (!_skipExisting &&
                !EditorUtility.DisplayDialog(
                    "Confirm Extract / 确认提取",
                    $"将从\n{_sourceFolder}\n提取动画到\n{_outputFolder}\n\n" +
                    "已存在的 .anim 将被覆盖。继续？\nExisting .anim files will be overwritten. Continue?",
                    "Extract / 提取",
                    "Cancel / 取消"))
            {
                return;
            }

            _lastResult = FbxAnimationExtractUtility.Extract(
                _sourceFolder,
                _outputFolder,
                _preserveSubfolders,
                _skipExisting);

            const int maxLogLines = 50;
            _displayLogLines = _lastResult.logLines;
            if (_displayLogLines.Count > maxLogLines)
                _displayLogLines = _displayLogLines.GetRange(_displayLogLines.Count - maxLogLines, maxLogLines);

            Repaint();
        }

        void DrawSummaryAndLog()
        {
            EditorGUILayout.Space(8f);

            if (_lastResult != null)
            {
                EditorGUILayout.HelpBox(
                    $"FBX scanned: {_lastResult.fbxScanned} | Extracted: {_lastResult.clipsExtracted} | " +
                    $"Skipped: {_lastResult.clipsSkipped} | Failed: {_lastResult.failures}\n" +
                    $"扫描 FBX：{_lastResult.fbxScanned} | 提取：{_lastResult.clipsExtracted} | " +
                    $"跳过：{_lastResult.clipsSkipped} | 失败：{_lastResult.failures}",
                    _lastResult.failures > 0 ? MessageType.Warning : MessageType.Info);
            }

            EditorGUILayout.LabelField("Log (latest) / 日志（最近）", EditorStyles.boldLabel);
            _logScroll = EditorGUILayout.BeginScrollView(_logScroll, GUILayout.MinHeight(140f));

            if (_displayLogLines == null || _displayLogLines.Count == 0)
            {
                EditorGUILayout.LabelField("No log yet. Run Extract to see results.\n尚无日志，请点击提取。");
            }
            else
            {
                foreach (var line in _displayLogLines)
                    EditorGUILayout.LabelField(line, EditorStyles.wordWrappedMiniLabel);
            }

            EditorGUILayout.EndScrollView();
        }
    }
}
#endif
