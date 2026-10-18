using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
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
        public bool FlipUvV = true;
    }

    [Serializable]
    public sealed class GlbAnimationData
    {
        public string Name;
        public float Duration;
        public GlbAnimationTrack[] Tracks;
    }

    [Serializable]
    public sealed class GlbAnimationTrack
    {
        public int TargetNode = -1;
        public string Path;
        public string Interpolation = "LINEAR";
        public float[] Times;
        public float[] Values;
        public int ComponentsPerElement;
        public int ValuesPerKeyframe;
    }

    public static class ModelAPI
    {
        public const string Version = "0.1.17-skinning";

        private sealed class CpuSkinningEntry
        {
            public GameObject ModelRoot;
            public Transform MeshTransform;
            public Mesh Mesh;

            public Vector3[] OriginalVertices;
            public Vector3[] OriginalNormals;

            public Transform[] Bones;
            public Matrix4x4[] BindPoses;

            public byte[] BonesPerVertex;
            public GlbBoneWeight1[] Weights;

            public Matrix4x4[] SkinMatrices;
            public Vector3[] DeformedVertices;
        }

        private static readonly List<CpuSkinningEntry>
            CpuSkinningEntries = new List<CpuSkinningEntry>();

        internal static readonly ManualLogSource DiagnosticLog =
            BepInEx.Logging.Logger.CreateLogSource(
                "ULTRADROID Models API");

        private static void LogError(string message, Exception ex)
        {
            DiagnosticLog.LogError(
                "[ULTRADROID Models] " + message +
                (ex == null ? "" : "\n" + ex));
        }

        public static GlbAnimationData[] ReadGLBAnimations(string path)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentException(
                    "A GLB path is required.", "path");

            if (!File.Exists(path))
                throw new FileNotFoundException(
                    "GLB file was not found.", path);

            try
            {
                GlbDocument doc =
                    GlbDocument.Parse(File.ReadAllBytes(path));

                return doc.ReadAnimations();
            }
            catch (Exception ex)
            {
                LogError("Failed to read GLB animations.", ex);
                throw;
            }
        }

        public static GameObject LoadGLB(
            string path,
            Transform parent = null,
            ModelLoadOptions options = null)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentException(
                    "A GLB path is required.", "path");

            if (!File.Exists(path))
                throw new FileNotFoundException(
                    "GLB file was not found.", path);

            options = options ?? new ModelLoadOptions();

            GlbDocument doc;

            try
            {
                doc = GlbDocument.Parse(File.ReadAllBytes(path));
            }
            catch (Exception ex)
            {
                LogError("GLB parsing failed.", ex);
                throw;
            }

            if (doc == null || doc.json == null)
                throw new InvalidDataException(
                    "GLB parser returned an empty document.");

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

                BuildScene(doc, root.transform, materials, options);

                return root;
            }
            catch (Exception ex)
            {
                LogError("LoadGLB failed while building the model.", ex);
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

            if (!load || count == 0)
                return result;

            for (int i = 0; i < count; i++)
            {
                GltfImage image = doc.json.images[i];

                if (image == null || image.bufferView < 0)
                    continue;

                byte[] bytes;

                try
                {
                    bytes = doc.ReadBufferView(image.bufferView);
                }
                catch (Exception ex)
                {
                    LogError("Image " + i + " extraction failed.", ex);
                    continue;
                }

                if (bytes == null || bytes.Length == 0)
                    continue;

                Texture2D texture = null;

                try
                {
                    texture = new Texture2D(
                        2, 2, TextureFormat.RGBA32, true);

                    if (!ImageConversion.LoadImage(texture, bytes, false))
                    {
                        UnityEngine.Object.Destroy(texture);
                        continue;
                    }

                    texture.name = "ULTRADROID_GLTF_Image_" + i;
                    texture.wrapMode = TextureWrapMode.Repeat;
                    texture.filterMode = FilterMode.Bilinear;
                    result[i] = texture;
                }
                catch (Exception ex)
                {
                    LogError("Image " + i + " decoding failed.", ex);

                    if (texture != null)
                        UnityEngine.Object.Destroy(texture);
                }
            }

            return result;
        }

        private static Material[] BuildMaterials(
            GlbDocument doc,
            Texture2D[] textures)
        {
            int count = Count(doc.json.materials);
            Material[] result = new Material[count + 1];

            Dictionary<string, Texture2D> convertedCache =
                new Dictionary<string, Texture2D>();

            Shader shader = Shader.Find("Standard");

            if (shader == null)
                shader = Shader.Find("Universal Render Pipeline/Lit");

            if (shader == null)
                shader = Shader.Find("Sprites/Default");

            if (shader == null)
                throw new InvalidOperationException(
                    "No compatible Unity shader was found.");

            for (int i = 0; i < count; i++)
            {
                GltfMaterial source = doc.json.materials[i];

                if (source == null)
                    continue;

                Material material = new Material(shader);

                material.name = string.IsNullOrEmpty(source.name)
                    ? "GLTF_Material_" + i
                    : source.name;

                PbrMetallicRoughness pbr =
                    source.pbrMetallicRoughness;

                if (pbr != null)
                {
                    float metallic =
                        Mathf.Clamp01(pbr.metallicFactor);

                    float roughness =
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
                        int imageIndex = GetImageIndex(
                            doc, pbr.baseColorTexture.index);

                        if (imageIndex >= 0 &&
                            imageIndex < textures.Length &&
                            textures[imageIndex] != null)
                        {
                            if (material.HasProperty("_MainTex"))
                                material.SetTexture(
                                    "_MainTex", textures[imageIndex]);

                            if (material.HasProperty("_BaseMap"))
                                material.SetTexture(
                                    "_BaseMap", textures[imageIndex]);
                        }
                    }

                    float smoothness = 1f - roughness;

                    if (material.HasProperty("_Metallic"))
                        material.SetFloat("_Metallic", metallic);

                    if (material.HasProperty("_Glossiness"))
                        material.SetFloat("_Glossiness", smoothness);

                    if (material.HasProperty("_Smoothness"))
                        material.SetFloat("_Smoothness", smoothness);

                    if (pbr.metallicRoughnessTexture != null)
                    {
                        int imageIndex = GetImageIndex(
                            doc, pbr.metallicRoughnessTexture.index);

                        if (imageIndex >= 0 &&
                            imageIndex < textures.Length &&
                            textures[imageIndex] != null &&
                            material.HasProperty("_MetallicGlossMap") &&
                            material.HasProperty("_GlossMapScale"))
                        {
                            string key =
                                imageIndex + "|" +
                                metallic.ToString("R") + "|" +
                                roughness.ToString("R");

                            Texture2D converted;

                            if (!convertedCache.TryGetValue(key, out converted))
                            {
                                converted = BuildUnityMetallicGlossTexture(
                                    textures[imageIndex], metallic, roughness);

                                convertedCache[key] = converted;
                            }

                            if (converted != null)
                            {
                                material.SetTexture(
                                    "_MetallicGlossMap", converted);

                                material.SetFloat("_Metallic", 1f);
                                material.SetFloat("_GlossMapScale", 1f);
                                material.EnableKeyword("_METALLICGLOSSMAP");
                            }
                        }
                    }
                }

                result[i] = material;
            }

            result[count] = new Material(shader)
            {
                name = "GLTF_Default_Material"
            };

            return result;
        }

        private static Texture2D BuildUnityMetallicGlossTexture(
            Texture2D source,
            float metallicFactor,
            float roughnessFactor)
        {
            if (source == null)
                return null;

            try
            {
                Color32[] pixels = source.GetPixels32();

                if (pixels == null || pixels.Length == 0)
                    return null;

                Color32[] convertedPixels =
                    new Color32[pixels.Length];

                for (int i = 0; i < pixels.Length; i++)
                {
                    float metallic =
                        pixels[i].b / 255f * metallicFactor;

                    float roughness =
                        pixels[i].g / 255f * roughnessFactor;

                    byte m = (byte)Mathf.Clamp(
                        Mathf.RoundToInt(metallic * 255f), 0, 255);

                    byte s = (byte)Mathf.Clamp(
                        Mathf.RoundToInt((1f - roughness) * 255f),
                        0, 255);

                    convertedPixels[i] = new Color32(m, 0, 0, s);
                }

                Texture2D converted = new Texture2D(
                    source.width,
                    source.height,
                    TextureFormat.RGBA32,
                    true,
                    true);

                converted.name = source.name + "_UnityMetallicGloss";
                converted.wrapMode = source.wrapMode;
                converted.filterMode = source.filterMode;
                converted.SetPixels32(convertedPixels);
                converted.Apply(true, false);

                return converted;
            }
            catch (Exception ex)
            {
                LogError("Metallic-roughness conversion failed.", ex);
                return null;
            }
        }

        private static int GetImageIndex(
            GlbDocument doc,
            int textureIndex)
        {
            if (doc.json.textures == null ||
                textureIndex < 0 ||
                textureIndex >= doc.json.textures.Length)
                return -1;

            GltfTexture texture = doc.json.textures[textureIndex];

            return texture == null ? -1 : texture.source;
        }

        // ------------------------------------------------------------
        // SCENE AND NODE HIERARCHY
        // ------------------------------------------------------------

        private static void BuildScene(
            GlbDocument doc,
            Transform root,
            Material[] materials,
            ModelLoadOptions options)
        {
            if (doc.json.nodes == null ||
                doc.json.nodes.Length == 0)
                return;

            int count = doc.json.nodes.Length;

            GameObject[] nodeObjects = new GameObject[count];
            Transform[] nodeTransforms = new Transform[count];

            int[] roots = GetSceneRoots(doc);

            HashSet<int> guard = new HashSet<int>();

            foreach (int index in roots)
            {
                CreateNodeHierarchy(
                    doc, index, root, nodeObjects, nodeTransforms, guard);
            }

            for (int i = 0; i < count; i++)
            {
                if (nodeObjects[i] == null)
                {
                    CreateNodeHierarchy(
                        doc, i, root, nodeObjects, nodeTransforms, guard);
                }
            }

            for (int i = 0; i < count; i++)
            {
                GltfNode node = doc.json.nodes[i];

                if (node == null || node.mesh < 0 ||
                    doc.json.meshes == null ||
                    node.mesh >= doc.json.meshes.Length)
                    continue;

                GltfMesh mesh = doc.json.meshes[node.mesh];

                if (mesh == null || nodeObjects[i] == null)
                    continue;

                BuildMesh(
                    doc,
                    mesh,
                    nodeObjects[i],
                    materials,
                    options,
                    node.skin,
                    nodeTransforms,
                    root.gameObject);
            }
        }

        private static int[] GetSceneRoots(GlbDocument doc)
        {
            int sceneIndex = doc.json.scene;

            if (doc.json.scenes != null &&
                doc.json.scenes.Length > 0)
            {
                if (sceneIndex < 0 ||
                    sceneIndex >= doc.json.scenes.Length)
                    sceneIndex = 0;

                GltfScene scene = doc.json.scenes[sceneIndex];

                if (scene != null && scene.nodes != null &&
                    scene.nodes.Length > 0)
                    return scene.nodes;
            }

            HashSet<int> children = new HashSet<int>();

            foreach (GltfNode node in doc.json.nodes)
            {
                if (node == null || node.children == null)
                    continue;

                foreach (int child in node.children)
                {
                    if (child >= 0 && child < doc.json.nodes.Length)
                        children.Add(child);
                }
            }

            List<int> roots = new List<int>();

            for (int i = 0; i < doc.json.nodes.Length; i++)
            {
                if (!children.Contains(i))
                    roots.Add(i);
            }

            return roots.ToArray();
        }

        private static void CreateNodeHierarchy(
            GlbDocument doc,
            int index,
            Transform parent,
            GameObject[] objects,
            Transform[] transforms,
            HashSet<int> guard)
        {
            if (index < 0 || index >= doc.json.nodes.Length)
                return;

            if (guard.Contains(index))
            {
                LogError(
                    "Node hierarchy cycle detected at node " + index + ".",
                    null);
                return;
            }

            if (objects[index] != null)
                return;

            GltfNode node = doc.json.nodes[index];

            if (node == null)
                return;

            guard.Add(index);

            GameObject go = new GameObject(
                string.IsNullOrEmpty(node.name)
                    ? "GLTF_Node_" + index
                    : node.name);

            objects[index] = go;
            transforms[index] = go.transform;

            go.transform.SetParent(parent, false);
            ApplyNodeTransform(go.transform, node);

            if (node.children != null)
            {
                foreach (int child in node.children)
                {
                    CreateNodeHierarchy(
                        doc,
                        child,
                        go.transform,
                        objects,
                        transforms,
                        guard);
                }
            }

            guard.Remove(index);
        }

        private static void ApplyNodeTransform(
            Transform transform,
            GltfNode node)
        {
            if (node.matrix != null && node.matrix.Length == 16)
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

                Vector3 position;
                Quaternion rotation;
                Vector3 scale;

                position = matrix.GetColumn(3);
                rotation = matrix.rotation;
                scale = matrix.lossyScale;

                transform.localPosition = position;
                transform.localRotation = rotation;
                transform.localScale = scale;
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

            if (node.rotation != null && node.rotation.Length >= 4)
            {
                transform.localRotation = new Quaternion(
                    node.rotation[0],
                    node.rotation[1],
                    node.rotation[2],
                    node.rotation[3]);
            }

            if (node.scale != null && node.scale.Length >= 3)
            {
                transform.localScale = new Vector3(
                    node.scale[0],
                    node.scale[1],
                    node.scale[2]);
            }
        }

        // ------------------------------------------------------------
        // VARIABLE-INFLUENCE BONE WEIGHTS
        // ------------------------------------------------------------

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        private struct GlbBoneWeight1
        {
            public int boneIndex;
            public float weight;

            public GlbBoneWeight1(int boneIndex, float weight)
            {
                this.boneIndex = boneIndex;
                this.weight = weight;
            }
        }

        private static void SetBoneWeightsCompat(
            Mesh mesh,
            List<byte> bonesPerVertex,
            List<GlbBoneWeight1> allBoneWeights)
        {
            if (mesh == null)
                throw new ArgumentNullException(nameof(mesh));

            if (bonesPerVertex == null)
                throw new ArgumentNullException(nameof(bonesPerVertex));

            if (allBoneWeights == null)
                throw new ArgumentNullException(nameof(allBoneWeights));

            if (bonesPerVertex.Count != mesh.vertexCount)
            {
                throw new InvalidDataException(
                    "Bone influence counts do not match the mesh vertex count.");
            }

            long expectedWeightCount = 0;

            foreach (byte count in bonesPerVertex)
                expectedWeightCount += count;

            if (expectedWeightCount != allBoneWeights.Count)
            {
                throw new InvalidDataException(
                    "Bone influence counts do not match the flattened weight array.");
            }

            byte[] vertexCounts = bonesPerVertex.ToArray();
            GlbBoneWeight1[] flattenedWeights = allBoneWeights.ToArray();

            GCHandle countsHandle = default(GCHandle);
            GCHandle weightsHandle = default(GCHandle);

            try
            {
                countsHandle = GCHandle.Alloc(
                    vertexCounts, GCHandleType.Pinned);

                weightsHandle = GCHandle.Alloc(
                    flattenedWeights, GCHandleType.Pinned);

                mesh.InternalSetBoneWeights(
                    countsHandle.AddrOfPinnedObject(),
                    vertexCounts.Length,
                    weightsHandle.AddrOfPinnedObject(),
                    flattenedWeights.Length);
            }
            finally
            {
                if (weightsHandle.IsAllocated)
                    weightsHandle.Free();

                if (countsHandle.IsAllocated)
                    countsHandle.Free();
            }
        }

        // ------------------------------------------------------------
        // CPU SKINNING FALLBACK
        // ------------------------------------------------------------

        private static void RegisterCpuSkinning(
            GameObject modelRoot,
            Transform meshTransform,
            Mesh mesh,
            Vector3[] originalVertices,
            Vector3[] originalNormals,
            Transform[] bones,
            Matrix4x4[] bindPoses,
            List<byte> bonesPerVertex,
            List<GlbBoneWeight1> allBoneWeights)
        {
            if (modelRoot == null ||
                meshTransform == null ||
                mesh == null ||
                originalVertices == null ||
                bones == null ||
                bindPoses == null ||
                bonesPerVertex == null ||
                allBoneWeights == null)
            {
                throw new ArgumentNullException(
                    "CPU skinning received missing data.");
            }

            if (bonesPerVertex.Count != originalVertices.Length)
            {
                throw new InvalidDataException(
                    "CPU skinning vertex count mismatch.");
            }

            int expectedWeights = 0;

            foreach (byte count in bonesPerVertex)
                expectedWeights += count;

            if (expectedWeights != allBoneWeights.Count)
            {
                throw new InvalidDataException(
                    "CPU skinning weight count mismatch.");
            }

            CpuSkinningEntry entry = new CpuSkinningEntry
            {
                ModelRoot = modelRoot,
                MeshTransform = meshTransform,
                Mesh = mesh,

                OriginalVertices = (Vector3[])originalVertices.Clone(),

                OriginalNormals = originalNormals == null
                    ? null
                    : (Vector3[])originalNormals.Clone(),

                Bones = (Transform[])bones.Clone(),
                BindPoses = (Matrix4x4[])bindPoses.Clone(),

                BonesPerVertex = bonesPerVertex.ToArray(),
                Weights = allBoneWeights.ToArray(),

                SkinMatrices = new Matrix4x4[bones.Length],
                DeformedVertices = new Vector3[originalVertices.Length]
            };

            CpuSkinningEntries.Add(entry);

            DiagnosticLog.LogInfo(
                "[ULTRADROID Models] CPU skinning registered: " +
                mesh.name +
                ", vertices=" + originalVertices.Length +
                ", bones=" + bones.Length +
                ", weights=" + allBoneWeights.Count);
        }

        public static void UpdateCpuSkinning(GameObject modelRoot)
        {
            if (modelRoot == null)
                return;

            for (int e = CpuSkinningEntries.Count - 1; e >= 0; e--)
            {
                CpuSkinningEntry entry = CpuSkinningEntries[e];

                if (entry.ModelRoot == null ||
                    entry.Mesh == null ||
                    entry.MeshTransform == null)
                {
                    CpuSkinningEntries.RemoveAt(e);
                    continue;
                }

                if (entry.ModelRoot != modelRoot)
                    continue;

                try
                {
                    UpdateCpuSkinningEntry(entry);
                }
                catch (Exception ex)
                {
                    LogError(
                        "CPU skinning update failed for '" +
                        entry.Mesh.name + "'.",
                        ex);

                    CpuSkinningEntries.RemoveAt(e);
                }
            }
        }

        private static void UpdateCpuSkinningEntry(
            CpuSkinningEntry entry)
        {
            Transform meshTransform = entry.MeshTransform;

            Matrix4x4 worldToMesh =
                meshTransform.worldToLocalMatrix;

            for (int b = 0; b < entry.Bones.Length; b++)
            {
                Transform bone = entry.Bones[b];

                if (bone == null)
                    throw new InvalidOperationException(
                        "CPU skinning encountered a missing bone.");

                entry.SkinMatrices[b] =
                    worldToMesh *
                    bone.localToWorldMatrix *
                    entry.BindPoses[b];
            }

            int weightIndex = 0;

            for (int v = 0; v < entry.OriginalVertices.Length; v++)
            {
                Vector3 original = entry.OriginalVertices[v];
                Vector3 result = Vector3.zero;

                int influenceCount = entry.BonesPerVertex[v];
                float totalWeight = 0f;

                for (int j = 0; j < influenceCount; j++)
                {
                    GlbBoneWeight1 weight =
                        entry.Weights[weightIndex++];

                    if (weight.boneIndex < 0 ||
                        weight.boneIndex >= entry.SkinMatrices.Length)
                    {
                        throw new InvalidDataException(
                            "CPU skinning bone index is invalid.");
                    }

                    if (weight.weight <= 0f)
                        continue;

                    Vector3 transformed =
                        entry.SkinMatrices[weight.boneIndex]
                            .MultiplyPoint3x4(original);

                    result += transformed * weight.weight;
                    totalWeight += weight.weight;
                }

                if (totalWeight > 0.000001f)
                    entry.DeformedVertices[v] = result / totalWeight;
                else
                    entry.DeformedVertices[v] = original;
            }

            entry.Mesh.vertices = entry.DeformedVertices;

            // Keep the mesh's visible bounds up to date as it deforms.
            entry.Mesh.RecalculateBounds();

            // Recalculate normals so lighting follows the deformation.
            // This can be expensive on large meshes.
            entry.Mesh.RecalculateNormals();
        }

        // ------------------------------------------------------------
        // MESH BUILDING, INCLUDING SKINNED MESHES
        // ------------------------------------------------------------

        private static void BuildMesh(
            GlbDocument doc,
            GltfMesh source,
            GameObject gameObject,
            Material[] materials,
            ModelLoadOptions options,
            int skinIndex,
            Transform[] nodeTransforms,
            GameObject modelRoot)
        {
            if (source.primitives == null ||
                source.primitives.Length == 0)
                return;

            GltfSkin skin = null;
            Transform[] bones = null;
            Transform rootBone = null;

            bool skinned =
                skinIndex >= 0 &&
                doc.json.skins != null &&
                skinIndex < doc.json.skins.Length;

            if (skinned)
            {
                skin = doc.json.skins[skinIndex];

                if (skin == null || skin.joints == null ||
                    skin.joints.Length == 0)
                {
                    throw new InvalidDataException(
                        "Skin has no joints.");
                }

                bones = new Transform[skin.joints.Length];

                for (int i = 0; i < skin.joints.Length; i++)
                {
                    int jointNode = skin.joints[i];

                    if (jointNode < 0 ||
                        jointNode >= nodeTransforms.Length ||
                        nodeTransforms[jointNode] == null)
                    {
                        throw new InvalidDataException(
                            "Skin joint references an invalid node: " +
                            jointNode);
                    }

                    bones[i] = nodeTransforms[jointNode];
                }

                if (skin.skeleton >= 0 &&
                    skin.skeleton < nodeTransforms.Length)
                {
                    rootBone = nodeTransforms[skin.skeleton];
                }

                if (rootBone == null)
                    rootBone = bones[0];
            }

            List<Vector3> vertices = new List<Vector3>();
            List<Vector3> normals = new List<Vector3>();
            List<Vector2> uv = new List<Vector2>();
            List<byte> bonesPerVertex = new List<byte>();
            List<GlbBoneWeight1> allBoneWeights =
                new List<GlbBoneWeight1>();

            List<List<int>> submeshTriangles =
                new List<List<int>>();

            List<Material> submeshMaterials =
                new List<Material>();

            bool hasNormals = true;
            bool hasUv = true;
            bool hasBoneWeights = skinned;

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
                    doc.ReadVector3(primitive.attributes.POSITION);

                Vector3[] primitiveNormals = null;

                if (primitive.attributes.NORMAL >= 0)
                {
                    primitiveNormals =
                        doc.ReadVector3(primitive.attributes.NORMAL);
                }

                Vector2[] primitiveUv = null;

                if (primitive.attributes.TEXCOORD_0 >= 0)
                {
                    primitiveUv =
                        doc.ReadVector2(primitive.attributes.TEXCOORD_0);
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

                if (skinned)
                {
                    int jointsAccessor = primitive.attributes.JOINTS_0;
                    int weightsAccessor = primitive.attributes.WEIGHTS_0;

                    if (jointsAccessor < 0 || weightsAccessor < 0)
                    {
                        hasBoneWeights = false;
                    }
                    else
                    {
                        int[] joints =
                            doc.ReadJointIndices(jointsAccessor);

                        float[] weights =
                            doc.ReadWeights(weightsAccessor);

                        int vertexCount = primitiveVertices.Length;

                        if (joints.Length != vertexCount * 4 ||
                            weights.Length != vertexCount * 4)
                        {
                            throw new InvalidDataException(
                                "JOINTS_0 or WEIGHTS_0 count does not " +
                                "match the primitive vertex count.");
                        }

                        for (int v = 0; v < vertexCount; v++)
                        {
                            int offset = v * 4;

                            int influenceCount = 0;
                            float totalWeight = 0f;

                            for (int j = 0; j < 4; j++)
                            {
                                float weight = weights[offset + j];

                                if (float.IsNaN(weight) ||
                                    float.IsInfinity(weight) ||
                                    weight < 0f)
                                {
                                    throw new InvalidDataException(
                                        "Vertex contains an invalid bone weight.");
                                }

                                if (weight <= 0f)
                                    continue;

                                int boneIndex = joints[offset + j];
                                ValidateBoneIndex(boneIndex, bones.Length);

                                totalWeight += weight;
                                influenceCount++;
                            }

                            if (influenceCount == 0 || totalWeight <= 0.000001f)
                            {
                                bonesPerVertex.Add(1);
                                allBoneWeights.Add(new GlbBoneWeight1(0, 1f));
                                continue;
                            }

                            for (int j = 0; j < 4; j++)
                            {
                                float weight = weights[offset + j];

                                if (weight <= 0f)
                                    continue;

                                int boneIndex = joints[offset + j];

                                allBoneWeights.Add(
                                    new GlbBoneWeight1(
                                        boneIndex,
                                        weight / totalWeight));
                            }

                            bonesPerVertex.Add((byte)influenceCount);
                        }
                    }
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

                    if (a < 0 || a >= primitiveVertices.Length ||
                        b < 0 || b >= primitiveVertices.Length ||
                        c < 0 || c >= primitiveVertices.Length)
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

            Il2CppSystem.Collections.Generic.List<Vector3> ilVertices =
                new Il2CppSystem.Collections.Generic.List<Vector3>();

            foreach (Vector3 vertex in vertices)
                ilVertices.Add(vertex);

            mesh.SetVertices(ilVertices);
            mesh.subMeshCount = submeshTriangles.Count;

            for (int i = 0; i < submeshTriangles.Count; i++)
            {
                mesh.SetTriangles(
                    new Il2CppStructArray<int>(
                        submeshTriangles[i].ToArray()),
                    i,
                    false);
            }

            if (hasNormals && normals.Count == vertices.Count)
            {
                Il2CppSystem.Collections.Generic.List<Vector3> ilNormals =
                    new Il2CppSystem.Collections.Generic.List<Vector3>();

                foreach (Vector3 normal in normals)
                    ilNormals.Add(normal);

                mesh.SetNormals(ilNormals);
            }
            else
            {
                mesh.RecalculateNormals();
            }

            if (hasUv && uv.Count == vertices.Count)
            {
                Il2CppSystem.Collections.Generic.List<Vector4> ilUv =
                    new Il2CppSystem.Collections.Generic.List<Vector4>();

                foreach (Vector2 coordinate in uv)
                {
                    ilUv.Add(new Vector4(
                        coordinate.x,
                        options.FlipUvV
                            ? 1f - coordinate.y
                            : coordinate.y,
                        0f,
                        0f));
                }

                mesh.SetUVs(0, ilUv);
            }

            // ---------- SAFE SKIN / FALLBACK BLOCK ----------
            bool skinDataReady =
                skinned &&
                hasBoneWeights &&
                bonesPerVertex.Count == vertices.Count;

            Matrix4x4[] bindPoses = null;

            if (skinDataReady)
            {
                bindPoses = BuildBindPoses(
                    doc,
                    skin,
                    bones,
                    gameObject.transform);
            }

            bool skinWeightsApplied = false;

            if (skinDataReady)
            {
                try
                {
                    SetBoneWeightsCompat(
                        mesh,
                        bonesPerVertex,
                        allBoneWeights);

                    skinWeightsApplied = true;
                }
                catch (Exception ex)
                {
                    LogError(
                        "Native bone-weight assignment failed. " +
                        "Using CPU skinning for '" +
                        gameObject.name + "'.",
                        ex);
                }
            }

            if (skinWeightsApplied)
            {
                mesh.bindposes =
                    new Il2CppStructArray<Matrix4x4>(bindPoses);

                mesh.RecalculateBounds();

                SkinnedMeshRenderer renderer =
                    gameObject.AddComponent<SkinnedMeshRenderer>();

                renderer.sharedMesh = mesh;

                renderer.bones =
                    new Il2CppReferenceArray<Transform>(bones);

                renderer.rootBone = rootBone;

                renderer.sharedMaterials =
                    new Il2CppReferenceArray<Material>(
                        submeshMaterials.ToArray());

                if (options.CreateColliders)
                {
                    MeshCollider collider =
                        gameObject.AddComponent<MeshCollider>();

                    collider.sharedMesh = mesh;
                }
            }
            else
            {
                mesh.RecalculateBounds();

                MeshFilter filter =
                    gameObject.AddComponent<MeshFilter>();

                filter.sharedMesh = mesh;

                MeshRenderer renderer =
                    gameObject.AddComponent<MeshRenderer>();

                renderer.sharedMaterials =
                    new Il2CppReferenceArray<Material>(
                        submeshMaterials.ToArray());

                if (skinned && skinDataReady)
                {
                    RegisterCpuSkinning(
                        modelRoot,
                        gameObject.transform,
                        mesh,
                        vertices.ToArray(),
                        hasNormals && normals.Count == vertices.Count
                            ? normals.ToArray()
                            : null,
                        bones,
                        bindPoses,
                        bonesPerVertex,
                        allBoneWeights);
                }
                else if (skinned)
                {
                    LogError(
                        "CPU skinning unavailable because valid " +
                        "JOINTS_0/WEIGHTS_0 data is missing for '" +
                        gameObject.name + "'.",
                        null);
                }

                if (options.CreateColliders)
                {
                    MeshCollider collider =
                        gameObject.AddComponent<MeshCollider>();

                    collider.sharedMesh = mesh;
                }
            }
        }

        private static void ValidateBoneIndex(
            int boneIndex,
            int boneCount)
        {
            if (boneIndex < 0 || boneIndex >= boneCount)
            {
                throw new InvalidDataException(
                    "Vertex joint index " + boneIndex +
                    " exceeds the skin's joint count " + boneCount + ".");
            }
        }

        private static Matrix4x4[] BuildBindPoses(
            GlbDocument doc,
            GltfSkin skin,
            Transform[] bones,
            Transform meshTransform)
        {
            if (skin.inverseBindMatrices >= 0)
            {
                Matrix4x4[] matrices =
                    doc.ReadMatrix4x4(skin.inverseBindMatrices);

                if (matrices.Length != bones.Length)
                {
                    throw new InvalidDataException(
                        "Inverse bind matrix count does not match " +
                        "the skin's joint count.");
                }

                return matrices;
            }

            Matrix4x4[] result = new Matrix4x4[bones.Length];

            for (int i = 0; i < bones.Length; i++)
            {
                result[i] =
                    bones[i].worldToLocalMatrix *
                    meshTransform.localToWorldMatrix;
            }

            return result;
        }

        private static int[] MakeSequential(int count)
        {
            int[] result = new int[count];

            for (int i = 0; i < count; i++)
                result[i] = i;

            return result;
        }
    }

    // ------------------------------------------------------------
    // GLB FILE READER
    // ------------------------------------------------------------

    internal sealed class GlbDocument
    {
        internal GltfRoot json;
        internal byte[] binary;

        internal static GlbDocument Parse(byte[] file)
        {
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

            while (position + 8 <= file.Length)
            {
                uint chunkLength = ReadU32(file, position);
                uint chunkType = ReadU32(file, position + 4);

                position += 8;

                if (chunkLength > file.Length - position)
                    throw new InvalidDataException(
                        "GLB chunk exceeds file length.");

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
            }

            if (jsonBytes == null)
                throw new InvalidDataException(
                    "GLB JSON chunk is missing.");

            string jsonText =
                Encoding.UTF8.GetString(jsonBytes)
                    .TrimEnd('\0', ' ', '\r', '\n', '\t');

            GltfRoot root;

            try
            {
                root = GlbManualJsonParser.ParseRoot(jsonText);
            }
            catch (Exception ex)
            {
                ModelAPI.DiagnosticLog.LogError(
                    "[ULTRADROID Models] Manual JSON parsing failed\n" + ex);
                throw;
            }

            if (root == null)
                throw new InvalidDataException(
                    "Manual JSON parser returned null.");

            if (binBytes == null)
                binBytes = new byte[0];

            if (root.asset == null || root.asset.version != "2.0")
                throw new InvalidDataException(
                    "Missing or unsupported glTF asset version.");

            return new GlbDocument
            {
                json = root,
                binary = binBytes
            };
        }

        internal byte[] ReadBufferView(int index)
        {
            GltfBufferView view = GetView(index);

            byte[] result = new byte[view.byteLength];

            Buffer.BlockCopy(
                binary, view.byteOffset, result, 0, view.byteLength);

            return result;
        }

        internal GlbAnimationData[] ReadAnimations()
        {
            if (json.animations == null ||
                json.animations.Length == 0)
                return new GlbAnimationData[0];

            List<GlbAnimationData> result =
                new List<GlbAnimationData>();

            foreach (GltfAnimation animation in json.animations)
            {
                if (animation == null)
                    continue;

                if (animation.channels == null ||
                    animation.samplers == null)
                {
                    throw new InvalidDataException(
                        "Animation is missing channels or samplers.");
                }

                List<GlbAnimationTrack> tracks =
                    new List<GlbAnimationTrack>();

                float duration = 0f;

                foreach (GltfAnimationChannel channel in animation.channels)
                {
                    if (channel == null || channel.target == null)
                        throw new InvalidDataException(
                            "Animation channel or target is null.");

                    if (channel.sampler < 0 ||
                        channel.sampler >= animation.samplers.Length)
                    {
                        throw new InvalidDataException(
                            "Animation sampler index is invalid.");
                    }

                    GltfAnimationSampler sampler =
                        animation.samplers[channel.sampler];

                    if (sampler == null)
                        throw new InvalidDataException(
                            "Animation sampler is null.");

                    string path = channel.target.path;

                    if (path != "translation" &&
                        path != "rotation" &&
                        path != "scale" &&
                        path != "weights")
                    {
                        throw new InvalidDataException(
                            "Unsupported animation path: " + path);
                    }

                    GltfAccessor input = GetAccessor(sampler.input);

                    if (input.componentType != 5126 ||
                        input.type != "SCALAR")
                    {
                        throw new InvalidDataException(
                            "Animation timestamps must use FLOAT SCALAR.");
                    }

                    float[] times = ReadFloats(sampler.input, 1);

                    if (times.Length == 0)
                        throw new InvalidDataException(
                            "Animation has no timestamps.");

                    for (int i = 0; i < times.Length; i++)
                    {
                        if (float.IsNaN(times[i]) ||
                            float.IsInfinity(times[i]) ||
                            times[i] < 0f)
                        {
                            throw new InvalidDataException(
                                "Animation contains an invalid timestamp.");
                        }

                        if (i > 0 && times[i] < times[i - 1])
                            throw new InvalidDataException(
                                "Animation timestamps are not ordered.");

                        if (times[i] > duration)
                            duration = times[i];
                    }

                    GltfAccessor output = GetAccessor(sampler.output);

                    if (output.componentType != 5126)
                        throw new InvalidDataException(
                            "Animation output must use FLOAT values.");

                    int components = ComponentCount(output.type);

                    float[] values =
                        ReadFloats(sampler.output, components);

                    string interpolation =
                        string.IsNullOrEmpty(sampler.interpolation)
                            ? "LINEAR"
                            : sampler.interpolation;

                    if (interpolation != "LINEAR" &&
                        interpolation != "STEP" &&
                        interpolation != "CUBICSPLINE")
                    {
                        throw new InvalidDataException(
                            "Unsupported interpolation: " + interpolation);
                    }

                    int multiplier =
                        interpolation == "CUBICSPLINE" ? 3 : 1;

                    int expectedElements =
                        checked(times.Length * multiplier);

                    if (output.count % expectedElements != 0)
                    {
                        throw new InvalidDataException(
                            "Animation output count does not match timestamps.");
                    }

                    int valuesPerKeyframe =
                        output.count / expectedElements * components;

                    if ((path == "translation" || path == "scale") &&
                        (components != 3 || valuesPerKeyframe != 3))
                    {
                        throw new InvalidDataException(
                            path + " animation must use VEC3 values.");
                    }

                    if (path == "rotation" &&
                        (components != 4 || valuesPerKeyframe != 4))
                    {
                        throw new InvalidDataException(
                            "Rotation animation must use VEC4 values.");
                    }

                    tracks.Add(new GlbAnimationTrack
                    {
                        TargetNode = channel.target.node,
                        Path = path,
                        Interpolation = interpolation,
                        Times = times,
                        Values = values,
                        ComponentsPerElement = components,
                        ValuesPerKeyframe = valuesPerKeyframe
                    });
                }

                result.Add(new GlbAnimationData
                {
                    Name = string.IsNullOrEmpty(animation.name)
                        ? "Animation_" + result.Count
                        : animation.name,
                    Duration = duration,
                    Tracks = tracks.ToArray()
                });
            }

            return result.ToArray();
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

        internal Matrix4x4[] ReadMatrix4x4(int accessorIndex)
        {
            GltfAccessor accessor = GetAccessor(accessorIndex);

            if (accessor.componentType != 5126 ||
                accessor.type != "MAT4")
            {
                throw new InvalidDataException(
                    "Inverse bind matrices must use FLOAT MAT4.");
            }

            float[] values = ReadFloatsRaw(accessorIndex, 16);
            Matrix4x4[] result =
                new Matrix4x4[accessor.count];

            for (int i = 0; i < accessor.count; i++)
            {
                Matrix4x4 matrix = new Matrix4x4();

                for (int row = 0; row < 4; row++)
                {
                    for (int column = 0; column < 4; column++)
                    {
                        matrix[row, column] =
                            values[i * 16 + column * 4 + row];
                    }
                }

                result[i] = matrix;
            }

            return result;
        }

        internal float[] ReadFloats(int accessorIndex, int components)
        {
            GltfAccessor accessor = GetAccessor(accessorIndex);

            if (accessor.componentType != 5126)
                throw new InvalidDataException(
                    "Expected FLOAT accessor.");

            if (ComponentCount(accessor.type) != components)
                throw new InvalidDataException(
                    "Unexpected accessor type.");

            return ReadFloatsRaw(accessorIndex, components);
        }

        private float[] ReadFloatsRaw(
            int accessorIndex,
            int components)
        {
            GltfAccessor accessor = GetAccessor(accessorIndex);
            GltfBufferView view = GetView(accessor.bufferView);

            int elementSize = checked(components * 4);

            int stride = view.byteStride > 0
                ? view.byteStride
                : elementSize;

            if (stride < elementSize)
                throw new InvalidDataException(
                    "Accessor byte stride is too small.");

            int start = checked(view.byteOffset + accessor.byteOffset);

            float[] result =
                new float[checked(accessor.count * components)];

            for (int i = 0; i < accessor.count; i++)
            {
                for (int c = 0; c < components; c++)
                {
                    int offset =
                        checked(start + i * stride + c * 4);

                    EnsureBinaryRange(offset, 4);

                    result[i * components + c] =
                        BitConverter.ToSingle(binary, offset);
                }
            }

            return result;
        }

        internal int[] ReadJointIndices(int accessorIndex)
        {
            GltfAccessor accessor = GetAccessor(accessorIndex);

            if (accessor.type != "VEC4")
                throw new InvalidDataException(
                    "JOINTS_0 must use VEC4.");

            if (accessor.componentType != 5121 &&
                accessor.componentType != 5123)
            {
                throw new InvalidDataException(
                    "JOINTS_0 must use UNSIGNED_BYTE or UNSIGNED_SHORT.");
            }

            GltfBufferView view = GetView(accessor.bufferView);

            int componentSize =
                accessor.componentType == 5121 ? 1 : 2;

            int elementSize = componentSize * 4;

            int stride = view.byteStride > 0
                ? view.byteStride
                : elementSize;

            if (stride < elementSize)
                throw new InvalidDataException(
                    "JOINTS_0 byte stride is too small.");

            int start = checked(view.byteOffset + accessor.byteOffset);
            int[] result = new int[checked(accessor.count * 4)];

            for (int i = 0; i < accessor.count; i++)
            {
                for (int c = 0; c < 4; c++)
                {
                    int offset =
                        checked(start + i * stride + c * componentSize);

                    EnsureBinaryRange(offset, componentSize);

                    int value = componentSize == 1
                        ? binary[offset]
                        : BitConverter.ToUInt16(binary, offset);

                    result[i * 4 + c] = value;
                }
            }

            return result;
        }

        internal float[] ReadWeights(int accessorIndex)
        {
            GltfAccessor accessor = GetAccessor(accessorIndex);

            if (accessor.type != "VEC4")
                throw new InvalidDataException(
                    "WEIGHTS_0 must use VEC4.");

            GltfBufferView view = GetView(accessor.bufferView);

            int componentSize =
                accessor.componentType == 5126 ? 4 :
                accessor.componentType == 5121 ? 1 :
                accessor.componentType == 5123 ? 2 : 0;

            if (componentSize == 0)
                throw new InvalidDataException(
                    "Unsupported WEIGHTS_0 component type.");

            if (accessor.componentType != 5126 &&
                !accessor.normalized)
            {
                throw new InvalidDataException(
                    "Integer WEIGHTS_0 accessors must be normalized.");
            }

            int elementSize = componentSize * 4;

            int stride = view.byteStride > 0
                ? view.byteStride
                : elementSize;

            if (stride < elementSize)
                throw new InvalidDataException(
                    "WEIGHTS_0 byte stride is too small.");

            int start = checked(view.byteOffset + accessor.byteOffset);

            float[] result = new float[checked(accessor.count * 4)];

            for (int i = 0; i < accessor.count; i++)
            {
                for (int c = 0; c < 4; c++)
                {
                    int offset =
                        checked(start + i * stride + c * componentSize);

                    EnsureBinaryRange(offset, componentSize);

                    float value;

                    if (accessor.componentType == 5126)
                    {
                        value = BitConverter.ToSingle(binary, offset);
                    }
                    else if (accessor.componentType == 5121)
                    {
                        value = binary[offset] / 255f;
                    }
                    else
                    {
                        value =
                            BitConverter.ToUInt16(binary, offset) / 65535f;
                    }

                    if (float.IsNaN(value) || float.IsInfinity(value) ||
                        value < 0f)
                    {
                        throw new InvalidDataException(
                            "WEIGHTS_0 contains an invalid weight.");
                    }

                    result[i * 4 + c] = value;
                }
            }

            return result;
        }

        internal int[] ReadIndices(int accessorIndex)
        {
            GltfAccessor accessor = GetAccessor(accessorIndex);

            if (accessor.type != "SCALAR")
                throw new InvalidDataException(
                    "Index accessor must be SCALAR.");

            if (accessor.componentType == 5126)
                throw new InvalidDataException(
                    "Index accessor cannot use FLOAT.");

            GltfBufferView view = GetView(accessor.bufferView);

            int elementSize =
                accessor.componentType == 5121 ? 1 :
                accessor.componentType == 5123 ? 2 :
                accessor.componentType == 5125 ? 4 : 0;

            if (elementSize == 0)
                throw new InvalidDataException(
                    "Unsupported index component type.");

            int stride = view.byteStride > 0
                ? view.byteStride
                : elementSize;

            if (stride < elementSize)
                throw new InvalidDataException(
                    "Index byte stride is too small.");

            int start = checked(view.byteOffset + accessor.byteOffset);
            int[] result = new int[accessor.count];

            for (int i = 0; i < accessor.count; i++)
            {
                int offset = checked(start + i * stride);
                EnsureBinaryRange(offset, elementSize);

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
                        throw new InvalidDataException(
                            "Index is too large.");

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
                throw new InvalidDataException(
                    "Accessor has a negative count or offset.");

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
                throw new InvalidDataException(
                    "Only the embedded GLB binary buffer is supported.");

            return view;
        }

        private void EnsureBinaryRange(int offset, int size)
        {
            if (offset < 0 || size < 0 ||
                offset > binary.Length ||
                size > binary.Length - offset)
            {
                throw new InvalidDataException(
                    "Accessor exceeds binary data.");
            }
        }

        private static int ComponentCount(string type)
        {
            if (type == "SCALAR") return 1;
            if (type == "VEC2") return 2;
            if (type == "VEC3") return 3;
            if (type == "VEC4") return 4;
            if (type == "MAT4") return 16;

            throw new InvalidDataException(
                "Unsupported accessor type: " + type);
        }

        private static uint ReadU32(byte[] bytes, int offset)
        {
            return BitConverter.ToUInt32(bytes, offset);
        }
    }

    // ------------------------------------------------------------
    // GLTF DATA CLASSES
    // ------------------------------------------------------------

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
        public GltfAnimation[] animations;
        public GltfSkin[] skins;
    }

    [Serializable]
    internal sealed class GltfSkin
    {
        public string name;
        public int[] joints;
        public int skeleton = -1;
        public int inverseBindMatrices = -1;
    }

    [Serializable]
    internal class GltfAnimation
    {
        public string name;
        public GltfAnimationSampler[] samplers;
        public GltfAnimationChannel[] channels;
    }

    [Serializable]
    internal class GltfAnimationSampler
    {
        public int input = -1;
        public int output = -1;
        public string interpolation = "LINEAR";
    }

    [Serializable]
    internal class GltfAnimationChannel
    {
        public int sampler = -1;
        public GltfAnimationTarget target;
    }

    [Serializable]
    internal class GltfAnimationTarget
    {
        public int node = -1;
        public string path;
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
        public int skin = -1;
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

        public int JOINTS_0 = -1;
        public int WEIGHTS_0 = -1;
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
