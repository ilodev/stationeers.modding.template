using UnityEditor;
using UnityEngine;
using System.IO;
using System.Reflection;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEditor.Compilation;
using System.Linq;
using System.Runtime.CompilerServices;

[InitializeOnLoad]
public class ProjectSetupWizard : EditorWindow
{
    private static bool hasOpened = false;

    private string projectName;
    private string description = "";
    private string namespaceName;

    private Texture2D banner;

    // UI state for the description TextArea
    private Vector2 descScroll;

    static ProjectSetupWizard()
    {
        // Open wizard on project load
        EditorApplication.update += OpenOnLoad;
    }

    private static void OpenOnLoad()
    {
        if (!hasOpened)
        {
            hasOpened = true;
            ShowWindow();
        }
    }

    static Rect GetEditorMainWindowPos()
    {
        // Internal type: UnityEditor.ContainerWindow
        var containerWinType = typeof(Editor).Assembly.GetType("UnityEditor.ContainerWindow");
        var showModeField = containerWinType.GetField("m_ShowMode", BindingFlags.NonPublic | BindingFlags.Instance);
        var positionProp = containerWinType.GetProperty("position", BindingFlags.Public | BindingFlags.Instance);

        var windows = Resources.FindObjectsOfTypeAll(containerWinType);
        foreach (var win in windows)
        {
            int showmode = (int)showModeField.GetValue(win);
            if (showmode == 4) // 4 == main editor window
                return (Rect)positionProp.GetValue(win, null);
        }
        return new Rect(80, 80, 800, 600); // fallback
    }

    static void CenterOnMainWin(EditorWindow win, Vector2 size)
    {
        var main = GetEditorMainWindowPos();
        var x = main.x + (main.width - size.x) * 0.5f;
        var y = main.y + (main.height - size.y) * 0.5f;
        win.position = new Rect(Mathf.Round(x), Mathf.Round(y), size.x, size.y);
    }

    [MenuItem("Tools/Project Setup Wizard")]
    public static void ShowWindow()
    {
        var window = GetWindow<ProjectSetupWizard>(utility: false, title: "Project Setup Wizard");
        var size = new Vector2(400, 320);     // your preferred size
        window.minSize = size;
        // Delay one tick so the main window is initialized when opening on load
        EditorApplication.delayCall += () => CenterOnMainWin(window, size);
        window.Show();
    }


    [MenuItem("Tools/Project Setup Wizard (Modal)")]
    public static void ShowWindowModal()
    {
        var win = CreateInstance<ProjectSetupWizard>();
        win.titleContent = new GUIContent("Project Setup Wizard");
        var size = new Vector2(400, 320);
        win.minSize = size;
        CenterOnMainWin(win, size); // set position BEFORE showing
        win.ShowModalUtility();     // <-- modal: blocks other editor windows until closed
    }


    [MenuItem("Tools/Project Setup Wizard Test (Modal)")]
    public static void ShowWindowModalTest()
    {
        if (EditorUtility.DisplayDialog("Project Setup", "We need to complete setup now.", "OK", "Cancel"))
        {
            var win = ScriptableObject.CreateInstance<ProjectSetupWizard>();
            CenterOnMainWin(win, new Vector2(400, 320)); // your helper
            win.ShowModalUtility();
        }
    }


    private void OnEnable()
    {
        string projectPath = Application.dataPath;
        projectName = Path.GetFileName(Path.GetDirectoryName(projectPath));

        // Default namespace suggestion
        if (string.IsNullOrEmpty(namespaceName))
            namespaceName = projectName.Replace(" ", "_");

        // Find path of this script
        string scriptPath = AssetDatabase.GetAssetPath(MonoScript.FromScriptableObject(this));
        string scriptDirectory = Path.GetDirectoryName(scriptPath);

        // Load banner from same folder as the script
        string bannerPath = Path.Combine(scriptDirectory, "ProjectSetupWizard.png").Replace("\\", "/");
        banner = AssetDatabase.LoadAssetAtPath<Texture2D>(bannerPath);
    }

    private void OnGUI()
    {
        GUILayout.Space(10);

        if (banner != null)
        {
            Rect bannerRect = GUILayoutUtility.GetRect(position.width, 100);
            GUI.DrawTexture(bannerRect, banner, ScaleMode.ScaleToFit);
        }
        else
        {
            EditorGUILayout.LabelField("Project Setup", EditorStyles.boldLabel);
        }

        GUILayout.Space(10);

        float old = EditorGUIUtility.labelWidth;
        EditorGUIUtility.labelWidth = 90f;

        projectName = EditorGUILayout.TextField("Name", projectName);
        namespaceName = EditorGUILayout.TextField("Namespace", namespaceName);

        EditorGUIUtility.labelWidth = old;

        GUILayout.Space(10);
        EditorGUILayout.LabelField("Description");
        var wrapStyle = new GUIStyle(EditorStyles.textArea) { wordWrap = true };
        description = EditorGUILayout.TextArea(description, wrapStyle, GUILayout.Height(100));

        GUILayout.FlexibleSpace();

        if (GUILayout.Button("Complete Setup", GUILayout.Height(24)))
        {
            PlayerSettings.productName = projectName;

            PerformSetup(projectName, namespaceName, description);
        }
    }

    private void PerformSetup(string projectName, string namespaceName, string description)
    {
        // Update player settings
        Close();

        CreateDefaultAbout(description);
        // Optionally delete Editor folder after setup
        string editorPath = Path.Combine(Application.dataPath, "Editor");
        if (Directory.Exists(editorPath))
        {
            // Directory.Delete(editorPath, true);
            // File.Delete(editorPath + ".meta");
        }

        AssetDatabase.Refresh();

        CreateDefaultScript(namespaceName);
        CreateDefaultAssembly(namespaceName);

        EditorUtility.DisplayDialog("Setup Complete", "Setup is complete, the project will now recompile.", "OK");
    }



    public static void CreateDefaultAbout(string description = null)
    {
        string companyName = Sanitize(PlayerSettings.companyName ?? "Default Company");
        string productName = Sanitize(PlayerSettings.productName ?? "ProductName");
        string productVersion = PlayerSettings.bundleVersion ?? "1.0.0";
        string DefaultFilename = "About.xml";

        string assetFolder = "About";
        string savePath = Path.Combine("Assets", assetFolder, DefaultFilename);

        // Ensure folder exists
        if (!AssetDatabase.IsValidFolder(Path.Combine("Assets", assetFolder)))
        {
            string guid = AssetDatabase.CreateFolder("Assets", assetFolder);
            AssetDatabase.GUIDToAssetPath(guid);
        }

        // Avoid overwriting existing files
        if (!File.Exists(savePath))
        {
            // Build final source text from template
            string src = GetXmlTemplate()
                .Replace("{productName}", productName)
                .Replace("{author}", companyName)
                .Replace("{description}", description)
                .Replace("{productVersion}", productVersion);

            // Write file
            File.WriteAllText(savePath, src);
            AssetDatabase.ImportAsset(savePath);
        }

        savePath = Path.Combine("Assets", assetFolder, "Preview.png");
        // Avoid overwriting existing files
        if (!File.Exists(savePath))
        {
            string thisFileAbs = GetThisFilePath();
            string thisDirAbs = Path.GetDirectoryName(thisFileAbs);
            string sourceAbs = Path.Combine(thisDirAbs, "Preview.png");
            File.Copy(sourceAbs, savePath, overwrite: true);
            AssetDatabase.ImportAsset(savePath);
        }


        savePath = Path.Combine("Assets", assetFolder, "Thumb.png");
        // Avoid overwriting existing files
        if (!File.Exists(savePath))
        {
            string thisFileAbs = GetThisFilePath();
            string thisDirAbs = Path.GetDirectoryName(thisFileAbs);
            string sourceAbs = Path.Combine(thisDirAbs, "Thumb.png");
            File.Copy(sourceAbs, savePath, overwrite: true);
            AssetDatabase.ImportAsset(savePath);
        }
    }

    // Returns the absolute path of THIS .cs file at compile time (Editor only)
    private static string GetThisFilePath([CallerFilePath] string path = "") => path;

    private static string GetXmlTemplate()
    {
        return
            "<?xml version=\"1.0\"?>\n" +
            "<ModMetadata xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\">\n" +
            "  <Name>{productName}</Name>\n" +
            "  <Author>{author}</Author>\n" +
            "  <Version>{productVersion}</Version>\n" +
            "  <Description>{description}</Description>\n" +
            "  <WorkshopHandle>0</WorkshopHandle>\n" +
            "  <Tags>\n" +
            "    <Tag>LaunchPad</Tag>\n" +
            "  </Tags>\n" +
            "</ModMetadata>\n";
    }




    public static void CreateDefaultScript(string namespaceName = null)
    {
        string productName = Sanitize(PlayerSettings.productName);
        string productVersion = PlayerSettings.bundleVersion ?? "1.0.0";
        string DefaultFilename = productName + ".cs";

        string assetFolder = "Scripts";
        string savePath = Path.Combine("Assets", assetFolder, DefaultFilename);

        // Avoid overwriting existing files
        if (File.Exists(savePath))
        {
            Debug.Log($"{savePath} exists, aborting.");
            return;
        }

        // Ensure folder exists
        if (!AssetDatabase.IsValidFolder(Path.Combine("Assets", assetFolder)))
        {
            string guid = AssetDatabase.CreateFolder("Assets", assetFolder);
            AssetDatabase.GUIDToAssetPath(guid);
        }

        // Build final source text from template
        string src = GetScriptTemplate()
            .Replace("{productName}", productName)
            .Replace("{namespaceName}", namespaceName)
            .Replace("{productVersion}", productVersion);

        // Write file
        File.WriteAllText(savePath, src);
        AssetDatabase.ImportAsset(savePath);

    }

    private static string GetScriptTemplate()
    {
        return
            "using UnityEngine;\n" +
            "using LaunchPadBooster;\n" +
            "using System.Collections.Generic;\n" +
            "\n" +
            "namespace {namespaceName}\n" +
            "{\n" +
            "    public class {productName} : MonoBehaviour\n" +
            "    {\n" +
            "        public static readonly Mod MOD = new(\"{productName}\", \"{productVersion}\");\n" +
            "\n" +
            "        public void OnLoaded(List<GameObject> prefabs)\n" +
            "        {\n" +
            "            MOD.AddPrefabs(prefabs);\n" +
            "\n#if DEVELOPMENT_BUILD" +
            "\n            Debug.Log($\"Loaded {prefabs.Count} prefabs\");" +
            "\n#endif" +
            "\n" +
            "\n" +       // Additional initialization goes here
            "        }\n" +
            "    }\n" +
            "}\n";
    }

    private void CreateDefaultAssembly(string nameSpace = null)
    {
        string productName = Sanitize(PlayerSettings.productName);
        string rootNamespace = (nameSpace == null) ? Sanitize(PlayerSettings.productName) : Sanitize(nameSpace);

        // don't overwrite existing files.
        string path = Path.Combine(Application.dataPath, $"{productName}.asmdef");
        if (File.Exists(path))
            return;

        CreateAsmdef(
            "Assets", // Default relative to Assets
            productName,
            rootNamespace,
            new List<string> { "Unity.TextMeshPro" },
            new List<string> { "Assembly-CSharp.dll", "Assembly-CSharp-firstpass.dll", "BepInEx.dll", "0Harmony.dll", "RW.RocketNet.dll", "LaunchPadBooster.dll" },
            new List<string> { }
        );
    }

    public static void CreateAsmdef(string folderPath, string assemblyName, string nameSpace, List<string> references, List<string> precompiled, List<string> constraints)
    {

        // Ensure folder exists
        if (!AssetDatabase.IsValidFolder(folderPath))
        {
            Debug.LogError($"Folder not found: {folderPath}");
            return;
        }

        // Define the assembly definition structure
        var asmDef = new AssemblyDefinitionData
        {
            name = assemblyName,
            rootNamespace = nameSpace,
            references = references?.ToArray() ?? new string[0],
            includePlatforms = new string[2] { "Editor", "WindowsStandalone64" },
            excludePlatforms = new string[0],
            allowUnsafeCode = false,
            autoReferenced = true,
            overrideReferences = true,
            precompiledReferences = precompiled?.ToArray() ?? new string[0],
            defineConstraints = constraints?.ToArray() ?? new string[0],
            versionDefines = new VersionDefine[0],
            noEngineReferences = false
        };

        // Convert to JSON
        string json = JsonUtility.ToJson(asmDef, true);

        // Write to file
        string filePath = Path.Combine(folderPath, $"{assemblyName}.asmdef");
        File.WriteAllText(filePath, json);


        // Import that specific asset; fall back to full refresh if needed
        AssetDatabase.ImportAsset(filePath, ImportAssetOptions.ForceUpdate);
        if (!AssetDatabase.GUIDFromAssetPath(filePath).Empty())
        {
            // good
        }
        else
        {
            AssetDatabase.Refresh(); // last resort
        }

        // Trigger script compilation so the new asmdef is picked up immediately
        CompilationPipeline.RequestScriptCompilation();
    }

    // Internal representation of Unity's asmdef JSON structure
    [System.Serializable]
    private class AssemblyDefinitionData
    {
        public string name;
        public string rootNamespace;
        public string[] references;
        public string[] includePlatforms;
        public string[] excludePlatforms;
        public bool allowUnsafeCode;
        public bool autoReferenced;
        public bool overrideReferences;
        public string[] precompiledReferences;
        public string[] defineConstraints;
        public VersionDefine[] versionDefines;
        public bool noEngineReferences;
    }

    [System.Serializable]
    private class VersionDefine
    {
        public string name;
        public string expression;
        public string define;
    }

    public static string Sanitize(string part)
    {
        // Replace invalid chars with underscores
        string clean = Regex.Replace(part, @"[^A-Za-z0-9_]", "_");
        // Remove leading digits and underscores
        clean = Regex.Replace(clean, @"^[^A-Za-z_]+", "");
        // PascalCase words separated by underscores
        clean = string.Join("", clean
            .Split(new[] { '_', ' ' }, System.StringSplitOptions.RemoveEmptyEntries)
            .Select(w => char.ToUpper(w[0]) + w.Substring(1).ToLower()));
        // Fallback if it became empty
        return string.IsNullOrEmpty(clean) ? "Unnamed" : clean;
    }
}
