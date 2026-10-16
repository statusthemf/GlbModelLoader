// ULTRADROID Models API - GLB 2.0 loader
// Target: Unity / BepInEx IL2CPP
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

        public static GameObject LoadGLB(
            string path,
            Transform parent = null,
            ModelLoadOptions options = null)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentException("A GLB path is required.", "path");

            if (!File.Exists(path))
                throw new FileNotFoundException("GLB file was not found.", path);

            options = options ?? new ModelLoadOptions();

            GlbDocument doc = GlbDocument.Parse(File.ReadAllBytes(path));

            GameObject root = new GameObject(
                string.IsNullOrEmpty(options.Name)
                    ? Path.GetFileNameWithoutExtension(path)
                    : options.Name);

            if (parent != null)
                root.transform.SetParent(parent, false);

            root.transform.localPosition = options.Position;
            root.transform.localRotation = Quaternion.Euler(options.RotationEuler);
            root.transform.localScale = options.Scale;

            try
            {
                Texture2D[] textures = BuildTextures(doc, options.LoadTextures);
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
            GlbDocument doc,
            bool load)
        {
            int count = doc.json.images == null
                ? 0
                : doc.json.images.Length;

            Texture2D[] result = new Texture2D[count];

            if (!load || count == 0)
                return result;

            for (int i = 0; i < count; i++)
            {
                GltfImage image = doc.json.images[i];

                if (image == null ||
                    image.bufferView < 0 ||
                    doc.json.bufferViews == null ||
                    image.bufferView >= doc.json.bufferViews.Length)
                {
                    continue;
                }

                byte[] bytes = doc.ReadBufferView(image.bufferView);

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
            GlbDocument doc,
            Texture2D[] textures)
        {
            int count = doc.json.materials == null
                ? 0
                : doc.json.materials.Length;

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
                        int textureIndex = GetImageIndex(
                            doc,
                            pbr.baseColorTexture.index);

                        if (textureIndex >= 0 &&
                            textureIndex < textures.Length &&
                            textures[textureIndex] != null &&
                            material.HasProperty("_MainTex"))
                        {
                            material.mainTexture = textures[textureIndex];
                        }
                    }

                    if (material.HasProperty("_Metallic"))
                        material.SetFloat(
                            "_Metallic",
                            pbr.metallicFactor);

                    if (material.HasProperty("_Glossiness"))
                        material.SetFloat(
                            "_Glossiness",
                            1f - pbr.roughnessFactor);
                }

                if (source.emissiveFactor != null &&
                    source.emissiveFactor.Length >= 3 &&
                    material.HasProperty("_EmissionColor"))
                {
                    Color emission = new Color(
                        source.emissiveFactor[0],
                        source.emissiveFactor[1],
                        source.emissiveFactor[2]);

                    material.EnableKeyword("_EMISSION");
                    material.SetColor("_EmissionColor", emission);
                }

                result[i] = material;
            }

            result[count] = new Material(shader)
            {
                name = "GLTF_Default_Material"
            };

            return result;
        }

        private static int GetImageIndex(
            GlbDocument doc,
            int textureIndex)
        {
            if (doc.json.textures == null ||
                textureIndex < 0 ||
                textureIndex >= doc.json.textures.Length)
            {
                return -1;
            }

            GltfTexture texture = doc.json.textures[textureIndex];

            return texture == null ? -1 : texture.source;
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

                GltfScene scene = doc.json.scenes[sceneIndex];

                if (scene != null)
                    roots = scene.nodes;
            }

            if (roots == null || roots.Length == 0)
            {
                HashSet<int> childIndices = new HashSet<int>();

                foreach (GltfNode node in doc.json.nodes)
                {
                    if (node == null || node.children == null)
                        continue;

                    foreach (int child in node.children)
                        childIndices.Add(child);
                }

                List<int> inferredRoots = new List<int>();

                for (int i = 0; i < doc.json.nodes.Length; i++)
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
            {
                return;
            }

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

                // glTF matrices are column-major.
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
            {
                return;
            }

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
                {
                    continue;
                }

                if (primitive.mode != 4)
                {
                    throw new InvalidDataException(
                        "Only TRIANGLES primitives are currently supported.");
                }

                Vector3[] primitiveVertices =
                    doc.ReadVector3(primitive.attributes.POSITION);

                Vector3[] primitiveNormals =
                    primitive.attributes.NORMAL >= 0
                        ? doc.ReadVector3(primitive.attributes.NORMAL)
                        : null;

                Vector2[] primitiveUv =
                    primitive.attributes.TEXCOORD_0 >= 0
                        ? doc.ReadVector2(primitive.attributes.TEXCOORD_0)
                        : null;

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

                for (int j = 0; j + 2 < indices.Length; j += 3)
                {
                    int a = indices[j];
                    int b = indices[j + 1];
                    int c = indices[j + 2];

                    if (a < 0 || a >= primitiveVertices.Length ||
                        b < 0 || b >= primitiveVertices.Length ||
                        c < 0 || c >= primitiveVertices.Length)
                    {
                        throw new InvalidDataException(
                            "Mesh index is outside the vertex array.");
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

            // Convert managed collections to IL2CPP collections.
            Il2CppSystem.Collections.Generic.List<Vector3>
                il2CppVertices =
                    new Il2CppSystem.Collections.Generic.List<Vector3>();

            foreach (Vector3 vertex in vertices)
                il2CppVertices.Add(vertex);

            mesh.SetVertices(il2CppVertices);

            mesh.subMeshCount = submeshTriangles.Count;

            for (int i = 0; i < submeshTriangles.Count; i++)
            {
                Il2CppStructArray<int> il2CppTriangles =
                    new Il2CppStructArray<int>(
                        submeshTriangles[i].ToArray());

                mesh.SetTriangles(il2CppTriangles, i, false);
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
                mesh.RecalculateNormals();
            }

            if (hasUv && uv.Count == vertices.Count)
            {
                // This Unity IL2CPP binding exposes SetUVs using Vector4.
                Il2CppSystem.Collections.Generic.List<Vector4>
                    il2CppUv =
                        new Il2CppSystem.Collections.Generic.List<Vector4>();

                foreach (Vector2 coordinate in uv)
                {
                    il2CppUv.Add(
                        new Vector4(
                            coordinate.x,
                            coordinate.y,
                            0f,
                            0f));
                }

                mesh.SetUVs(0, il2CppUv);
            }

            mesh.RecalculateBounds();

            MeshFilter filter = gameObject.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;

            MeshRenderer renderer =
                gameObject.AddComponent<MeshRenderer>();

            renderer.sharedMaterials =
                new Il2CppReferenceArray<Material>(
                    submeshMaterials.ToArray());

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

            int position = 12;
            int fileLength = (int)declaredLength;

            while (position + 8 <= fileLength)
            {
                uint chunkLength = ReadU32(file, position);
                uint chunkType = ReadU32(file, position + 4);

                position += 8;

                if (chunkLength > fileLength - position)
                {
                    throw new InvalidDataException(
                        "GLB chunk exceeds file length.");
                }

                byte[] chunk = new byte[(int)chunkLength];

                Buffer.BlockCopy(
                    file,
                    position,
                    chunk,
                    0,
                    (int)chunkLength);

                position += (int)chunkLength;

                if (chunkType == 0x4E4F534A)
                    jsonBytes = chunk;
                else if (chunkType == 0x004E4942)
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

            if (root == null)
                throw new InvalidDataException(
                    "GLB JSON deserialization returned null.");

            if (root.asset == null ||
                string.IsNullOrEmpty(root.asset.version))
            {
                throw new InvalidDataException(
                    "GLB JSON is missing asset.version.");
            }

            if (!root.asset.version.StartsWith("2"))
                throw new InvalidDataException(
                    "Only glTF 2.x files are supported.");

            if (root.accessors == null)
                throw new InvalidDataException(
                    "GLB JSON accessors were not deserialized.");

            if (root.bufferViews == null)
                throw new InvalidDataException(
                    "GLB JSON bufferViews were not deserialized.");

            if (root.nodes == null)
                throw new InvalidDataException(
                    "GLB JSON nodes were not deserialized.");

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

            Vector3[] result =
                new Vector3[values.Length / 3];

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

            Vector2[] result =
                new Vector2[values.Length / 2];

            for (int i = 0; i < result.Length; i++)
            {
                result[i] = new Vector2(
                    values[i * 2],
                    values[i * 2 + 1]);
            }

            return result;
        }

        internal float[] ReadFloats(
            int accessorIndex,
            int components)
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
                throw new InvalidDataException(
                    "Accessor byte stride is too small.");

            int start = checked(view.byteOffset + accessor.byteOffset);

            float[] result =
                new float[checked(accessor.count * components)];

            for (int i = 0; i < accessor.count; i++)
            {
                for (int c = 0; c < components; c++)
                {
                    int offset = checked(start + i * stride + c * 4);

                    if (offset < 0 ||
                        offset + 4 > binary.Length)
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

                if (offset < 0 ||
                    offset + elementSize > binary.Length)
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
                    result[i] =
                        BitConverter.ToUInt16(binary, offset);
                }
                else
                {
                    uint value =
                        BitConverter.ToUInt32(binary, offset);

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
                    "Accessor contains a negative count or offset.");

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
                    "Buffer view is outside the binary chunk.");
            }

            if (view.buffer != 0)
                throw new InvalidDataException(
                    "Only the embedded GLB binary buffer is supported.");

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
