// ULTRADROID Models API - GLB 2.0 loader prototype
// Target: Unity / BepInEx IL2CPP
// File: src/GlbModelLoader.cs

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
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
        public const string Version = "0.1.0-prototype";

        /// <summary>
        /// Loads a binary glTF 2.0 (.glb) file into the active Unity scene.
        /// </summary>
        public static GameObject LoadGLB(
            string path,
            Transform parent = null,
            ModelLoadOptions options = null)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentException(
                    "A GLB path is required.", nameof(path));

            if (!File.Exists(path))
                throw new FileNotFoundException(
                    "GLB file was not found.", path);

            options = options ?? new ModelLoadOptions();

            byte[] file = File.ReadAllBytes(path);
            GlbDocument doc = GlbDocument.Parse(file);

            string objectName = string.IsNullOrEmpty(options.Name)
                ? Path.GetFileNameWithoutExtension(path)
                : options.Name;

            GameObject root = new GameObject(objectName);

            if (parent != null)
                root.transform.SetParent(parent, false);

            root.transform.localPosition = options.Position;
            root.transform.localRotation =
                Quaternion.Euler(options.RotationEuler);
            root.transform.localScale = options.Scale;

            try
            {
                Texture2D[] textures =
                    BuildTextures(doc, options.LoadTextures);

                Material[] materials = BuildMaterials(doc, textures);

                BuildScene(
                    doc,
                    root.transform,
                    materials,
                    options.CreateColliders);

                return root;
            }
            catch
            {
                UnityEngine.Object.Destroy(root);
                throw;
            }
        }

        private static Texture2D[] BuildTextures(
            GlbDocument d, bool load)
        {
            int count = d.json.images == null
                ? 0
                : d.json.images.Length;

            Texture2D[] result = new Texture2D[count];

            if (!load || d.json.images == null)
                return result;

            for (int i = 0; i < count; i++)
            {
                GltfImage image = d.json.images[i];

                if (image.bufferView < 0 ||
                    d.json.bufferViews == null ||
                    image.bufferView >= d.json.bufferViews.Length)
                    continue;

                byte[] bytes = d.ReadBufferView(image.bufferView);

                Texture2D texture = new Texture2D(
                    2, 2, TextureFormat.RGBA32, true);

                if (ImageConversion.LoadImage(texture, bytes, false))
                {
                    texture.name = "ULTRADROID_GLTF_Image_" + i;
                    texture.wrapMode = TextureWrapMode.Repeat;
                    texture.filterMode = FilterMode.Bilinear;
                    result[i] = texture;
                }
                else
                {
                    UnityEngine.Object.Destroy(texture);
                }
            }

            return result;
        }

        private static Material[] BuildMaterials(
            GlbDocument d, Texture2D[] textures)
        {
            int count = d.json.materials == null
                ? 0
                : d.json.materials.Length;

            Material[] result = new Material[count + 1];

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
                GltfMaterial source = d.json.materials[i];

                Material mat = new Material(shader);

                mat.name = string.IsNullOrEmpty(source.name)
                    ? "GLTF_Material_" + i
                    : source.name;

                if (source.pbrMetallicRoughness != null)
                {
                    PbrMetallicRoughness pbr =
                        source.pbrMetallicRoughness;

                    if (pbr.baseColorFactor != null &&
                        pbr.baseColorFactor.Length >= 4)
                    {
                        mat.color = new Color(
                            pbr.baseColorFactor[0],
                            pbr.baseColorFactor[1],
                            pbr.baseColorFactor[2],
                            pbr.baseColorFactor[3]);
                    }

                    if (pbr.baseColorTexture != null)
                    {
                        int textureIndex = GetImageIndex(
                            d, pbr.baseColorTexture.index);

                        if (textureIndex >= 0 &&
                            textureIndex < textures.Length &&
                            textures[textureIndex] != null &&
                            mat.HasProperty("_MainTex"))
                        {
                            mat.mainTexture = textures[textureIndex];
                        }
                    }

                    if (mat.HasProperty("_Metallic"))
                        mat.SetFloat("_Metallic", pbr.metallicFactor);

                    if (mat.HasProperty("_Glossiness"))
                    {
                        mat.SetFloat(
                            "_Glossiness",
                            1f - pbr.roughnessFactor);
                    }
                }

                if (source.emissiveFactor != null &&
                    source.emissiveFactor.Length >= 3 &&
                    mat.HasProperty("_EmissionColor"))
                {
                    Color emission = new Color(
                        source.emissiveFactor[0],
                        source.emissiveFactor[1],
                        source.emissiveFactor[2]);

                    mat.EnableKeyword("_EMISSION");
                    mat.SetColor("_EmissionColor", emission);
                }

                result[i] = mat;
            }

            result[count] = new Material(shader)
            {
                name = "GLTF_Default_Material"
            };

            return result;
        }

        private static int GetImageIndex(
            GlbDocument d, int textureIndex)
        {
            if (d.json.textures == null ||
                textureIndex < 0 ||
                textureIndex >= d.json.textures.Length)
                return -1;

            return d.json.textures[textureIndex].source;
        }

        private static void BuildScene(
            GlbDocument d,
            Transform root,
            Material[] materials,
            bool colliders)
        {
            if (d.json.nodes == null || d.json.nodes.Length == 0)
                return;

            int sceneIndex = d.json.scene;
            int[] roots = null;

            if (d.json.scenes != null &&
                d.json.scenes.Length > 0)
            {
                if (sceneIndex < 0 ||
                    sceneIndex >= d.json.scenes.Length)
                    sceneIndex = 0;

                roots = d.json.scenes[sceneIndex].nodes;
            }

            if (roots == null || roots.Length == 0)
            {
                HashSet<int> childIndices = new HashSet<int>();

                foreach (GltfNode node in d.json.nodes)
                {
                    if (node.children == null)
                        continue;

                    foreach (int child in node.children)
                        childIndices.Add(child);
                }

                List<int> inferredRoots = new List<int>();

                for (int i = 0; i < d.json.nodes.Length; i++)
                {
                    if (!childIndices.Contains(i))
                        inferredRoots.Add(i);
                }

                roots = inferredRoots.ToArray();
            }

            HashSet<int> recursionGuard = new HashSet<int>();

            foreach (int nodeIndex in roots)
            {
                BuildNode(
                    d,
                    nodeIndex,
                    root,
                    materials,
                    colliders,
                    recursionGuard);
            }
        }

        private static void BuildNode(
            GlbDocument d,
            int index,
            Transform parent,
            Material[] materials,
            bool colliders,
            HashSet<int> guard)
        {
            if (index < 0 ||
                index >= d.json.nodes.Length ||
                guard.Contains(index))
                return;

            guard.Add(index);

            GltfNode node = d.json.nodes[index];

            GameObject go = new GameObject(
                string.IsNullOrEmpty(node.name)
                    ? "GLTF_Node_" + index
                    : node.name);

            go.transform.SetParent(parent, false);

            ApplyNodeTransform(go.transform, node);

            if (node.mesh >= 0 &&
                d.json.meshes != null &&
                node.mesh < d.json.meshes.Length)
            {
                BuildMesh(
                    d,
                    d.json.meshes[node.mesh],
                    go,
                    materials,
                    colliders);
            }

            if (node.children != null)
            {
                foreach (int child in node.children)
                {
                    BuildNode(
                        d,
                        child,
                        go.transform,
                        materials,
                        colliders,
                        guard);
                }
            }

            guard.Remove(index);
        }

        private static void ApplyNodeTransform(
            Transform t, GltfNode n)
        {
            if (n.matrix != null && n.matrix.Length == 16)
            {
                Matrix4x4 m = new Matrix4x4();

                // glTF matrices are column-major.
                for (int r = 0; r < 4; r++)
                {
                    for (int c = 0; c < 4; c++)
                        m[r, c] = n.matrix[c * 4 + r];
                }

                t.localPosition = m.GetColumn(3);
                t.localRotation = m.rotation;
                t.localScale = m.lossyScale;
                return;
            }

            if (n.translation != null &&
                n.translation.Length >= 3)
            {
                t.localPosition = new Vector3(
                    n.translation[0],
                    n.translation[1],
                    n.translation[2]);
            }

            if (n.rotation != null &&
                n.rotation.Length >= 4)
            {
                t.localRotation = new Quaternion(
                    n.rotation[0],
                    n.rotation[1],
                    n.rotation[2],
                    n.rotation[3]);
            }

            if (n.scale != null && n.scale.Length >= 3)
            {
                t.localScale = new Vector3(
                    n.scale[0],
                    n.scale[1],
                    n.scale[2]);
            }
        }

        private static void BuildMesh(
            GlbDocument d,
            GltfMesh source,
            GameObject go,
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
                if (primitive.attributes == null ||
                    primitive.attributes.POSITION < 0)
                    continue;

                Vector3[] pv =
                    d.ReadVector3(primitive.attributes.POSITION);

                Vector3[] pn = primitive.attributes.NORMAL >= 0
                    ? d.ReadVector3(primitive.attributes.NORMAL)
                    : null;

                Vector2[] pu = primitive.attributes.TEXCOORD_0 >= 0
                    ? d.ReadVector2(primitive.attributes.TEXCOORD_0)
                    : null;

                int offset = vertices.Count;

                vertices.AddRange(pv);

                if (pn == null || pn.Length != pv.Length)
                    hasNormals = false;
                else
                    normals.AddRange(pn);

                if (pu == null || pu.Length != pv.Length)
                    hasUv = false;
                else
                    uv.AddRange(pu);

                int[] indices = primitive.indices >= 0
                    ? d.ReadIndices(primitive.indices)
                    : MakeSequential(pv.Length);

                List<int> triangles = new List<int>(indices.Length);

                // This prototype supports triangle-list primitives only.
                for (int j = 0; j + 2 < indices.Length; j += 3)
                {
                    if (indices[j] < 0 || indices[j] >= pv.Length ||
                        indices[j + 1] < 0 ||
                        indices[j + 1] >= pv.Length ||
                        indices[j + 2] < 0 ||
                        indices[j + 2] >= pv.Length)
                    {
                        throw new InvalidDataException(
                            "Mesh index is outside the vertex array.");
                    }

                    triangles.Add(offset + indices[j]);
                    triangles.Add(offset + indices[j + 1]);
                    triangles.Add(offset + indices[j + 2]);
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
                ? go.name + "_Mesh"
                : source.name;

            if (vertices.Count > 65535)
                mesh.indexFormat =
                    UnityEngine.Rendering.IndexFormat.UInt32;

            // Convert managed collections to IL2CPP collections.
            var il2CppVertices =
                new Il2CppSystem.Collections.Generic.List<Vector3>();

            foreach (Vector3 vertex in vertices)
                il2CppVertices.Add(vertex);

            mesh.SetVertices(il2CppVertices);

            mesh.subMeshCount = submeshTriangles.Count;

            for (int i = 0; i < submeshTriangles.Count; i++)
            {
                int[] managedTriangles =
                    submeshTriangles[i].ToArray();

                Il2CppStructArray<int> il2CppTriangles =
                    new Il2CppStructArray<int>(managedTriangles);

                mesh.SetTriangles(il2CppTriangles, i, false);
            }

            if (hasNormals && normals.Count == vertices.Count)
            {
                var il2CppNormals =
                    new Il2CppSystem.Collections.Generic.List<Vector3>();

                foreach (Vector3 normal in normals)
                    il2CppNormals.Add(normal);

                mesh.SetNormals(il2CppNormals);
            }
            else
            {
                mesh.RecalculateNormals();
            }

            if (hasUv && uv.Count == vertices.Count)
            {
                // The target bindings report Vector4 for SetUVs.
                var il2CppUv =
                    new Il2CppSystem.Collections.Generic.List<Vector4>();

                foreach (Vector2 texCoord in uv)
                {
                    il2CppUv.Add(new Vector4(
                        texCoord.x,
                        texCoord.y,
                        0f,
                        0f));
                }

                mesh.SetUVs(0, il2CppUv);
            }

            mesh.RecalculateBounds();

            MeshFilter filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;

            MeshRenderer renderer = go.AddComponent<MeshRenderer>();

            // Material arrays may also require IL2CPP conversion,
            // depending on the exact generated Unity bindings.
            renderer.sharedMaterials = submeshMaterials.ToArray();

            if (colliders)
            {
                MeshCollider collider = go.AddComponent<MeshCollider>();
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

        internal static GlbDocument Parse(byte[] file)
        {
            if (file == null || file.Length < 20)
                throw new InvalidDataException(
                    "File is too small to be a GLB.");

            if (ReadU32(file, 0) != 0x46546C67)
                throw new InvalidDataException(
                    "Invalid GLB magic; expected glTF.");

            if (ReadU32(file, 4) != 2)
                throw new InvalidDataException(
                    "Only GLB version 2 is supported.");

            uint declaredLength = ReadU32(file, 8);

            if (declaredLength > file.Length ||
                declaredLength < 20)
            {
                throw new InvalidDataException(
                    "GLB declared length is invalid.");
            }

            byte[] jsonBytes = null;
            byte[] binBytes = null;

            int pos = 12;
            int fileLength = (int)declaredLength;

            while (pos + 8 <= fileLength)
            {
                uint length = ReadU32(file, pos);
                uint type = ReadU32(file, pos + 4);
                pos += 8;

                if (length > fileLength - pos)
                {
                    throw new InvalidDataException(
                        "GLB chunk exceeds file length.");
                }

                byte[] chunk = new byte[(int)length];

                Buffer.BlockCopy(
                    file, pos, chunk, 0, (int)length);

                pos += (int)length;

                if (type == 0x4E4F534A)
                    jsonBytes = chunk;
                else if (type == 0x004E4942)
                    binBytes = chunk;
            }

            if (jsonBytes == null)
                throw new InvalidDataException(
                    "GLB JSON chunk is missing.");

            string jsonText = Encoding.UTF8
                .GetString(jsonBytes)
                .TrimEnd('\0', ' ', '\r', '\n', '\t');

            GltfRoot root =
                UnityEngine.JsonUtility.FromJson<GltfRoot>(jsonText);

            if (root == null ||
                root.asset == null ||
                root.asset.version == null ||
                !root.asset.version.StartsWith("2"))
            {
                throw new InvalidDataException(
                    "The file is not glTF 2.x.");
            }

            return new GlbDocument
            {
                json = root,
                binary = binBytes ?? new byte[0]
            };
        }

        internal byte[] ReadBufferView(int index)
        {
            GltfBufferView view = GetView(index);

            byte[] result = new byte[view.byteLength];

            Buffer.BlockCopy(
                binary,
                view.byteOffset,
                result,
                0,
                view.byteLength);

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

        internal float[] ReadFloats(
            int accessorIndex, int components)
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

            int start = view.byteOffset + accessor.byteOffset;

            float[] result =
                new float[accessor.count * components];

            for (int i = 0; i < accessor.count; i++)
            {
                for (int c = 0; c < components; c++)
                {
                    int offset = start + i * stride + c * 4;

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
            GltfBufferView view = GetView(accessor.bufferView);

            int size =
                accessor.componentType == 5121 ? 1 :
                accessor.componentType == 5123 ? 2 :
                accessor.componentType == 5125 ? 4 : 0;

            if (size == 0)
            {
                throw new InvalidDataException(
                    "Unsupported index component type.");
            }

            if (accessor.type != "SCALAR")
            {
                throw new InvalidDataException(
                    "Index accessor must be SCALAR.");
            }

            int stride = view.byteStride > 0
                ? view.byteStride
                : size;

            int start = view.byteOffset + accessor.byteOffset;

            int[] result = new int[accessor.count];

            for (int i = 0; i < accessor.count; i++)
            {
                int p = start + i * stride;

                if (p < 0 || p + size > binary.Length)
                {
                    throw new InvalidDataException(
                        "Index accessor exceeds binary data.");
                }

                if (size == 1)
                {
                    result[i] = binary[p];
                }
                else if (size == 2)
                {
                    result[i] = BitConverter.ToUInt16(binary, p);
                }
                else
                {
                    uint value = BitConverter.ToUInt32(binary, p);

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

            return json.accessors[index];
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

            if (view.byteOffset < 0 ||
                view.byteLength < 0 ||
                view.byteOffset > binary.Length ||
                view.byteLength > binary.Length - view.byteOffset)
            {
                throw new InvalidDataException(
                    "Buffer view is outside the binary chunk.");
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
