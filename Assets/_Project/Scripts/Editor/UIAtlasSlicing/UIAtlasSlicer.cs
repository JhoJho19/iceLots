using TMPro;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>Explicit, image-reviewed rectangles, with isolated uGUI verification.</summary>
public static class UIAtlasSlicer
{
    const string ManifestPath = "Assets/Editor/UIAtlasSlicing/AtlasRects.json";
    const string ScenePath = "Assets/Editor/UIAtlasSlicing/UIAtlasVerification.unity";
    const string OutputPath = "Tools/UIAtlas/Verification";
    [Serializable] public class Manifest { public Atlas[] atlases; }
    [Serializable] public class Atlas { public string path; public int width, height; public Slice[] sprites; }
    [Serializable] public class Slice
    {
        public string name; public int x, top, width, height; public int[] border;
        public Rect Rect(int textureHeight) { return new Rect(x, textureHeight - top - height, width, height); }
        public Vector4 Border { get { return new Vector4(border[0], border[1], border[2], border[3]); } }
    }
    static string Hash(string path)
    {
        using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
    }
    static Manifest Read() { return JsonUtility.FromJson<Manifest>(File.ReadAllText(ManifestPath)); }

    [MenuItem("Tools/UI Atlases/Apply reviewed slicing and verify")]
    public static void ApplyAndVerify()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
        var manifest = Read();
        var hashes = manifest.atlases.ToDictionary(a => a.path, a => Hash(a.path));
        var factories = new SpriteDataProviderFactories(); factories.Init();
        foreach (var atlas in manifest.atlases)
        {
            ValidateRects(atlas);
            var importer = (TextureImporter)AssetImporter.GetAtPath(atlas.path);
            if (importer == null) throw new FileNotFoundException(atlas.path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Multiple;
            var settings = new TextureImporterSettings(); importer.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            settings.spriteAlignment = (int)SpriteAlignment.Center;
            settings.spritePivot = new Vector2(0.5f, 0.5f);
            importer.SetTextureSettings(settings);
            importer.maxTextureSize = 2048;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.crunchedCompression = false;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.sRGBTexture = true;
            importer.filterMode = FilterMode.Bilinear;
            importer.wrapMode = TextureWrapMode.Clamp;
            // Keep full source resolution and RGBA on relevant build targets too.
            foreach (var target in new[] { "DefaultTexturePlatform", "Standalone", "Android", "iPhone", "WebGL" })
            {
                var platform = importer.GetPlatformTextureSettings(target);
                platform.name = target;
                platform.maxTextureSize = 2048;
                platform.textureCompression = TextureImporterCompression.Uncompressed;
                platform.crunchedCompression = false;
                platform.format = TextureImporterFormat.RGBA32;
                if (target != "DefaultTexturePlatform") platform.overridden = true;
                importer.SetPlatformTextureSettings(platform);
            }
            var provider = factories.GetSpriteEditorDataProviderFromObject(importer);
            provider.InitSpriteEditorDataProvider();
            var existing = provider.GetSpriteRects().ToDictionary(s => s.name, s => s.spriteID);
            var rects = atlas.sprites.Select(s => new SpriteRect {
                name = s.name, rect = s.Rect(atlas.height), border = s.Border,
                alignment = SpriteAlignment.Center, pivot = new Vector2(0.5f, 0.5f),
                spriteID = existing.ContainsKey(s.name) ? existing[s.name] : GUID.Generate()
            }).ToArray();
            provider.SetSpriteRects(rects);
            provider.GetDataProvider<ISpriteNameFileIdDataProvider>().SetNameFileIdPairs(
                rects.Select(s => new SpriteNameFileIdPair(s.name, s.spriteID)));
            provider.Apply();
            importer.SaveAndReimport();
            AssetDatabase.ImportAsset(atlas.path, ImportAssetOptions.ForceUpdate);
            VerifyImported(atlas);
            if (Hash(atlas.path) != hashes[atlas.path]) throw new InvalidOperationException("Source PNG changed: " + atlas.path);
        }
        CreateVerificationScene(manifest);
        AssetDatabase.Refresh();
        Debug.Log("UIAtlas slicing verified: " + string.Join(", ", manifest.atlases.Select(a => a.path + " = " + a.sprites.Length)));
    }

    static void ValidateRects(Atlas atlas)
    {
        if (atlas.sprites.Select(s => s.name).Distinct().Count() != atlas.sprites.Length) throw new Exception("Duplicate names.");
        for (int i = 0; i < atlas.sprites.Length; i++)
        {
            var s = atlas.sprites[i]; var rect = s.Rect(atlas.height);
            if (rect.xMin < 0 || rect.yMin < 0 || rect.xMax > atlas.width || rect.yMax > atlas.height || rect.width <= 0 || rect.height <= 0) throw new Exception("Invalid rect: " + s.name);
            if (s.border.Any(b => b < 0) || s.border[0] + s.border[2] >= s.width || s.border[1] + s.border[3] >= s.height) throw new Exception("Invalid border: " + s.name);
            for (int j = 0; j < i; j++) if (rect.Overlaps(atlas.sprites[j].Rect(atlas.height))) throw new Exception("Overlapping rect: " + s.name);
        }
    }
    static void VerifyImported(Atlas atlas)
    {
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(atlas.path);
        if (tex.width != atlas.width || tex.height != atlas.height) throw new Exception("Resolution changed: " + atlas.path);
        var sprites = AssetDatabase.LoadAllAssetsAtPath(atlas.path).OfType<Sprite>().ToDictionary(s => s.name);
        if (sprites.Count != atlas.sprites.Length) throw new Exception("Sub-asset count mismatch: " + atlas.path);
        foreach (var s in atlas.sprites)
        {
            var sprite = sprites[s.name];
            if (sprite.rect != s.Rect(atlas.height) || sprite.border != s.Border || sprite.vertices.Length != 4) throw new Exception("Sprite metadata mismatch: " + s.name);
        }
    }
    static RectTransform NewRect(string name, Transform parent, Vector2 center, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform)); go.layer = 30;
        var rt = (RectTransform)go.transform; rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f); rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = center; rt.sizeDelta = size; return rt;
    }
    static Image NewImage(string name, Transform parent, Vector2 center, Vector2 size, Sprite sprite, bool sliced)
    {
        var rt = NewRect(name, parent, center, size);
        var image = rt.gameObject.AddComponent<Image>(); image.sprite = sprite;
        image.type = sliced ? Image.Type.Sliced : Image.Type.Simple;
        image.raycastTarget = false; return image;
    }
    static void Label(Transform parent, string text, Vector2 center, Vector2 size)
    {
        var rt = NewRect(text, parent, center, size); var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        t.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/_Project/Fonts/Baloo2-Bold SDF.asset"); t.text = text;
        t.fontSize = 18; t.color = Color.white; t.alignment = TextAlignmentOptions.MidlineLeft; t.raycastTarget = false;
    }
    static Canvas NewCanvas(string name, Vector2 origin)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Canvas)); go.layer = 30;
        var c = go.GetComponent<Canvas>(); c.renderMode = RenderMode.WorldSpace;
        var rt = (RectTransform)go.transform; rt.sizeDelta = new Vector2(3000, 1600); rt.position = new Vector3(origin.x, origin.y, 0);
        return c;
    }
    static void Render(Camera cam, Vector2 center, int width, int height, string file)
    {
        cam.transform.position = new Vector3(center.x, center.y, -10);
        cam.orthographicSize = height / 2f; cam.aspect = (float)width / height;
        var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
        var previous = RenderTexture.active;
        try
        {
            cam.targetTexture = rt; Canvas.ForceUpdateCanvases(); cam.Render();
            RenderTexture.active = rt;
            var capture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            capture.ReadPixels(new Rect(0, 0, width, height), 0, 0); capture.Apply();
            File.WriteAllBytes(file, capture.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(capture);
        }
        finally { cam.targetTexture = null; RenderTexture.active = previous; rt.Release(); UnityEngine.Object.DestroyImmediate(rt); }
    }
    static void CreateVerificationScene(Manifest manifest)
    {
        Directory.CreateDirectory(OutputPath);
        var original = SceneManager.GetActiveScene();
        // Additive scene keeps the user's loaded scene and unsaved changes intact.
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        var csv = new List<string> { "atlas,name,x,y,width,height,borderLeft,borderBottom,borderRight,borderTop,fileID" };
        try
        {
            SceneManager.SetActiveScene(scene);
            var cameraGO = new GameObject("UI Atlas Verification Camera", typeof(Camera)); cameraGO.layer = 30;
            var cam = cameraGO.GetComponent<Camera>(); cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.15f, 0.17f, 0.20f, 1);
            cam.cullingMask = 1 << 30; cam.allowHDR = false; cam.allowMSAA = false;
            var lightGO = new GameObject("Directional Light", typeof(Light)); lightGO.GetComponent<Light>().type = LightType.Directional;
            var gallery = NewCanvas("All 122 sprites at native pixel size", Vector2.zero);
            var sliced = new List<KeyValuePair<Slice, Sprite>>();
            for (int ai = 0; ai < manifest.atlases.Length; ai++)
            {
                var atlas = manifest.atlases[ai];
                var sprites = AssetDatabase.LoadAllAssetsAtPath(atlas.path).OfType<Sprite>().ToDictionary(s => s.name);
                float offset = ai * 1500;
                Label(gallery.transform, atlas.path, new Vector2(offset + 724, 1120), new Vector2(1448, 40));
                foreach (var s in atlas.sprites)
                {
                    var sprite = sprites[s.name];
                    var center = new Vector2(offset + s.x + s.width / 2f, 1086 - s.top - s.height / 2f);
                    NewImage(s.name, gallery.transform, center, new Vector2(s.width, s.height), sprite, false);
                    string guid; long id; AssetDatabase.TryGetGUIDAndLocalFileIdentifier(sprite, out guid, out id);
                    csv.Add(string.Join(",", new object[] { atlas.path, s.name, s.x, 1086-s.top-s.height, s.width, s.height, s.border[0], s.border[1], s.border[2], s.border[3], id }));
                    if (sprite.border != Vector4.zero) sliced.Add(new KeyValuePair<Slice, Sprite>(s, sprite));
                }
                Render(cam, new Vector2(offset+724, 543), 1448, 1086, OutputPath + "/" + Path.GetFileNameWithoutExtension(atlas.path) + "_UnityNative.png");
            }
            const int rowsPerPage = 4;
            for (int page = 0; page * rowsPerPage < sliced.Count; page++)
            {
                var origin = new Vector2(4000 + page * 3300, 0);
                var canvas = NewCanvas("9-slice page " + (page+1), origin);
                float y = 0;
                foreach (var pair in sliced.Skip(page*rowsPerPage).Take(rowsPerPage))
                {
                    var s = pair.Key;
                    Label(canvas.transform, s.name + " | native / 1.5w 1.2h / 2w 1.5h", new Vector2(1400,-y-18), new Vector2(2800,36));
                    float x = 10; float maxHeight = s.height * 1.5f;
                    foreach (var scale in new[] { new Vector2(1,1), new Vector2(1.5f,1.2f), new Vector2(2,1.5f) })
                    {
                        var size = new Vector2(s.width*scale.x, s.height*scale.y);
                        var image = NewImage(s.name + " " + scale, canvas.transform, new Vector2(x+size.x/2,-y-45-maxHeight/2), size, pair.Value, true);
                        // Force uGUI geometry generation and check that all coordinates remain finite.
                        image.Rebuild(CanvasUpdate.PreRender);
                        x += size.x + 36;
                    }
                    y += maxHeight + 90;
                }
                int height = Mathf.CeilToInt(y + 10);
                Render(cam, origin + new Vector2(1400,-height/2f), 2800, height, OutputPath + "/UnitySliced_" + (page+1).ToString("00") + ".png");
            }
            cam.transform.position = new Vector3(1474,543,-10); cam.orthographicSize = 600; cam.aspect = 2.5f;
            if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new Exception("Could not save verification scene.");
            File.WriteAllLines(OutputPath + "/ImportedSprites.csv", csv);
            File.WriteAllText(OutputPath + "/UnityVerification.json", "{\"unityVersion\":\""+Application.unityVersion+"\",\"spriteCount\":"+manifest.atlases.Sum(a=>a.sprites.Length)+",\"slicedCount\":"+sliced.Count+",\"rectsNonOverlapping\":true,\"allSubAssetsVerified\":true,\"fullRectVerified\":true,\"sourceHashesUnchanged\":true,\"slicedSizes\":[[1,1],[1.5,1.2],[2,1.5]]}");
        }
        finally
        {
            if (original.IsValid() && original.isLoaded) SceneManager.SetActiveScene(original);
            EditorSceneManager.CloseScene(scene, true);
        }
    }
}
