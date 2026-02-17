
#if UNITY_EDITOR
/*
 * 形态键上限重设工具（仅编辑器）
 * 作者：LGC & M365 Copilot
 * 说明：为 SkinnedMeshRenderer 的 BlendShapes 设置新的上限（线性缩放），生成新 Mesh 资产并自动替换当前组件。
 * 菜单入口：LGC/形态键上限重设
 * 默认保存路径（信息区展示）：Assets/LGC/Tools/形态键上限重设
 * 生成网格保存路径：Assets/LGC/Tools/形态键上限重设/已生成/<物体名称>_<时间戳:yyyyMMdd_HH_mm_ss>/<源Mesh名>_Capped.asset
 * 注意：需要源 Mesh 开启 Read/Write Enabled。
 */

using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public class BlendShapeCapperWindow : EditorWindow
{
    private SkinnedMeshRenderer smr;
    private Mesh sourceMesh;
    private Vector2 scroll;
    private readonly Dictionary<int, float> perShapeCap = new Dictionary<int, float>(); // shapeIndex -> cap

    // 状态消息（在面板底部 HelpBox 显示）
    private string statusMessage = "准备就绪：请拖入含有 BlendShapes 的 SkinnedMeshRenderer。";
    private MessageType statusType = MessageType.Info;

    // 路径常量（按你要求修改）
    private const string kBaseFolder = "Assets/LGC/Tools/形态键上限重设";
    private const string kGeneratedFolder = "Assets/LGC/Tools/形态键上限重设/已生成";

    [MenuItem("LGC/形态键上限重设")]
    public static void ShowWindow()
    {
        var win = GetWindow<BlendShapeCapperWindow>("形态键上限重设");
        win.minSize = new Vector2(560, 420);
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("形态键上限重设（生成新网格并自动替换）", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        smr = (SkinnedMeshRenderer)EditorGUILayout.ObjectField("SkinnedMeshRenderer", smr, typeof(SkinnedMeshRenderer), true);
        sourceMesh = smr ? smr.sharedMesh : null;

        if (sourceMesh == null)
        {
            EditorGUILayout.HelpBox("请拖入含有 BlendShapes 的 SkinnedMeshRenderer。", MessageType.Info);
            SetStatus("提示：尚未指定 SkinnedMeshRenderer 或其 sharedMesh 为空。", MessageType.Info);
            DrawStatusArea();
            return;
        }

        if (!sourceMesh.isReadable)
        {
            EditorGUILayout.HelpBox("当前 Mesh 未开启 Read/Write（不可读）。请在模型导入设置中勾选 Read/Write Enabled，然后重新导入。", MessageType.Error);
            SetStatus("错误：源 Mesh 不可读（需开启 Read/Write Enabled）。", MessageType.Error);
            DrawStatusArea();
            return;
        }

        EditorGUILayout.LabelField($"源 Mesh：{sourceMesh.name}", EditorStyles.miniBoldLabel);
        EditorGUILayout.LabelField($"顶点数：{sourceMesh.vertexCount}，子网格：{sourceMesh.subMeshCount}，形态键：{sourceMesh.blendShapeCount}");

        // 当没有形态键时，给出提示并禁用后续操作
        if (sourceMesh.blendShapeCount == 0)
        {
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox("未在该模型找到形态键（BlendShapes）。请确认导入设置或模型内容。", MessageType.Warning);

            // 禁用按钮与底部区域提示
            SetStatus("警告：该 Mesh 不包含任何 BlendShapes，无法生成。", MessageType.Warning);

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(true))
            {
                GUILayout.Button("生成新网格并替换", GUILayout.Height(28));
            }

            EditorGUILayout.Space();
            DrawInfoArea();
            DrawStatusArea();
            return;
        }

        EditorGUILayout.Space();

        // 逐个形态键设置上限
        EnsurePerShapeCaps(sourceMesh);
        EditorGUILayout.LabelField("逐个形态键设置上限（0~100）：", EditorStyles.miniBoldLabel);

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

        // 生成按钮（默认：生成后直接替换当前 SMR）
        using (new EditorGUI.DisabledScope(sourceMesh.blendShapeCount == 0))
        {
            if (GUILayout.Button("生成新网格并替换", GUILayout.Height(28)))
            {
                GenerateAndReplace();
            }
        }

        EditorGUILayout.Space();
        DrawInfoArea();

        // 面板底部状态区域
        DrawStatusArea();
    }

    private void DrawInfoArea()
    {
        EditorGUILayout.HelpBox(
            "工作原理：将 BlendShape 的所有帧增量按 (上限/100) 线性缩放。\n" +
            "示例：上限设为 50，则新网格上该形态键拉到 100，等效为原网格的 50，从而减少叠加导致的穿模。\n\n" +
            "注意事项：\n" +
            $"• 默认保存路径：{kBaseFolder}\n" +
            $"• 生成网格保存路径：{kGeneratedFolder}/<物体名称>_<时间戳>\n" +
            "• 源 Mesh 必须开启 Read/Write Enabled。\n" +
            "• 若形态键包含多帧（如 0/25/50/100），每帧增量统一比例缩放，插值结构保持不变。\n" +
            "• 生成的新 Mesh 资产独立保存，不会破坏原始 FBX。\n" +
            "• 时间戳格式：yyyyMMdd_HH_mm_ss（例如 20251203_19_06_00）。",
            MessageType.Info
        );
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
        {
            if (!perShapeCap.ContainsKey(i))
                perShapeCap[i] = 100f;
        }
    }

    private void GenerateAndReplace()
    {
        var src = sourceMesh;
        if (src == null)
        {
            SetStatus("失败：未找到源 Mesh。", MessageType.Error);
            return;
        }
        if (!src.isReadable)
        {
            SetStatus("失败：源 Mesh 未开启 Read/Write。请在 Import Settings 勾选 Read/Write Enabled。", MessageType.Error);
            return;
        }
        if (src.blendShapeCount == 0)
        {
            SetStatus("失败：该 Mesh 不包含任何 BlendShapes，无法生成。", MessageType.Error);
            return;
        }
        if (smr == null)
        {
            SetStatus("失败：未指定 SkinnedMeshRenderer。", MessageType.Error);
            return;
        }

        // 计算每个形态键的缩放因子（cap/100）
        var caps = new Dictionary<int, float>();
        for (int i = 0; i < src.blendShapeCount; i++)
        {
            float cap = perShapeCap.TryGetValue(i, out float v) ? v : 100f;
            caps[i] = Mathf.Clamp(cap, 0f, 100f) / 100f;
        }

        // 创建新 Mesh
        Mesh newMesh;
        try
        {
            newMesh = CreateCappedMesh(src, caps);
        }
        catch (Exception e)
        {
            SetStatus($"失败：生成新网格时出现异常。\n{e.Message}", MessageType.Error);
            return;
        }

        // 生成网格保存路径：Assets/LGC/Tools/形态键上限重设/已生成/<物体名称>_<时间戳>/<源Mesh名>_Capped.asset
        string objName = smr.gameObject.name;
        string timestamp = DateTime.Now.ToString("yyyyMMdd_HH_mm_ss");
        string dir = $"{kGeneratedFolder}/{SanitizeFolderName(objName)}_{timestamp}";
        try
        {
            CreateFolderRecursive(kBaseFolder);      // 确保基础目录存在
            CreateFolderRecursive(kGeneratedFolder); // 确保“已生成”目录存在
            CreateFolderRecursive(dir);              // 创建本次对象目录
        }
        catch (Exception e)
        {
            SetStatus($"失败：创建保存目录时出现异常。\n{e.Message}", MessageType.Error);
            return;
        }

        string assetName = $"{src.name}_Capped.asset";
        string fullPath = Path.Combine(dir, assetName).Replace("\\", "/");

        try
        {
            AssetDatabase.CreateAsset(newMesh, AssetDatabase.GenerateUniqueAssetPath(fullPath));
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }
        catch (Exception e)
        {
            SetStatus($"失败：保存新网格资产时出现异常。\n{e.Message}", MessageType.Error);
            return;
        }

        // 自动替换到当前渲染器
        try
        {
            Undo.RecordObject(smr, "Replace SharedMesh with Capped");
            smr.sharedMesh = newMesh;
            EditorUtility.SetDirty(smr);
        }
        catch (Exception e)
        {
            SetStatus($"生成成功但替换到当前渲染器失败：\n{e.Message}\n资产路径：{fullPath}", MessageType.Warning);
            return;
        }

        SetStatus($"完成：已生成并替换。\n保存路径：{fullPath}", MessageType.Info);
    }

    private static string SanitizeFolderName(string name)
    {
        if (string.IsNullOrEmpty(name)) return "Unnamed";
        char[] invalid = Path.GetInvalidFileNameChars();
        foreach (var c in invalid)
        {
            name = name.Replace(c.ToString(), "_");
        }
        return name;
    }

    private static void CreateFolderRecursive(string path)
    {
        if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets")) return;
        var parts = path.Split('/');
        string current = parts[0]; // "Assets"
        for (int i = 1; i < parts.Length; i++)
        {
            string next = parts[i];
            string candidate = $"{current}/{next}";
            if (!AssetDatabase.IsValidFolder(candidate))
            {
                AssetDatabase.CreateFolder(current, next);
            }
            current = candidate;
        }
    }

    private Mesh CreateCappedMesh(Mesh src, Dictionary<int, float> capsScale)
    {
        int vCount = src.vertexCount;

        Mesh dst = new Mesh
        {
            name = src.name + "_Capped",
            indexFormat = src.indexFormat
        };

        // 复制基础数据
        dst.vertices = src.vertices;

        // 法线
        var normals = src.normals;
        if (normals != null && normals.Length == vCount) dst.normals = normals;
        else dst.RecalculateNormals();

        // 切线
        var tans = src.tangents;
        if (tans != null && tans.Length == vCount) dst.tangents = tans;

        // 顶点色
        var colors32 = src.colors32;
        if (colors32 != null && colors32.Length == vCount) dst.colors32 = colors32;

        // UV（最多 8 通道）
        for (int channel = 0; channel < 8; channel++)
        {
            var uvs = new List<Vector4>();
            src.GetUVs(channel, uvs);
            if (uvs != null && uvs.Count == vCount)
                dst.SetUVs(channel, uvs);
        }

        // 子网格
        dst.subMeshCount = src.subMeshCount;
        for (int sm = 0; sm < src.subMeshCount; sm++)
        {
            dst.SetTriangles(src.GetTriangles(sm), sm);
        }

        // Skin 数据（若存在）
        dst.bindposes = src.bindposes;
        var boneWeights = src.boneWeights;
        if (boneWeights != null && boneWeights.Length == vCount)
            dst.boneWeights = boneWeights;

        dst.bounds = src.bounds;

        // BlendShapes：读取每一帧并按 cap 缩放
        var deltaV = new Vector3[vCount];
        var deltaN = new Vector3[vCount];
        var deltaT = new Vector3[vCount];

        for (int shapeIndex = 0; shapeIndex < src.blendShapeCount; shapeIndex++)
        {
            string shapeName = src.GetBlendShapeName(shapeIndex);
            int frameCount = src.GetBlendShapeFrameCount(shapeIndex);

            float scale = capsScale.TryGetValue(shapeIndex, out float s) ? s : 1f;
            scale = Mathf.Max(0f, scale);

            for (int frame = 0; frame < frameCount; frame++)
            {
                float weight = src.GetBlendShapeFrameWeight(shapeIndex, frame);

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

                dst.AddBlendShapeFrame(shapeName, weight, deltaV, deltaN, deltaT);
            }
        }

        dst.RecalculateBounds();
        return dst;
    }
}
#endif
