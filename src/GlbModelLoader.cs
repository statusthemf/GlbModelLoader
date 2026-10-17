using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInEx.Logging;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace ULTRADROID.Models
{
    public sealed class ModelLoadOptions
    {
        public string Name = "ULTRADROID_Model";
        public Vector3 Position = Vector3.zero;
        public Vector3 RotationEuler = Vector3.zero;
        public Vector3 Scale = Vector3.one;
        public bool LoadTextures = true;
        public bool CreateColliders = false;
    }

    public static class ModelAPI
    {
        public const string Version = "0.1.13-emission-diag";

        // UV TEST SWITCH: true = flip V (1 - v), false = use V as-is.
        private const bool FlipUvV = true;

        private static readonly ManualLogSource DiagnosticLog =
            BepInEx.Logging.Logger.CreateLogSource(
                "ULTRADROID Models API Diagnostics");

        private static void Log(string message)
        {
            DiagnosticLog.LogInfo("[ULTRADROID Models] " + message);
        }

        private static void LogError(string message, Exception ex)
        {
            DiagnosticLog.LogError(
                "[ULTRADROID Models] " + message + "\n" + ex);
        }

        public static GameObject LoadGLB(
            string path,
            Transform parent = null,
            ModelLoadOptions options = null)
        {
            Log("LoadGLB ENTER");

            if (string.IsNullOrEmpty(path))
                throw new ArgumentException(
                    "A GLB path is required.", "path");

            if (!File.Exists(path))
                throw new FileNotFoundException(
                    "GLB file was not found.", path);

            options = options ?? new ModelLoadOptions();

            Log("Reading GLB: " + path);

            byte[] fileBytes = File.ReadAllBytes(path);

            Log("File read complete. Bytes=" + fileBytes.Length);

            GlbDocument doc;

            try
            {
                doc = GlbDocument.Parse(fileBytes);
            }
            catch (Exception ex)
            {
                LogError("GLB parsing failed", ex);
                throw;
            }

            if (doc == null || doc.json == null)
                throw new InvalidDataException(
                    "GLB parser returned an empty document.");

            Log("Document counts: nodes=" + Count(doc.json.nodes) +
                ", meshes=" + Count(doc.json.meshes) +
                ", accessors=" + Count(doc.json.accessors) +
                ", bufferViews=" + Count(doc.json.bufferViews) +
                ", materials=" + Count(doc.json.materials) +
                ", textures=" + Count(doc.json.textures) +
                ", images=" + Count(doc.json.images) +
                ", binaryBytes=" + doc.binary.Length);

            GameObject root = new GameObject(
                string.IsNullOrEmpty(options.Name)
                    ? Path.GetFileNameWithoutExtension(path)
                    : options.Name);

            try
            {
                if (parent != null)
                    root.transform.SetParent(parent, false);

                root.transform.localPosition = options.Position;
                root.transform.localRotation =
                    Quaternion.Euler(options.RotationEuler);
                root.transform.localScale = options.Scale;

                Texture2D[] textures =
                    BuildTextures(doc, options.LoadTextures);

                Material[] materials =
                    BuildMaterials(doc, textures);

                BuildScene(
                    doc,
                    root.transform,
                    materials,
                    options.CreateColliders);

                Log("LoadGLB SUCCESS");

                return root;
            }
            catch (Exception ex)
            {
                LogError(
                    "LoadGLB failed while building the model", ex);

                UnityEngine.Object.Destroy(root);
                throw;
            }
        }

        private static int Count<T>(T[] array)
        {
            return array == null ? 0 : array.Length;
        }

        private static Texture2D[] BuildTextures(
            GlbDocument doc,
            bool load)
        {
            int count = Count(doc.json.images);
            Texture2D[] result = new Texture2D[count];

            Log("[TEXTURE DIAG] BuildTextures started.");
            Log("[TEXTURE DIAG] Images=" + count +
                ", enabled=" + load);

            if (!load || count == 0)
            {
                Log("[TEXTURE DIAG] Skipped. Enabled=" + load +
                    ", imageCount=" + count);
                return result;
            }

            for (int i = 0; i < count; i++)
            {
                GltfImage image = doc.json.images[i];

                if (image == null)
                {
                    Log("[TEXTURE DIAG] Image " + i + " is null.");
                    continue;
                }

                Log("[TEXTURE DIAG] Image " + i +
                    ": bufferView=" + image.bufferView +
                    ", mimeType=" + (image.mimeType ?? "<null>"));

                if (image.bufferView < 0 ||
                    doc.json.bufferViews == null ||
                    image.bufferView >= doc.json.bufferViews.Length)
                {
                    Log("[TEXTURE DIAG] Image " + i +
                        " skipped: invalid bufferView. " +
                        "Embedded GLB images only.");
                    continue;
                }

                byte[] bytes;

                try
                {
                    bytes = doc.ReadBufferView(image.bufferView);
                }
                catch (Exception ex)
                {
                    LogError(
                        "[TEXTURE DIAG] Image " + i +
                        ": failed to extract bytes.", ex);
                    continue;
                }

                if (bytes == null || bytes.Length == 0)
                {
                    Log("[TEXTURE DIAG] Image " + i +
                        ": extracted image data is empty.");
                    continue;
                }

                Log("[TEXTURE DIAG] Image " + i +
                    ": extracted " + bytes.Length + " bytes.");

                Texture2D texture = null;

                try
                {
                    texture = new Texture2D(
                        2, 2, TextureFormat.RGBA32, true);

                    bool loaded =
                        ImageConversion.LoadImage(
                            texture, bytes, false);

                    Log("[TEXTURE DIAG] Image " + i +
                        ": LoadImage returned " + loaded);

                    if (!loaded)
                    {
                        UnityEngine.Object.Destroy(texture);
                        continue;
                    }

                    texture.name = "ULTRADROID_GLTF_Image_" + i;
                    texture.wrapMode = TextureWrapMode.Repeat;
                    texture.filterMode = FilterMode.Bilinear;

                    result[i] = texture;

                    Log("[TEXTURE DIAG] Image " + i +
                        ": loaded successfully; dimensions=" +
                        texture.width + "x" + texture.height);
                }
                catch (Exception ex)
                {
                    LogError(
                        "[TEXTURE DIAG] Image " + i +
                        ": texture creation or decoding failed.", ex);

                    if (texture != null)
                        UnityEngine.Object.Destroy(texture);
                }
            }

            Log("[TEXTURE DIAG] BuildTextures finished: " +
                CountLoadedTextures(result) + "/" + count +
                " images loaded.");

            return result;
        }

        private static Material[] BuildMaterials(
            GlbDocument doc,
            Texture2D[] textures)
        {
            int count = Count(doc.json.materials);
            Material[] result = new Material[count + 1];

            // Converted metallic-gloss textures, shared between materials
            // that use the same image and the same factors.
            Dictionary<string, Texture2D> convertedCache =
                new Dictionary<string, Texture2D>();

            Log("[MATERIAL DIAG] Starting. Materials=" + count +
                ", loadedTextures=" + CountLoadedTextures(textures));

            Shader shader = Shader.Find("Standard");

            if (shader == null)
                shader = Shader.Find("Universal Render Pipeline/Lit");

            if (shader == null)
                shader = Shader.Find("Sprites/Default");

            if (shader == null)
                throw new InvalidOperationException(
                    "No compatible Unity shader was found.");

            Log("[MATERIAL DIAG] Selected shader: " + shader.name);

            for (int i = 0; i < count; i++)
            {
                GltfMaterial source = doc.json.materials[i];

                if (source == null)
                {
                    Log("[MATERIAL DIAG] Material " + i + " is null.");
                    continue;
                }

                Material material = new Material(shader);

                material.name = string.IsNullOrEmpty(source.name)
                    ? "GLTF_Material_" + i
                    : source.name;

                PbrMetallicRoughness pbr =
                    source.pbrMetallicRoughness;

                if (pbr != null)
                {
                    float metallicFactor =
                        Mathf.Clamp01(pbr.metallicFactor);

                    float roughnessFactor =
                        Mathf.Clamp01(pbr.roughnessFactor);

                    if (pbr.baseColorFactor != null &&
                        pbr.baseColorFactor.Length >= 4)
                    {
                        material.color = new Color(
                            pbr.baseColorFactor[0],
                            pbr.baseColorFactor[1],
                            pbr.baseColorFactor[2],
                            pbr.baseColorFactor[3]);
                    }

                    if (pbr.baseColorTexture != null)
                    {
                        int textureIndex =
                            pbr.baseColorTexture.index;

                        int imageIndex =
                            GetImageIndex(doc, textureIndex);

                        Log("[MATERIAL DIAG] Material " + i +
                            ": textureIndex=" + textureIndex +
                            ", imageIndex=" + imageIndex);

                        if (imageIndex >= 0 &&
                            imageIndex < textures.Length &&
                            textures[imageIndex] != null)
                        {
                            Texture2D texture = textures[imageIndex];
                            bool assigned = false;

                            if (material.HasProperty("_MainTex"))
                            {
                                material.SetTexture(
                                    "_MainTex", texture);
                                assigned = true;
                            }

                            if (material.HasProperty("_BaseMap"))
                            {
                                material.SetTexture(
                                    "_BaseMap", texture);
                                assigned = true;
                            }

                            Log("[MATERIAL DIAG] Material " + i +
                                ": texture=" + texture.name +
                                ", shader=" + material.shader.name +
                                ", assigned=" + assigned);
                        }
                        else
                        {
                            Log("[MATERIAL DIAG] Material " + i +
                                ": image index invalid or texture missing.");
                        }
                    }
                    else
                    {
                        Log("[MATERIAL DIAG] Material " + i +
                            ": no baseColorTexture defined.");
                    }

                    // Scalar fallback (used as-is when there is no
                    // metallic-roughness texture). If a texture is
                    // converted below, these are reset so the factors
                    // are not applied twice.
                    float scalarSmoothness = 1f - roughnessFactor;

                    if (material.HasProperty("_Metallic"))
                    {
                        material.SetFloat(
                            "_Metallic", metallicFactor);
                    }

                    if (material.HasProperty("_Glossiness"))
                    {
                        material.SetFloat(
                            "_Glossiness", scalarSmoothness);
                    }

                    if (material.HasProperty("_Smoothness"))
                    {
                        material.SetFloat(
                            "_Smoothness", scalarSmoothness);
                    }

                    bool metallicTextureAssigned = false;

                    if (pbr.metallicRoughnessTexture != null)
                    {
                        int mrTextureIndex =
                            pbr.metallicRoughnessTexture.index;

                        int mrImageIndex =
                            GetImageIndex(doc, mrTextureIndex);

                        bool found =
                            mrImageIndex >= 0 &&
                            mrImageIndex < textures.Length &&
                            textures[mrImageIndex] != null;

                        Log("[METALLIC-ROUGHNESS] Material " + i +
                            ": textureIndex=" + mrTextureIndex +
                            ", imageIndex=" + mrImageIndex +
                            ", found=" + found);

                        // Only the Standard shader workflow is
                        // implemented. URP Lit also has _MetallicGlossMap
                        // but a different smoothness setup that has not
                        // been verified here, so it is not touched.
                        bool standardWorkflow =
                            material.HasProperty("_MetallicGlossMap") &&
                            material.HasProperty("_GlossMapScale");

                        if (!found)
                        {
                            Log("[METALLIC-ROUGHNESS] Material " + i +
                                ": texture not available; using " +
                                "scalar metallic/smoothness.");
                        }
                        else if (!standardWorkflow)
                        {
                            Log("[METALLIC-ROUGHNESS] Material " + i +
                                ": shader " + material.shader.name +
                                " does not use the Standard " +
                                "_MetallicGlossMap/_GlossMapScale " +
                                "layout; texture NOT assigned. " +
                                "Using scalar metallic/smoothness.");
                        }
                        else
                        {
                            string cacheKey =
                                mrImageIndex + "|" +
                                metallicFactor.ToString("R") + "|" +
                                roughnessFactor.ToString("R");

                            Texture2D converted;

                            if (!convertedCache.TryGetValue(
                                cacheKey, out converted))
                            {
                                converted =
                                    BuildUnityMetallicGlossTexture(
                                        textures[mrImageIndex],
                                        metallicFactor,
                                        roughnessFactor);

                                convertedCache[cacheKey] = converted;
                            }

                            bool convertedOk = converted != null;

                            Log("[METALLIC-ROUGHNESS] Material " + i +
                                ": converted=" + convertedOk);

                            if (convertedOk)
                            {
                                try
                                {
                                    material.SetTexture(
                                        "_MetallicGlossMap", converted);

                                    // Factors are already baked into the
                                    // converted texture, so the shader
                                    // multipliers must be 1.
                                    material.SetFloat("_Metallic", 1f);
                                    material.SetFloat(
                                        "_GlossMapScale", 1f);

                                    // Smoothness comes from the alpha
                                    // channel of the metallic map.
                                    if (material.HasProperty(
                                        "_SmoothnessTextureChannel"))
                                    {
                                        material.SetFloat(
                                            "_SmoothnessTextureChannel",
                                            0f);
                                    }

                                    material.EnableKeyword(
                                        "_METALLICGLOSSMAP");

                                    metallicTextureAssigned = true;

                                    Log("[METALLIC-ROUGHNESS] Material " +
                                        i + ": assigned=true, texture=" +
                                        converted.name);
                                }
                                catch (Exception ex)
                                {
                                    LogError(
                                        "[METALLIC-ROUGHNESS] Material " +
                                        i + ": assignment failed.", ex);
                                }
                            }
                            else
                            {
                                Log("[METALLIC-ROUGHNESS] Material " + i +
                                    ": assigned=false; using scalar " +
                                    "metallic/smoothness.");
                            }
                        }
                    }
                    else
                    {
                        Log("[METALLIC-ROUGHNESS] Material " + i +
                            ": no metallicRoughnessTexture defined; " +
                            "metallic=" + metallicFactor +
                            ", smoothness=" + scalarSmoothness);
                    }

                    Log("[MATERIAL DIAG] Material " + i +
                        ": metallicFactor=" + metallicFactor +
                        ", roughnessFactor=" + roughnessFactor +
                        ", metallicTextureAssigned=" +
                        metallicTextureAssigned);
                }
                else
                {
                    Log("[MATERIAL DIAG] Material " + i +
                        ": PBR data is missing.");
                }

                // DIAGNOSTIC (0.1.13): force bright magenta emission to
                // verify the shader is actually reading material
                // properties. Restore the original block after testing.
                //
                // Original behaviour (commented out):
                //   if (material.HasProperty("_EmissionColor"))
                //   {
                //       material.SetColor("_EmissionColor", Color.black);
                //       material.DisableKeyword("_EMISSION");
                //       Log("[EMISSION TEST] Material " + i +
                //           ": emission disabled; source emissiveFactor=" +
                //           (source.emissiveFactor == null
                //               ? "<null>"
                //               : string.Join(",", source.emissiveFactor)));
                //   }
                if (material.HasProperty("_EmissionColor"))
                {
                    material.SetColor(
                        "_EmissionColor", Color.magenta);

                    material.EnableKeyword("_EMISSION");

                    Log("[EMISSION TEST] Material " + i +
                        ": emission forced to magenta; " +
                        "source emissiveFactor=" +
                        (source.emissiveFactor == null
                            ? "<null>"
                            : string.Join(",", source.emissiveFactor)));
                }

                result[i] = material;

                Log("[MATERIAL DIAG] Material " + i +
                    " ready: " + material.name);
            }

            result[count] = new Material(shader)
            {
                name = "GLTF_Default_Material"
            };

            Log("[MATERIAL DIAG] Created fallback material.");

            return result;
        }

        // Converts a glTF metallic-roughness texture
        //   G = roughness, B = metallic
        // into Unity Standard _MetallicGlossMap layout
        //   R = metallic, A = smoothness (1 - roughness)
        // metallicFactor and roughnessFactor are baked in here.
        // Returns null (and logs) if the source cannot be read.
        //
        // DIAGNOSTIC BUILD (0.1.12): every converted pixel is forced to
        // (0, 0, 0, 0) — fully non-metallic, fully rough — so we can
        // observe whether Unity's Standard shader reacts to
        // _MetallicGlossMap at all. Base color is untouched.
        private static Texture2D BuildUnityMetallicGlossTexture(
            Texture2D source,
            float metallicFactor,
            float roughnessFactor)
        {
            if (source == null)
            {
                Log("[METALLIC-ROUGHNESS] Convert: source is null.");
                return null;
            }

            try
            {
                int width = source.width;
                int height = source.height;

                if (width <= 0 || height <= 0)
                {
                    Log("[METALLIC-ROUGHNESS] Convert: invalid size " +
                        width + "x" + height);
                    return null;
                }

                Color32[] sourcePixels = source.GetPixels32();

                if (sourcePixels == null || sourcePixels.Length == 0)
                {
                    Log("[METALLIC-ROUGHNESS] Convert: no pixels read " +
                        "(texture may not be readable).");
                    return null;
                }

                Color32[] convertedPixels =
                    new Color32[sourcePixels.Length];

                for (int i = 0; i < sourcePixels.Length; i++)
                {
                    Color32 pixel = sourcePixels[i];

                    float metallic =
                        (pixel.b / 255f) * metallicFactor;

                    // Keep the source roughness calculation.
                    float roughness =
                        (pixel.g / 255f) * roughnessFactor;

                    // Temporary test: reduce metalness slightly and make
                    // metallic regions smoother to help reveal reflections.
                    metallic *= 0.75f;

                    float smoothness = 1f - roughness;

                    if (metallic > 0.25f)
                    {
                        smoothness = Mathf.Max(smoothness, 0.70f);
                    }

                    byte metallicByte = (byte)Mathf.Clamp(
                        Mathf.RoundToInt(metallic * 255f), 0, 255);

                    byte smoothnessByte = (byte)Mathf.Clamp(
                        Mathf.RoundToInt(smoothness * 255f), 0, 255);

                    // DIAGNOSTIC: force fully non-metallic, fully rough.
                    // Restore the original write after testing:
                    //   convertedPixels[i] = new Color32(
                    //       metallicByte, 0, 0, smoothnessByte);
                    convertedPixels[i] = new Color32(0, 0, 0, 0);
                }

                // linear = true: this is data, not color.
                Texture2D converted = new Texture2D(
                    width,
                    height,
                    TextureFormat.RGBA32,
                    true,
                    true);

                converted.name = source.name + "_UnityMetallicGloss";
                converted.wrapMode = source.wrapMode;
                converted.filterMode = source.filterMode;

                converted.SetPixels32(convertedPixels);
                converted.Apply(true, false);

                // DIAGNOSTIC: sample the converted texture's first pixels.
                Color32[] pixels = converted.GetPixels32();

                int count = Math.Min(pixels.Length, 1000);
                long metallicTotal = 0;
                long smoothnessTotal = 0;

                for (int i = 0; i < count; i++)
                {
                    metallicTotal += pixels[i].r;
                    smoothnessTotal += pixels[i].a;
                }

                if (count > 0)
                {
                    Log("[ULTRADROID METALLIC DIAG] " +
                        "Samples=" + count +
                        ", average metallic=" +
                        (metallicTotal / (float)count / 255f)
                            .ToString("F3") +
                        ", average smoothness=" +
                        (smoothnessTotal / (float)count / 255f)
                            .ToString("F3"));
                }

                Log("[METALLIC-ROUGHNESS] Convert OK: " +
                    converted.name + " " + width + "x" + height);

                return converted;
            }
            catch (Exception ex)
            {
                LogError(
                    "[METALLIC-ROUGHNESS] Texture conversion failed " +
                    "for " + source.name, ex);

                return null;
            }
        }

        private static int CountLoadedTextures(Texture2D[] textures)
        {
            int count = 0;

            if (textures == null)
                return 0;

            for (int i = 0; i < textures.Length; i++)
            {
                if (textures[i] != null)
                    count++;
            }

            return count;
        }

        private static int GetImageIndex(
            GlbDocument doc,
            int textureIndex)
        {
            if (doc.json.textures == null ||
                textureIndex < 0 ||
                textureIndex >= doc.json.textures.Length)
            {
                Log("[MATERIAL DIAG] Invalid texture index: " +
                    textureIndex);
                return -1;
            }

            GltfTexture texture =
                doc.json.textures[textureIndex];

            if (texture == null)
            {
                Log("[MATERIAL DIAG] Texture entry " +
                    textureIndex + " is null.");
                return -1;
            }

            Log("[MATERIAL DIAG] Texture " + textureIndex +
                " references image " + texture.source);

            return texture.source;
        }

        private static void BuildScene(
            GlbDocument doc,
            Transform root,
            Material[] materials,
            bool colliders)
        {
            if (doc.json.nodes == null ||
                doc.json.nodes.Length == 0)
            {
                Log("No nodes found in glTF scene.");
                return;
            }

            int sceneIndex = doc.json.scene;
            int[] roots = null;

            if (doc.json.scenes != null &&
                doc.json.scenes.Length > 0)
            {
                if (sceneIndex < 0 ||
                    sceneIndex >= doc.json.scenes.Length)
                {
                    sceneIndex = 0;
                }

                GltfScene scene =
                    doc.json.scenes[sceneIndex];

                if (scene != null)
                    roots = scene.nodes;
            }

            if (roots == null || roots.Length == 0)
            {
                HashSet<int> childIndices =
                    new HashSet<int>();

                foreach (GltfNode node in doc.json.nodes)
                {
                    if (node == null ||
                        node.children == null)
                        continue;

                    foreach (int child in node.children)
                        childIndices.Add(child);
                }

                List<int> inferredRoots =
                    new List<int>();

                for (int i = 0;
                    i < doc.json.nodes.Length;
                    i++)
                {
                    if (!childIndices.Contains(i))
                        inferredRoots.Add(i);
                }

                roots = inferredRoots.ToArray();
            }

            Log("[SCENE DIAG] Root node count=" + roots.Length);

            HashSet<int> recursionGuard =
                new HashSet<int>();

            foreach (int nodeIndex in roots)
            {
                BuildNode(
                    doc,
                    nodeIndex,
                    root,
                    materials,
                    colliders,
                    recursionGuard);
            }
        }

        private static void BuildNode(
            GlbDocument doc,
            int index,
            Transform parent,
            Material[] materials,
            bool colliders,
            HashSet<int> guard)
        {
            if (index < 0 ||
                index >= doc.json.nodes.Length ||
                guard.Contains(index))
                return;

            GltfNode node = doc.json.nodes[index];

            if (node == null)
                return;

            guard.Add(index);

            GameObject gameObject = new GameObject(
                string.IsNullOrEmpty(node.name)
                    ? "GLTF_Node_" + index
                    : node.name);

            gameObject.transform.SetParent(parent, false);

            ApplyNodeTransform(gameObject.transform, node);

            if (node.mesh >= 0 &&
                doc.json.meshes != null &&
                node.mesh < doc.json.meshes.Length)
            {
                GltfMesh mesh = doc.json.meshes[node.mesh];

                if (mesh != null)
                {
                    Log("[SCENE DIAG] Node " + index +
                        " uses mesh " + node.mesh);

                    BuildMesh(
                        doc,
                        mesh,
                        gameObject,
                        materials,
                        colliders);
                }
            }

            if (node.children != null)
            {
                foreach (int child in node.children)
                {
                    BuildNode(
                        doc,
                        child,
                        gameObject.transform,
                        materials,
                        colliders,
                        guard);
                }
            }

            guard.Remove(index);
        }

        private static void ApplyNodeTransform(
            Transform transform,
            GltfNode node)
        {
            if (node.matrix != null &&
                node.matrix.Length == 16)
            {
                Matrix4x4 matrix = new Matrix4x4();

                for (int row = 0; row < 4; row++)
                {
                    for (int column = 0; column < 4; column++)
                    {
                        matrix[row, column] =
                            node.matrix[column * 4 + row];
                    }
                }

                transform.localPosition = matrix.GetColumn(3);
                transform.localRotation = matrix.rotation;
                transform.localScale = matrix.lossyScale;

                return;
            }

            if (node.translation != null &&
                node.translation.Length >= 3)
            {
                transform.localPosition = new Vector3(
                    node.translation[0],
                    node.translation[1],
                    node.translation[2]);
            }

            if (node.rotation != null &&
                node.rotation.Length >= 4)
            {
                transform.localRotation = new Quaternion(
                    node.rotation[0],
                    node.rotation[1],
                    node.rotation[2],
                    node.rotation[3]);
            }

            if (node.scale != null &&
                node.scale.Length >= 3)
            {
                transform.localScale = new Vector3(
                    node.scale[0],
                    node.scale[1],
                    node.scale[2]);
            }
        }

        private static void BuildMesh(
            GlbDocument doc,
            GltfMesh source,
            GameObject gameObject,
            Material[] materials,
            bool colliders)
        {
            if (source.primitives == null ||
                source.primitives.Length == 0)
                return;

            List<Vector3> vertices = new List<Vector3>();
            List<Vector3> normals = new List<Vector3>();
            List<Vector2> uv = new List<Vector2>();

            List<List<int>> submeshTriangles =
                new List<List<int>>();

            List<Material> submeshMaterials =
                new List<Material>();

            bool hasNormals = true;
            bool hasUv = true;

            foreach (GltfPrimitive primitive in source.primitives)
            {
                if (primitive == null ||
                    primitive.attributes == null ||
                    primitive.attributes.POSITION < 0)
                    continue;

                if (primitive.mode != 4)
                {
                    throw new InvalidDataException(
                        "Only TRIANGLES primitives are supported.");
                }

                Vector3[] primitiveVertices =
                    doc.ReadVector3(
                        primitive.attributes.POSITION);

                Vector3[] primitiveNormals = null;

                if (primitive.attributes.NORMAL >= 0)
                {
                    primitiveNormals =
                        doc.ReadVector3(
                            primitive.attributes.NORMAL);
                }

                Vector2[] primitiveUv = null;

                if (primitive.attributes.TEXCOORD_0 >= 0)
                {
                    primitiveUv =
                        doc.ReadVector2(
                            primitive.attributes.TEXCOORD_0);
                }

                Log("[MESH DIAG] Mesh=" + source.name +
                    ", vertices=" + primitiveVertices.Length +
                    ", normals=" +
                    (primitiveNormals == null
                        ? 0
                        : primitiveNormals.Length) +
                    ", UVs=" +
                    (primitiveUv == null
                        ? 0
                        : primitiveUv.Length) +
                    ", materialIndex=" + primitive.material);

                if (primitiveUv != null &&
                    primitiveUv.Length > 0)
                {
                    Vector2 minUv = primitiveUv[0];
                    Vector2 maxUv = primitiveUv[0];
                    bool finiteUv = true;

                    for (int i = 0; i < primitiveUv.Length; i++)
                    {
                        Vector2 value = primitiveUv[i];

                        if (float.IsNaN(value.x) ||
                            float.IsNaN(value.y) ||
                            float.IsInfinity(value.x) ||
                            float.IsInfinity(value.y))
                        {
                            finiteUv = false;
                        }

                        minUv = Vector2.Min(minUv, value);
                        maxUv = Vector2.Max(maxUv, value);
                    }

                    Log("[UV DIAG] Mesh=" + source.name +
                        ", count=" + primitiveUv.Length +
                        ", min=(" + minUv.x + ", " + minUv.y + ")" +
                        ", max=(" + maxUv.x + ", " + maxUv.y + ")" +
                        ", first=(" + primitiveUv[0].x + ", " +
                        primitiveUv[0].y + ")" +
                        ", finite=" + finiteUv);
                }
                else
                {
                    Log("[UV DIAG] Mesh=" + source.name +
                        " has no TEXCOORD_0 data.");
                }

                int vertexOffset = vertices.Count;
                vertices.AddRange(primitiveVertices);

                if (primitiveNormals == null ||
                    primitiveNormals.Length != primitiveVertices.Length)
                {
                    hasNormals = false;
                }
                else
                {
                    normals.AddRange(primitiveNormals);
                }

                if (primitiveUv == null ||
                    primitiveUv.Length != primitiveVertices.Length)
                {
                    hasUv = false;
                }
                else
                {
                    uv.AddRange(primitiveUv);
                }

                int[] indices = primitive.indices >= 0
                    ? doc.ReadIndices(primitive.indices)
                    : MakeSequential(primitiveVertices.Length);

                List<int> triangles = new List<int>(indices.Length);

                for (int i = 0; i + 2 < indices.Length; i += 3)
                {
                    int a = indices[i];
                    int b = indices[i + 1];
                    int c = indices[i + 2];

                    if (a < 0 ||
                        a >= primitiveVertices.Length ||
                        b < 0 ||
                        b >= primitiveVertices.Length ||
                        c < 0 ||
                        c >= primitiveVertices.Length)
                    {
                        throw new InvalidDataException(
                            "Mesh index exceeds vertex count.");
                    }

                    triangles.Add(vertexOffset + a);
                    triangles.Add(vertexOffset + b);
                    triangles.Add(vertexOffset + c);
                }

                submeshTriangles.Add(triangles);

                int materialIndex =
                    primitive.material >= 0 &&
                    primitive.material < materials.Length - 1
                        ? primitive.material
                        : materials.Length - 1;

                submeshMaterials.Add(materials[materialIndex]);

                Log("[MESH DIAG] Selected material slot=" +
                    materialIndex + ", name=" +
                    materials[materialIndex].name);
            }

            if (vertices.Count == 0)
                return;

            Mesh mesh = new Mesh();

            mesh.name = string.IsNullOrEmpty(source.name)
                ? gameObject.name + "_Mesh"
                : source.name;

            if (vertices.Count > 65535)
            {
                mesh.indexFormat =
                    UnityEngine.Rendering.IndexFormat.UInt32;
            }

            Il2CppSystem.Collections.Generic.List<Vector3>
                il2CppVertices =
                    new Il2CppSystem.Collections.Generic.List<Vector3>();

            foreach (Vector3 vertex in vertices)
                il2CppVertices.Add(vertex);

            mesh.SetVertices(il2CppVertices);
            mesh.subMeshCount = submeshTriangles.Count;

            for (int i = 0; i < submeshTriangles.Count; i++)
            {
                Il2CppStructArray<int> triangleArray =
                    new Il2CppStructArray<int>(
                        submeshTriangles[i].ToArray());

                mesh.SetTriangles(triangleArray, i, false);
            }

            if (hasNormals && normals.Count == vertices.Count)
            {
                Il2CppSystem.Collections.Generic.List<Vector3>
                    il2CppNormals =
                        new Il2CppSystem.Collections.Generic.List<Vector3>();

                foreach (Vector3 normal in normals)
                    il2CppNormals.Add(normal);

                mesh.SetNormals(il2CppNormals);
            }
            else
            {
                Log("[MESH DIAG] Normals missing or incomplete; " +
                    "recalculating normals.");

                mesh.RecalculateNormals();
            }

            if (hasUv && uv.Count == vertices.Count)
            {
                Il2CppSystem.Collections.Generic.List<Vector4>
                    il2CppUv =
                        new Il2CppSystem.Collections.Generic.List<Vector4>();

                foreach (Vector2 coordinate in uv)
                {
                    // TEMPORARY UV TEST: V flip controlled by FlipUvV.
                    il2CppUv.Add(new Vector4(
                        coordinate.x,
                        FlipUvV ? 1f - coordinate.y : coordinate.y,
                        0f,
                        0f));
                }

                mesh.SetUVs(0, il2CppUv);

                Log("[MESH DIAG] UV coordinates applied. FlipUvV=" +
                    FlipUvV + ", Count=" + uv.Count);
            }
            else
            {
                Log("[MESH DIAG] UV coordinates missing or incomplete. " +
                    "Texture mapping may not work.");
            }

            mesh.RecalculateBounds();

            MeshFilter filter =
                gameObject.AddComponent<MeshFilter>();

            filter.sharedMesh = mesh;

            MeshRenderer renderer =
                gameObject.AddComponent<MeshRenderer>();

            renderer.sharedMaterials =
                new Il2CppReferenceArray<Material>(
                    submeshMaterials.ToArray());

            Log("[MESH DIAG] Renderer created. Mesh=" + mesh.name +
                ", submeshes=" + mesh.subMeshCount +
                ", materialSlots=" + submeshMaterials.Count);

            for (int i = 0; i < submeshMaterials.Count; i++)
            {
                Material material = submeshMaterials[i];

                Texture mainTexture = material != null
                    ? material.mainTexture
                    : null;

                Log("[TEXTURE TEST] Submesh=" + i +
                    ", material=" +
                    (material != null ? material.name : "NULL") +
                    ", shader=" +
                    (material != null && material.shader != null
                        ? material.shader.name
                        : "NULL") +
                    ", mainTexture=" +
                    (mainTexture != null ? mainTexture.name : "NULL") +
                    ", textureSize=" +
                    (mainTexture != null
                        ? mainTexture.width + "x" + mainTexture.height
                        : "N/A"));
            }

            if (colliders)
            {
                MeshCollider collider =
                    gameObject.AddComponent<MeshCollider>();

                collider.sharedMesh = mesh;
            }
        }

        private static int[] MakeSequential(int count)
        {
            int[] result = new int[count];

            for (int i = 0; i < count; i++)
                result[i] = i;

            return result;
        }
    }

    internal sealed class GlbDocument
    {
        internal GltfRoot json;
        internal byte[] binary;

        private static readonly ManualLogSource ParserLog =
            BepInEx.Logging.Logger.CreateLogSource(
                "ULTRADROID Models GLB Parser");

        internal static GlbDocument Parse(byte[] file)
        {
            LogParse("Parse ENTER; bytes=" +
                (file == null ? -1 : file.Length));

            if (file == null || file.Length < 20)
                throw new InvalidDataException(
                    "File is too small to be a GLB.");

            if (ReadU32(file, 0) != 0x46546C67)
                throw new InvalidDataException("Invalid GLB magic.");

            if (ReadU32(file, 4) != 2)
                throw new InvalidDataException(
                    "Only GLB version 2 is supported.");

            uint declaredLength = ReadU32(file, 8);

            if (declaredLength != file.Length)
            {
                throw new InvalidDataException(
                    "GLB length mismatch: declared=" +
                    declaredLength + ", actual=" + file.Length);
            }

            byte[] jsonBytes = null;
            byte[] binBytes = null;

            int position = 12;
            int chunkNumber = 0;

            while (position + 8 <= file.Length)
            {
                uint chunkLength = ReadU32(file, position);
                uint chunkType = ReadU32(file, position + 4);

                position += 8;

                if (chunkLength > file.Length - position)
                {
                    throw new InvalidDataException(
                        "GLB chunk exceeds file length.");
                }

                LogParse("Chunk " + chunkNumber +
                    ": type=0x" + chunkType.ToString("X8") +
                    ", bytes=" + chunkLength);

                if (chunkType == 0x4E4F534A)
                {
                    jsonBytes = new byte[(int)chunkLength];

                    Buffer.BlockCopy(
                        file, position, jsonBytes, 0, (int)chunkLength);
                }
                else if (chunkType == 0x004E4942)
                {
                    binBytes = new byte[(int)chunkLength];

                    Buffer.BlockCopy(
                        file, position, binBytes, 0, (int)chunkLength);
                }

                position += (int)chunkLength;
                chunkNumber++;
            }

            if (jsonBytes == null)
                throw new InvalidDataException(
                    "GLB JSON chunk is missing.");

            string jsonText =
                Encoding.UTF8.GetString(jsonBytes)
                    .TrimEnd('\0', ' ', '\r', '\n', '\t');

            LogParse("JSON extracted; chars=" + jsonText.Length);

            GltfRoot root;

            try
            {
                LogParse("Parsing glTF JSON with manual parser.");
                root = GlbManualJsonParser.ParseRoot(jsonText);
            }
            catch (Exception ex)
            {
                LogParseError("Manual JSON parsing failed", ex);
                throw;
            }

            if (root == null)
                throw new InvalidDataException(
                    "Manual JSON parser returned null.");

            if (binBytes == null)
                binBytes = new byte[0];

            if (root.asset == null || root.asset.version != "2.0")
            {
                throw new InvalidDataException(
                    "Missing or unsupported glTF asset version.");
            }

            LogParse("JSON parsed. Nodes=" + Count(root.nodes) +
                ", meshes=" + Count(root.meshes) +
                ", accessors=" + Count(root.accessors) +
                ", binaryBytes=" + binBytes.Length);

            return new GlbDocument
            {
                json = root,
                binary = binBytes
            };
        }

        private static void LogParse(string message)
        {
            ParserLog.LogInfo("[GLB PARSER] " + message);
        }

        private static void LogParseError(string message, Exception ex)
        {
            ParserLog.LogError("[GLB PARSER] " + message + "\n" + ex);
        }

        internal byte[] ReadBufferView(int index)
        {
            GltfBufferView view = GetView(index);

            byte[] result = new byte[view.byteLength];

            Buffer.BlockCopy(
                binary, view.byteOffset, result, 0, view.byteLength);

            return result;
        }

        internal Vector3[] ReadVector3(int accessorIndex)
        {
            float[] values = ReadFloats(accessorIndex, 3);
            Vector3[] result = new Vector3[values.Length / 3];

            for (int i = 0; i < result.Length; i++)
            {
                result[i] = new Vector3(
                    values[i * 3],
                    values[i * 3 + 1],
                    values[i * 3 + 2]);
            }

            return result;
        }

        internal Vector2[] ReadVector2(int accessorIndex)
        {
            float[] values = ReadFloats(accessorIndex, 2);
            Vector2[] result = new Vector2[values.Length / 2];

            for (int i = 0; i < result.Length; i++)
            {
                result[i] = new Vector2(
                    values[i * 2],
                    values[i * 2 + 1]);
            }

            return result;
        }

        internal float[] ReadFloats(int accessorIndex, int components)
        {
            GltfAccessor accessor = GetAccessor(accessorIndex);

            if (accessor.componentType != 5126)
            {
                throw new InvalidDataException(
                    "Expected FLOAT accessor for vector attributes.");
            }

            if (ComponentCount(accessor.type) != components)
            {
                throw new InvalidDataException(
                    "Unexpected accessor type for vector attribute.");
            }

            GltfBufferView view = GetView(accessor.bufferView);

            int elementSize = components * 4;
            int stride = view.byteStride > 0
                ? view.byteStride
                : elementSize;

            if (stride < elementSize)
            {
                throw new InvalidDataException(
                    "Accessor byte stride is too small.");
            }

            int start = checked(view.byteOffset + accessor.byteOffset);

            float[] result =
                new float[checked(accessor.count * components)];

            for (int i = 0; i < accessor.count; i++)
            {
                for (int c = 0; c < components; c++)
                {
                    int offset = checked(
                        start + i * stride + c * 4);

                    if (offset < 0 || offset + 4 > binary.Length)
                    {
                        throw new InvalidDataException(
                            "Vertex accessor exceeds binary data.");
                    }

                    result[i * components + c] =
                        BitConverter.ToSingle(binary, offset);
                }
            }

            return result;
        }

        internal int[] ReadIndices(int accessorIndex)
        {
            GltfAccessor accessor = GetAccessor(accessorIndex);

            if (accessor.type != "SCALAR")
            {
                throw new InvalidDataException(
                    "Index accessor must be SCALAR.");
            }

            if (accessor.componentType == 5126)
            {
                throw new InvalidDataException(
                    "Index accessor cannot use FLOAT.");
            }

            GltfBufferView view = GetView(accessor.bufferView);

            int elementSize =
                accessor.componentType == 5121 ? 1 :
                accessor.componentType == 5123 ? 2 :
                accessor.componentType == 5125 ? 4 : 0;

            if (elementSize == 0)
            {
                throw new InvalidDataException(
                    "Unsupported index component type.");
            }

            int stride = view.byteStride > 0
                ? view.byteStride
                : elementSize;

            if (stride < elementSize)
            {
                throw new InvalidDataException(
                    "Index byte stride is too small.");
            }

            int start = checked(view.byteOffset + accessor.byteOffset);
            int[] result = new int[accessor.count];

            for (int i = 0; i < accessor.count; i++)
            {
                int offset = checked(start + i * stride);

                if (offset < 0 || offset + elementSize > binary.Length)
                {
                    throw new InvalidDataException(
                        "Index accessor exceeds binary data.");
                }

                if (elementSize == 1)
                {
                    result[i] = binary[offset];
                }
                else if (elementSize == 2)
                {
                    result[i] = BitConverter.ToUInt16(binary, offset);
                }
                else
                {
                    uint value = BitConverter.ToUInt32(binary, offset);

                    if (value > int.MaxValue)
                    {
                        throw new InvalidDataException(
                            "Index is too large.");
                    }

                    result[i] = (int)value;
                }
            }

            return result;
        }

        private GltfAccessor GetAccessor(int index)
        {
            if (json.accessors == null ||
                index < 0 ||
                index >= json.accessors.Length)
            {
                throw new InvalidDataException(
                    "Accessor index is invalid.");
            }

            GltfAccessor accessor = json.accessors[index];

            if (accessor == null)
                throw new InvalidDataException(
                    "Accessor entry is null.");

            if (accessor.count < 0 || accessor.byteOffset < 0)
            {
                throw new InvalidDataException(
                    "Accessor has a negative count or offset.");
            }

            return accessor;
        }

        private GltfBufferView GetView(int index)
        {
            if (json.bufferViews == null ||
                index < 0 ||
                index >= json.bufferViews.Length)
            {
                throw new InvalidDataException(
                    "Buffer view index is invalid.");
            }

            GltfBufferView view = json.bufferViews[index];

            if (view == null)
                throw new InvalidDataException(
                    "Buffer view entry is null.");

            if (view.byteOffset < 0 ||
                view.byteLength < 0 ||
                view.byteOffset > binary.Length ||
                view.byteLength > binary.Length - view.byteOffset)
            {
                throw new InvalidDataException(
                    "Buffer view is outside binary data.");
            }

            if (view.buffer != 0)
            {
                throw new InvalidDataException(
                    "Only the embedded GLB binary buffer is supported.");
            }

            return view;
        }

        private static int ComponentCount(string type)
        {
            if (type == "SCALAR") return 1;
            if (type == "VEC2") return 2;
            if (type == "VEC3") return 3;
            if (type == "VEC4") return 4;

            throw new InvalidDataException(
                "Unsupported accessor type: " + type);
        }

        private static int Count<T>(T[] array)
        {
            return array == null ? 0 : array.Length;
        }

        private static uint ReadU32(byte[] bytes, int offset)
        {
            return BitConverter.ToUInt32(bytes, offset);
        }
    }

    [Serializable]
    internal class GltfRoot
    {
        public GltfAsset asset;
        public int scene;
        public GltfScene[] scenes;
        public GltfNode[] nodes;
        public GltfMesh[] meshes;
        public GltfMaterial[] materials;
        public GltfTexture[] textures;
        public GltfImage[] images;
        public GltfBufferView[] bufferViews;
        public GltfAccessor[] accessors;
    }

    [Serializable]
    internal class GltfAsset
    {
        public string version;
    }

    [Serializable]
    internal class GltfScene
    {
        public int[] nodes;
    }

    [Serializable]
    internal class GltfNode
    {
        public string name;
        public int mesh = -1;
        public int[] children;
        public float[] translation;
        public float[] rotation;
        public float[] scale;
        public float[] matrix;
    }

    [Serializable]
    internal class GltfMesh
    {
        public string name;
        public GltfPrimitive[] primitives;
    }

    [Serializable]
    internal class GltfPrimitive
    {
        public GltfAttributes attributes;
        public int indices = -1;
        public int material = -1;
        public int mode = 4;
    }

    [Serializable]
    internal class GltfAttributes
    {
        public int POSITION = -1;
        public int NORMAL = -1;
        public int TANGENT = -1;
        public int TEXCOORD_0 = -1;
    }

    [Serializable]
    internal class GltfMaterial
    {
        public string name;
        public PbrMetallicRoughness pbrMetallicRoughness;
        public float[] emissiveFactor;
    }

    [Serializable]
    internal class PbrMetallicRoughness
    {
        public float[] baseColorFactor;
        public GltfTextureInfo baseColorTexture;
        public GltfTextureInfo metallicRoughnessTexture;
        public float metallicFactor = 1f;
        public float roughnessFactor = 1f;
    }

    [Serializable]
    internal class GltfTextureInfo
    {
        public int index;
    }

    [Serializable]
    internal class GltfTexture
    {
        public int source = -1;
    }

    [Serializable]
    internal class GltfImage
    {
        public int bufferView = -1;
        public string mimeType;
    }

    [Serializable]
    internal class GltfBufferView
    {
        public int buffer;
        public int byteOffset;
        public int byteLength;
        public int byteStride;
    }

    [Serializable]
    internal class GltfAccessor
    {
        public int bufferView = -1;
        public int byteOffset;
        public int componentType;
        public int count;
        public string type;
        public bool normalized;
    }
}
