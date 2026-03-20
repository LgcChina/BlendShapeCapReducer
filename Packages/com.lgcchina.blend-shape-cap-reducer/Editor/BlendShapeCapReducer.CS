
#if UNITY_EDITOR
/*
 * 形态键上限重设工具（仅编辑器）
 * 作者：LGC & M365 Copilot
 *
 * 更新点（修复“清除按钮未成功移除物体并更新界面”）：
 * - 在清除按钮点击的帧，短路后续对象切换与UI逻辑：显式置空 newSmr/smr，清空会话状态，刷新界面并 return。
 * - 使用 Repaint() + GUI.FocusControl(null) + GUIUtility.keyboardControl = 0，确保 UI 焦点与绘制立即更新。
 *
 * 其它功能：
 * 1) 顶部语言切换：中文（默认）/ English / 日本語。
 * 2) SkinnedMeshRenderer 槽位右侧「清除」按钮：清空槽位并重置工具状态（不删除场景物体）。
 * 3) 逐个 BlendShape 上限设置，生成新 Mesh 并替换；生成成功后按钮禁用防重复。
 * 4) “还原网格（撤销本次生成）”：恢复到拖入时记录的原始网格，并删除生成目录与资产。
 * 5) “从FBX还原网格”（底部固定、独立）：优先手动选择 .fbx；未选择时自动定位（Prefab 源对象或父级 Animator.avatar），
 *    在 FBX 子资产中按对象名匹配 Mesh 并替换；成功后更新“当前网格（引用）”。
 */

using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public class BlendShapeCapperWindow : EditorWindow
{
    // ===== 语言支持 =====
    private enum UILang { ZhCN, En, Ja }
    private UILang uiLang = UILang.ZhCN;
    private Strings strings = Strings.For(UILang.ZhCN);

    private class Strings
    {
        public string ToolTitle, LangLabel, SkinnedMeshRendererLabel, ClearBtn;
        public string RefInfoTitle, SourceMeshLabel, CurrentMeshLabel, LastGenMeshLabel;
        public string CapsTitle, GenerateBtn, UndoBtn, BottomFbxTitle, ManualFbxLabel, BottomFbxButton;
        public string StatusReady, StatusNoSMR, ErrorNotReadable, WarnNoBlendShapes;
        public string StatusRecordedFmt, StatusGeneratedDone, StatusUndoDoneFmt, StatusUndoDoneNoDir;
        public string ErrorUndoNoSource, ErrorNoFBXSelected, ErrorAutoLocateFBXFailed, ErrorFBXNoChildren;
        public string StatusAutoLocatedFmt, WarnAutoLocateMissing, WarnFbxNoMatchFmt, StatusFbxRestoredFmt;

        public static Strings For(UILang lang)
        {
            var s = new Strings();
            switch (lang)
            {
                case UILang.En:
                    s.ToolTitle = "BlendShape Cap (Generate & Replace)";
                    s.LangLabel = "Language";
                    s.SkinnedMeshRendererLabel = "SkinnedMeshRenderer";
                    s.ClearBtn = "Clear";
                    s.RefInfoTitle = "References";
                    s.SourceMeshLabel = "Source Mesh";
                    s.CurrentMeshLabel = "Current Mesh";
                    s.LastGenMeshLabel = "Last Generated Mesh";
                    s.CapsTitle = "Per-BlendShape Cap (0~100)";
                    s.GenerateBtn = "Generate New Mesh & Replace";
                    s.UndoBtn = "Restore Mesh (Undo This Generation)";
                    s.BottomFbxTitle = "Restore Mesh from FBX (Independent)";
                    s.ManualFbxLabel = "Original FBX (optional)";
                    s.BottomFbxButton = "Restore from FBX";
                    s.StatusReady = "Ready: drag a SkinnedMeshRenderer.";
                    s.StatusNoSMR = "Hint: SkinnedMeshRenderer not assigned or sharedMesh is null.";
                    s.ErrorNotReadable = "Error: Mesh is not Read/Write Enabled.";
                    s.WarnNoBlendShapes = "No BlendShapes found.";
                    s.StatusRecordedFmt = "Recorded original mesh: {0}";
                    s.StatusGeneratedDone = "Done: generated and replaced.";
                    s.StatusUndoDoneFmt = "Restored and deleted generated directory: {0}";
                    s.StatusUndoDoneNoDir = "Restored to original mesh.";
                    s.ErrorUndoNoSource = "Undo failed: original mesh not recorded.";
                    s.ErrorNoFBXSelected = "Restore failed: select a valid .fbx file.";
                    s.ErrorAutoLocateFBXFailed = "Restore failed: cannot auto-locate FBX.";
                    s.ErrorFBXNoChildren = "FBX has no child assets.";
                    s.StatusAutoLocatedFmt = "Auto-located FBX: {0}";
                    s.WarnAutoLocateMissing = "Cannot auto-locate FBX, please select a .fbx file.";
                    s.WarnFbxNoMatchFmt = "No matching Mesh found in FBX for object name: {0}";
                    s.StatusFbxRestoredFmt = "Restored mesh from FBX: {0}";
                    break;

                case UILang.Ja:
                    s.ToolTitle = "BlendShape上限調整（生成して置換）";
                    s.LangLabel = "言語";
                    s.SkinnedMeshRendererLabel = "SkinnedMeshRenderer";
                    s.ClearBtn = "クリア";
                    s.RefInfoTitle = "参照情報";
                    s.SourceMeshLabel = "ソースメッシュ";
                    s.CurrentMeshLabel = "現在のメッシュ";
                    s.LastGenMeshLabel = "最新生成メッシュ";
                    s.CapsTitle = "ブレンドシェイプ上限（0〜100）";
                    s.GenerateBtn = "新規メッシュ生成＆置換";
                    s.UndoBtn = "メッシュを元に戻す（今回の生成を撤回）";
                    s.BottomFbxTitle = "FBXからメッシュを復元（独立）";
                    s.ManualFbxLabel = "元FBX（任意）";
                    s.BottomFbxButton = "FBXから復元";
                    s.StatusReady = "準備完了：SkinnedMeshRendererをドラッグしてください。";
                    s.StatusNoSMR = "ヒント：SkinnedMeshRenderer未指定、またはsharedMeshがnullです。";
                    s.ErrorNotReadable = "エラー：メッシュがRead/Write有効ではありません。";
                    s.WarnNoBlendShapes = "BlendShapesが見つかりません。";
                    s.StatusRecordedFmt = "元メッシュを記録しました：{0}";
                    s.StatusGeneratedDone = "完了：生成して置換しました。";
                    s.StatusUndoDoneFmt = "元に戻し、生成ディレクトリを削除しました：{0}";
                    s.StatusUndoDoneNoDir = "元メッシュへ復元しました。";
                    s.ErrorUndoNoSource = "撤回失敗：元メッシュが記録されていません。";
                    s.ErrorNoFBXSelected = "復元失敗：有効な .fbx を選択してください。";
                    s.ErrorAutoLocateFBXFailed = "復元失敗：FBXを自動特定できません。";
                    s.ErrorFBXNoChildren = "FBXに子アセットがありません。";
                    s.StatusAutoLocatedFmt = "自動特定したFBX：{0}";
                    s.WarnAutoLocateMissing = "FBXを自動特定できません。 .fbx を選択してください。";
                    s.WarnFbxNoMatchFmt = "FBXに一致するメッシュがありません。オブジェクト名：{0}";
                    s.StatusFbxRestoredFmt = "FBXからメッシュを復元しました：{0}";
                    break;

                default: // ZhCN
                    s.ToolTitle = "形态键上限重设工具";
                    s.LangLabel = "语言";
                    s.SkinnedMeshRendererLabel = "SkinnedMeshRenderer";
                    s.ClearBtn = "清除";
                    s.RefInfoTitle = "引用信息";
                    s.SourceMeshLabel = "源网格";
                    s.CurrentMeshLabel = "当前网格";
                    s.LastGenMeshLabel = "最新生成网格";
                    s.CapsTitle = "逐个形态键上限（0~100）";
                    s.GenerateBtn = "生成新网格并替换";
                    s.UndoBtn = "还原网格（撤销本次生成）";
                    s.BottomFbxTitle = "从FBX还原网格（独立）";
                    s.ManualFbxLabel = "原始FBX（可选）";
                    s.BottomFbxButton = "从FBX还原网格";
                    s.StatusReady = "准备就绪：请拖入含有 BlendShapes 的 SkinnedMeshRenderer。";
                    s.StatusNoSMR = "提示：尚未指定 SkinnedMeshRenderer 或其 sharedMesh为空。";
                    s.ErrorNotReadable = "错误：Mesh 未开启 Read/Write。";
                    s.WarnNoBlendShapes = "未找到形态键（BlendShapes）。";
                    s.StatusRecordedFmt = "已记录原始网格：{0}";
                    s.StatusGeneratedDone = "完成：已生成并替换。";
                    s.StatusUndoDoneFmt = "已还原并删除生成目录：{0}";
                    s.StatusUndoDoneNoDir = "已还原到原始网格。";
                    s.ErrorUndoNoSource = "撤销失败：未记录原始网格。";
                    s.ErrorNoFBXSelected = "还原失败：请选择有效 .fbx 文件。";
                    s.ErrorAutoLocateFBXFailed = "还原失败：未能自动定位 FBX。";
                    s.ErrorFBXNoChildren = "FBX 内无子资产。";
                    s.StatusAutoLocatedFmt = "自动定位到 FBX：{0}";
                    s.WarnAutoLocateMissing = "未能自动定位 FBX，请选择 .fbx 文件。";
                    s.WarnFbxNoMatchFmt = "FBX 中未找到匹配 Mesh：{0}";
                    s.StatusFbxRestoredFmt = "已从 FBX 还原网格：{0}";
                    break;
            }
            return s;
        }
    }

    // ===== 字段（统一声明） =====
    private SkinnedMeshRenderer smr;      // 当前对象
    private Mesh sourceMesh;               // 内部快照
    private Mesh currentMeshRef;           // 面板显示：当前网格

    private Mesh recordedOriginalMesh;     // 拖入时记录的原始网格（撤销用）
    private Mesh lastGeneratedMeshRef;     // 最新生成网格
    private string lastGeneratedDir;       // 最新生成目录
    private bool hasGeneratedThisSession = false; // 控制生成按钮禁用

    private UnityEngine.Object manualFbxAsset; // 用户选择的 .fbx（从FBX还原独立使用）

    private Vector2 scroll;
    private string statusMessage = "准备就绪：请拖入含有 BlendShapes 的 SkinnedMeshRenderer。";
    private MessageType statusType = MessageType.Info;

    private readonly Dictionary<int, float> perShapeCap = new Dictionary<int, float>();

    private const string kBaseFolder = "Assets/LGC/Tools/形态键上限重设";
    private const string kGeneratedFolder = "Assets/LGC/Tools/形态键上限重设/已生成";

    [MenuItem("LGC/LGC_BlendShapeCapReducer_形态键上限重设")]
    public static void ShowWindow()
    {
        var win = GetWindow<BlendShapeCapperWindow>("LGC_BlendShapeCapReducer_形态键上限重设");
        win.minSize = new Vector2(700, 600);
        win.statusMessage = win.strings.StatusReady;
        win.statusType = MessageType.Info;
    }

    private void OnDisable() => ClearSessionRecord();

    private void OnGUI()
    {
        // 顶部：语言切换
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField(strings.LangLabel, GUILayout.Width(60));
        var newLang = (UILang)EditorGUILayout.EnumPopup(uiLang);
        if (newLang != uiLang)
        {
            uiLang = newLang;
            strings = Strings.For(uiLang);
            statusMessage = strings.StatusReady;
            statusType = MessageType.Info;
            Repaint();
        }
        EditorGUILayout.EndHorizontal();

        // 对象选择 + 右侧清除按钮（清除=移除当前填入的物体引用）
        EditorGUILayout.BeginHorizontal();
        var newSmr = (SkinnedMeshRenderer)EditorGUILayout.ObjectField(strings.SkinnedMeshRendererLabel, smr, typeof(SkinnedMeshRenderer), true);
        bool clearClicked = GUILayout.Button(strings.ClearBtn, GUILayout.Width(60));
        EditorGUILayout.EndHorizontal();

        if (clearClicked)
        {
            // —— 关键：清除本帧的 ObjectField 值影响，短路后续逻辑 —— //
            // 彻底清空槽位并重置工具状态（不删除场景物体）
            ClearSessionRecord();
            smr = null;
            sourceMesh = null;
            currentMeshRef = null;
            manualFbxAsset = null;

            // 释放焦点并刷新
            GUI.FocusControl(null);
            GUIUtility.keyboardControl = 0;
            SetStatus(strings.StatusReady, MessageType.Info);

            // 绘制基础UI（空态）
            DrawReferenceInfoArea();
            DrawStatusArea();
            DrawFbxRestoreArea(); // 底部固定
            Repaint();
            return; // 🔴 本帧直接返回，避免 old newSmr 被后续逻辑采纳
        }

        // —— 正常处理对象切换 —— //
        if (newSmr != smr)
        {
            ClearSessionRecord(); // 切换对象：重置状态，生成按钮可用
            smr = newSmr;
            sourceMesh = smr ? smr.sharedMesh : null;
            currentMeshRef = sourceMesh;

            if (smr != null && sourceMesh != null)
            {
                recordedOriginalMesh = sourceMesh; // 拖入即记录
                SetStatus(string.Format(strings.StatusRecordedFmt, recordedOriginalMesh.name), MessageType.Info);
            }
            else
            {
                SetStatus(strings.StatusNoSMR, MessageType.Info);
            }
        }
        else
        {
            sourceMesh = smr ? smr.sharedMesh : null;
            currentMeshRef = sourceMesh;
        }

        // 引用信息
        DrawReferenceInfoArea();

        if (sourceMesh == null)
        {
            DrawStatusArea();
            DrawFbxRestoreArea(); // 底部固定
            return;
        }

        if (!sourceMesh.isReadable)
        {
            EditorGUILayout.HelpBox(strings.ErrorNotReadable, MessageType.Error);
            DrawStatusArea();
            DrawFbxRestoreArea();
            return;
        }

        // 无 BlendShapes：生成按钮置灰
        if (sourceMesh.blendShapeCount == 0)
        {
            EditorGUILayout.HelpBox(strings.WarnNoBlendShapes, MessageType.Warning);
            using (new EditorGUI.DisabledScope(true))
                GUILayout.Button(strings.GenerateBtn, GUILayout.Height(28));
            DrawStatusArea();
            DrawFbxRestoreArea();
            return;
        }

        // 形态键上限设置（滚动）
        EditorGUILayout.Space();
        EnsurePerShapeCaps(sourceMesh);
        EditorGUILayout.LabelField(strings.CapsTitle, EditorStyles.miniBoldLabel);

        scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.Height(260));
        for (int i = 0; i < sourceMesh.blendShapeCount; i++)
        {
            string name = sourceMesh.GetBlendShapeName(i);
            float cap = perShapeCap.TryGetValue(i, out float v) ? v : 100f;
            cap = EditorGUILayout.Slider($"{i}. {name}", cap, 0f, 100f);
            perShapeCap[i] = cap;
        }
        EditorGUILayout.EndScrollView();

        EditorGUILayout.Space();

        // 生成按钮：生成后禁用；撤销/清除/切换对象/关闭窗口才重新启用
        using (new EditorGUI.DisabledScope(hasGeneratedThisSession))
        {
            if (GUILayout.Button(strings.GenerateBtn, GUILayout.Height(28)))
                GenerateAndReplace();
        }

        // 生成成功后显示撤销按钮
        if (hasGeneratedThisSession)
        {
            if (GUILayout.Button(strings.UndoBtn, GUILayout.Height(26)))
                RestoreRecordedOriginalMeshAndDeleteGenerated();
        }

        DrawStatusArea();
        DrawFbxRestoreArea(); // 底部固定
    }

    // 引用信息（源/当前/最新生成）
    private void DrawReferenceInfoArea()
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField(strings.RefInfoTitle, EditorStyles.miniBoldLabel);

        using (new EditorGUI.DisabledScope(true))
            recordedOriginalMesh = (Mesh)EditorGUILayout.ObjectField(strings.SourceMeshLabel, recordedOriginalMesh, typeof(Mesh), false);

        using (new EditorGUI.DisabledScope(true))
            currentMeshRef = (Mesh)EditorGUILayout.ObjectField(strings.CurrentMeshLabel, currentMeshRef, typeof(Mesh), false);

        using (new EditorGUI.DisabledScope(true))
            lastGeneratedMeshRef = (Mesh)EditorGUILayout.ObjectField(strings.LastGenMeshLabel, lastGeneratedMeshRef, typeof(Mesh), false);
    }

    private void ClearSessionRecord()
    {
        recordedOriginalMesh = null;
        lastGeneratedMeshRef = null;
        lastGeneratedDir = null;
        hasGeneratedThisSession = false;
        manualFbxAsset = null;
        currentMeshRef = null;
        sourceMesh = null;
        perShapeCap.Clear();
        statusMessage = strings.StatusReady;
        statusType = MessageType.Info;
    }

    private void GenerateAndReplace()
    {
        if (sourceMesh == null || smr == null) { SetStatus(strings.StatusNoSMR, MessageType.Error); return; }
        if (!sourceMesh.isReadable) { SetStatus(strings.ErrorNotReadable, MessageType.Error); return; }
        if (sourceMesh.blendShapeCount == 0) { SetStatus(strings.WarnNoBlendShapes, MessageType.Error); return; }

        var caps = new Dictionary<int, float>();
        for (int i = 0; i < sourceMesh.blendShapeCount; i++)
        {
            float cap = perShapeCap.TryGetValue(i, out float v) ? v : 100f;
            caps[i] = Mathf.Clamp(cap, 0f, 100f) / 100f;
        }

        Mesh newMesh = CreateCappedMesh(sourceMesh, caps);

        string objName = smr.gameObject.name;
        string timestamp = DateTime.Now.ToString("yyyyMMdd_HH_mm_ss");
        string dir = $"{kGeneratedFolder}/{SanitizeFolderName(objName)}_{timestamp}";
        CreateFolderRecursive(kBaseFolder);
        CreateFolderRecursive(kGeneratedFolder);
        CreateFolderRecursive(dir);

        string assetName = $"{sourceMesh.name}_Capped.asset";
        string fullPath = Path.Combine(dir, assetName).Replace("\\", "/");
        AssetDatabase.CreateAsset(newMesh, AssetDatabase.GenerateUniqueAssetPath(fullPath));
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Undo.RecordObject(smr, "Replace SharedMesh with Capped");
        smr.sharedMesh = newMesh;
        EditorUtility.SetDirty(smr);

        sourceMesh = smr.sharedMesh;
        currentMeshRef = sourceMesh;
        lastGeneratedMeshRef = newMesh;
        lastGeneratedDir = dir;

        hasGeneratedThisSession = true;
        SetStatus(strings.StatusGeneratedDone, MessageType.Info);
    }

    private void RestoreRecordedOriginalMeshAndDeleteGenerated()
    {
        if (smr == null || recordedOriginalMesh == null)
        {
            SetStatus(strings.ErrorUndoNoSource, MessageType.Error);
            return;
        }

        Undo.RecordObject(smr, "Restore Original Mesh");
        smr.sharedMesh = recordedOriginalMesh;
        EditorUtility.SetDirty(smr);

        if (!string.IsNullOrEmpty(lastGeneratedDir))
        {
            DeleteFolderRecursively(lastGeneratedDir);
            SetStatus(string.Format(strings.StatusUndoDoneFmt, lastGeneratedDir), MessageType.Info);
        }
        else
        {
            SetStatus(strings.StatusUndoDoneNoDir, MessageType.Info);
        }

        sourceMesh = smr.sharedMesh;
        currentMeshRef = sourceMesh;
        lastGeneratedMeshRef = null;
        lastGeneratedDir = null;
        hasGeneratedThisSession = false;
    }

    private void DeleteFolderRecursively(string folderPath)
    {
        if (string.IsNullOrEmpty(folderPath) || !folderPath.StartsWith("Assets")) return;
        string[] guids = AssetDatabase.FindAssets("", new[] { folderPath });
        foreach (string guid in guids)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(guid);
            if (!AssetDatabase.IsValidFolder(assetPath))
                AssetDatabase.DeleteAsset(assetPath);
        }
        AssetDatabase.DeleteAsset(folderPath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    private void DrawFbxRestoreArea()
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField(strings.BottomFbxTitle, EditorStyles.boldLabel);

        string autoFbxPath = TryAutoLocateFbxPath();
        if (!string.IsNullOrEmpty(autoFbxPath))
            EditorGUILayout.HelpBox(string.Format(strings.StatusAutoLocatedFmt, autoFbxPath), MessageType.None);
        else
            EditorGUILayout.HelpBox(strings.WarnAutoLocateMissing, MessageType.Warning);

        manualFbxAsset = EditorGUILayout.ObjectField(strings.ManualFbxLabel, manualFbxAsset, typeof(UnityEngine.Object), false);

        if (GUILayout.Button(strings.BottomFbxButton, GUILayout.Height(26)))
            RestoreMesh_FromFBX_ByObjectName();
    }

    private string TryAutoLocateFbxPath()
    {
        if (smr == null) return null;

        var srcGo = PrefabUtility.GetCorrespondingObjectFromSource(smr.gameObject);
        if (srcGo != null)
        {
            string path = AssetDatabase.GetAssetPath(srcGo);
            if (!string.IsNullOrEmpty(path) && path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))
                return path;
        }

        var animator = smr.GetComponentInParent<Animator>();
        if (animator?.avatar != null)
        {
            string avatarPath = AssetDatabase.GetAssetPath(animator.avatar);
            if (!string.IsNullOrEmpty(avatarPath) && avatarPath.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))
                return avatarPath;
        }
        return null;
    }

    private void RestoreMesh_FromFBX_ByObjectName()
    {
        if (smr == null) { SetStatus(strings.StatusNoSMR, MessageType.Error); return; }

        string fbxPath = null;
        if (manualFbxAsset != null)
        {
            string p = AssetDatabase.GetAssetPath(manualFbxAsset);
            if (!string.IsNullOrEmpty(p) && p.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))
                fbxPath = p;
            else
            {
                SetStatus(strings.ErrorNoFBXSelected, MessageType.Error);
                return;
            }
        }
        else
        {
            fbxPath = TryAutoLocateFbxPath();
            if (string.IsNullOrEmpty(fbxPath))
            {
                SetStatus(strings.ErrorAutoLocateFBXFailed, MessageType.Error);
                return;
            }
        }

        var assets = AssetDatabase.LoadAllAssetsAtPath(fbxPath);
        if (assets == null || assets.Length == 0) { SetStatus(strings.ErrorFBXNoChildren, MessageType.Error); return; }

        string objName = smr.gameObject.name;
        Mesh found = null;

        foreach (var a in assets) { if (a is Mesh m && m.name == objName) { found = m; break; } }
        if (found == null) { foreach (var a in assets) { if (a is Mesh m && string.Equals(m.name, objName, StringComparison.OrdinalIgnoreCase)) { found = m; break; } } }
        if (found == null) { foreach (var a in assets) { if (a is Mesh m && m.name.IndexOf(objName, StringComparison.OrdinalIgnoreCase) >= 0) { found = m; break; } } }

        if (found == null) { SetStatus(string.Format(strings.WarnFbxNoMatchFmt, objName), MessageType.Warning); return; }

        Undo.RecordObject(smr, "Restore Mesh From FBX");
        smr.sharedMesh = found;
        EditorUtility.SetDirty(smr);

        sourceMesh = smr.sharedMesh;
        currentMeshRef = sourceMesh;
        SetStatus(string.Format(strings.StatusFbxRestoredFmt, found.name), MessageType.Info);
    }

    private void DrawStatusArea()
    {
        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(statusMessage, statusType);
    }

    private void SetStatus(string msg, MessageType type)
    {
        statusMessage = msg;
        statusType = type;
        Repaint();
    }

    private void EnsurePerShapeCaps(Mesh m)
    {
        for (int i = 0; i < m.blendShapeCount; i++)
            if (!perShapeCap.ContainsKey(i)) perShapeCap[i] = 100f;
    }

    private static string SanitizeFolderName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c.ToString(), "_");
        return string.IsNullOrEmpty(name) ? "Unnamed" : name;
    }

    private static void CreateFolderRecursive(string path)
    {
        if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets")) return;
        var parts = path.Split('/');
        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = parts[i];
            string candidate = $"{current}/{next}";
            if (!AssetDatabase.IsValidFolder(candidate))
                AssetDatabase.CreateFolder(current, next);
            current = candidate;
        }
    }

    private Mesh CreateCappedMesh(Mesh src, Dictionary<int, float> capsScale)
    {
        int vCount = src.vertexCount;
        Mesh dst = new Mesh { name = src.name + "_Capped", indexFormat = src.indexFormat };

        dst.vertices = src.vertices;
        dst.normals = src.normals;
        dst.tangents = src.tangents;
        dst.colors32 = src.colors32;

        for (int channel = 0; channel < 8; channel++)
        {
            var uvs = new List<Vector4>();
            src.GetUVs(channel, uvs);
            if (uvs.Count == vCount) dst.SetUVs(channel, uvs);
        }

        dst.subMeshCount = src.subMeshCount;
        for (int sm = 0; sm < src.subMeshCount; sm++) dst.SetTriangles(src.GetTriangles(sm), sm);

        dst.bindposes = src.bindposes;
        dst.boneWeights = src.boneWeights;
        dst.bounds = src.bounds;

        var deltaV = new Vector3[vCount];
        var deltaN = new Vector3[vCount];
        var deltaT = new Vector3[vCount];

        for (int shapeIndex = 0; shapeIndex < src.blendShapeCount; shapeIndex++)
        {
            string shapeName = src.GetBlendShapeName(shapeIndex);
            int frameCount = src.GetBlendShapeFrameCount(shapeIndex);
            float scale = capsScale.TryGetValue(shapeIndex, out float s) ? s : 1f;

            for (int frame = 0; frame < frameCount; frame++)
            {
                src.GetBlendShapeFrameVertices(shapeIndex, frame, deltaV, deltaN, deltaT);

                if (scale != 1f)
                {
                    for (int i = 0; i < vCount; i++)
                    {
                        deltaV[i] *= scale;
                        deltaN[i] *= scale;
                        deltaT[i] *= scale;
                    }
                }

                dst.AddBlendShapeFrame(shapeName, src.GetBlendShapeFrameWeight(shapeIndex, frame), deltaV, deltaN, deltaT);
            }
        }

        dst.RecalculateBounds();
        return dst;
    }
}
#endif
