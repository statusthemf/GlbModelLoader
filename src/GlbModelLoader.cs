// ULTRADROID Models API - GLB 2.0 loader
// Target: Unity 2021.3 / BepInEx IL2CPP (Android)
// Version with safer Newtonsoft.Json deserialization
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using Newtonsoft.Json;

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
        public const string Version = "0.1.1-safer-json";

        private static void Log(string message)
        {
            Debug.Log("[ULTRADROID Models] " + message);
        }

        private static void LogError(string message, Exception ex)
        {
            Debug.LogError(
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

            Log("CHECKPOINT A: GLB parsing begins");

            GlbDocument doc;

            try
            {
                doc = GlbDocument.Parse(fileBytes);
            }
            catch (Exception ex)
            {
                LogError("GLB parsing threw an exception", ex);
                throw;
            }

            Log("CHECKPOINT B: GLB parsing complete");

            Log("Document counts: nodes=" +
                Count(doc.json.nodes) +
                ", meshes=" + Count(doc.json.meshes) +
                ", accessors=" + Count(doc.json.accessors) +
                ", bufferViews=" + Count(doc.json.bufferViews) +
                ", materials=" + Count(doc.json.materials) +
                ", textures=" + Count(doc.json.textures) +
                ", images=" + Count(doc.json.images) +
                ", binaryBytes=" + doc.binary.Length);

            Log("CHECKPOINT C: creating root GameObject");

            GameObject root = new GameObject(
                string.IsNullOrEmpty(options.Name)
                    ? Path.GetFileNameWithoutExtension(path)
                    : options.Name);

            try
            {
                Log("Root GameObject created");

                if (parent != null)
                {
                    Log("Setting root parent");
                    root.transform.SetParent(parent, false);
                }

                root.transform.localPosition = options.Position;
                root.transform.localRotation =
                    Quaternion.Euler(options.RotationEuler);
                root.transform.localScale = options.Scale;

                Log("Root transform configured");

                Log("CHECKPOINT D: BuildTextures begins. Enabled=" +
                    options.LoadTextures);

                Texture2D[] textures =
                    BuildTextures(doc, options.LoadTextures);

                Log("CHECKPOINT E: BuildTextures complete. Count=" +
                    textures.Length);

                Log("CHECKPOINT F: BuildMaterials begins");

                Material[] materials = BuildMaterials(doc, textures);

                Log("CHECKPOINT G: BuildMaterials complete. Count=" +
                    materials.Length);

                Log("CHECKPOINT H: BuildScene begins");

                BuildScene(
                    doc,
                    root.transform,
                    materials,
                    options.CreateColliders);

                Log("CHECKPOINT I: BuildScene complete");
                Log("LoadGLB SUCCESS");

                return root;
            }
            catch (Exception ex)
            {
                LogError("LoadGLB caught a managed exception", ex);

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

            Log("BuildTextures ENTER. Images=" + count +
                ", enabled=" + load);

            if (!load || count == 0)
            {
                Log("BuildTextures skipped");
                return result;
            }

            for (int i = 0; i < count; i++)
            {
                Log("BuildTextures: image " + i);

                GltfImage image = doc.json.images[i];

                if (image == null ||
                    image.bufferView < 0 ||
                    doc.json.bufferViews == null ||
                    image.bufferView >= doc.json.bufferViews.Length)
                {
                    Log("BuildTextures: skipping invalid image " + i);
                    continue;
                }

                byte[] bytes = doc.ReadBufferView(image.bufferView);

                Log("BuildTextures: image bytes=" + bytes.Length);

                Texture2D texture = new Texture2D(
                    2, 2, TextureFormat.RGBA32, true);

                Log("BuildTextures: Texture2D created for image " + i);

                bool loaded = ImageConversion.LoadImage(
                    texture, bytes, false);

                Log("BuildTextures: LoadImage returned " + loaded);

                if (loaded)
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

            Log("BuildTextures EXIT");
            return result;
        }

        private static Material[] BuildMaterials(
            GlbDocument doc,
            Texture2D[] textures)
        {
            int count = Count(doc.json.materials);
            Material[] result = new Material[count + 1];

            Log("BuildMaterials ENTER. Count=" + count);

            Log("BuildMaterials: finding Standard shader");

            Shader shader = Shader.Find("Standard");

            if (shader == null)
            {
                Log("BuildMaterials: Standard not found; trying URP/Lit");
                shader = Shader.Find("Universal Render Pipeline/Lit");
            }

            if (shader == null)
            {
                Log("BuildMaterials: URP/Lit not found; trying Sprites/Default");
                shader = Shader.Find("Sprites/Default");
            }

            if (shader == null)
                throw new InvalidOperationException(
                    "No compatible Unity shader was found.");

            Log("BuildMaterials: shader found: " + shader.name);

            for (int i = 0; i < count; i++)
            {
                Log("BuildMaterials: material " + i + " begins");

                GltfMaterial source = doc.json.materials[i];

                if (source == null)
                {
                    Log("BuildMaterials: null material " + i);
                    continue;
                }

                Material material = new Material(shader);

                Log("BuildMaterials: Material object created " + i);

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
                            doc, pbr.baseColorTexture.index);

                        if (textureIndex >= 0 &&
                            textureIndex < textures.Length &&
                            textures[textureIndex] != null &&
                            material.HasProperty("_MainTex"))
                        {
                            material.mainTexture =
                                textures[textureIndex];
                        }
                    }

                    if (material.HasProperty("_Metallic"))
                        material.SetFloat(
                            "_Metallic", pbr.metallicFactor);

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

                Log("BuildMaterials: material " + i + " complete");
            }

            Log("BuildMaterials: creating default material");

            result[count] = new Material(shader)
            {
                name = "GLTF_Default_Material"
            };

            Log("BuildMaterials EXIT");
            return result;
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

        private static void BuildScene(
            GlbDocument doc,
            Transform root,
            Material[] materials,
            bool colliders)
        {
            Log("BuildScene ENTER");

            if (doc.json.nodes == null ||
                doc.json.nodes.Length == 0)
            {
                Log("BuildScene: no nodes");
                return;
            }

            int sceneIndex = doc.json.scene;
            int[] roots = null;

            Log("BuildScene: node count=" + doc.json.nodes.Length);

            if (doc.json.scenes != null &&
                doc.json.scenes.Length > 0)
            {
                if (sceneIndex < 0 ||
                    sceneIndex >= doc.json.scenes.Length)
                    sceneIndex = 0;

                GltfScene scene = doc.json.scenes[sceneIndex];

                if (scene != null)
                    roots = scene.nodes;
            }

            if (roots == null || roots.Length == 0)
            {
                Log("BuildScene: inferring root nodes");

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

            Log("BuildScene: root count=" + roots.Length);

            HashSet<int> recursionGuard = new HashSet<int>();

            for (int i = 0; i < roots.Length; i++)
            {
                Log("BuildScene: root " + i +
                    ", node index=" + roots[i]);

                BuildNode(
                    doc,
                    roots[i],
                    root,
                    materials,
                    colliders,
                    recursionGuard);

                Log("BuildScene: root " + i + " complete");
            }

            Log("BuildScene EXIT");
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
                Log("BuildNode: skipping invalid/repeated node " + index);
                return;
            }

            GltfNode node = doc.json.nodes[index];

            if (node == null)
            {
                Log("BuildNode: null node " + index);
                return;
            }

            string nodeName = string.IsNullOrEmpty(node.name)
                ? "GLTF_Node_" + index
                : node.name;

            Log("BuildNode ENTER: index=" + index +
                ", name=" + nodeName + ", mesh=" + node.mesh);

            guard.Add(index);

            GameObject gameObject = new GameObject(nodeName);

            Log("BuildNode: GameObject created");

            gameObject.transform.SetParent(parent, false);

            Log("BuildNode: parent set");

            ApplyNodeTransform(gameObject.transform, node);

            Log("BuildNode: transform applied");

            if (node.mesh >= 0 &&
                doc.json.meshes != null &&
                node.mesh < doc.json.meshes.Length)
            {
                GltfMesh mesh = doc.json.meshes[node.mesh];

                if (mesh != null)
                {
                    Log("BuildNode: calling BuildMesh");

                    BuildMesh(
                        doc,
                        mesh,
                        gameObject,
                        materials,
                        colliders);

                    Log("BuildNode: BuildMesh returned");
                }
            }

            if (node.children != null)
            {
                for (int i = 0; i < node.children.Length; i++)
                {
                    Log("BuildNode: child " + i +
                        ", node index=" + node.children[i]);

                    BuildNode(
                        doc,
                        node.children[i],
                        gameObject.transform,
                        materials,
                        colliders,
                        guard);
                }
            }

            guard.Remove(index);

            Log("BuildNode EXIT: " + nodeName);
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
            string meshLabel = string.IsNullOrEmpty(source.name)
                ? gameObject.name
                : source.name;

            Log("BuildMesh ENTER: " + meshLabel);

            if (source.primitives == null ||
                source.primitives.Length == 0)
            {
                Log("BuildMesh: no primitives");
                return;
            }

            Log("BuildMesh: primitive count=" +
                source.primitives.Length);

            List<Vector3> vertices = new List<Vector3>();
            List<Vector3> normals = new List<Vector3>();
            List<Vector2> uv = new List<Vector2>();

            List<List<int>> submeshTriangles =
                new List<List<int>>();

            List<Material> submeshMaterials =
                new List<Material>();

            bool hasNormals = true;
            bool hasUv = true;

            for (int p = 0; p < source.primitives.Length; p++)
            {
                GltfPrimitive primitive = source.primitives[p];

                Log("BuildMesh: primitive " + p + " ENTER");

                if (primitive == null ||
                    primitive.attributes == null ||
                    primitive.attributes.POSITION < 0)
                {
                    Log("BuildMesh: skipping primitive; POSITION missing");
                    continue;
                }

                if (primitive.mode != 4)
                {
                    throw new InvalidDataException(
                        "Only TRIANGLES primitives are currently supported.");
                }

                Log("BuildMesh: reading POSITION accessor " +
                    primitive.attributes.POSITION);

                Vector3[] primitiveVertices =
                    doc.ReadVector3(primitive.attributes.POSITION);

                Log("BuildMesh: POSITION read; count=" +
                    primitiveVertices.Length);

                Vector3[] primitiveNormals = null;

                if (primitive.attributes.NORMAL >= 0)
                {
                    Log("BuildMesh: reading NORMAL accessor");

                    primitiveNormals =
                        doc.ReadVector3(primitive.attributes.NORMAL);

                    Log("BuildMesh: NORMAL read; count=" +
                        primitiveNormals.Length);
                }

                Vector2[] primitiveUv = null;

                if (primitive.attributes.TEXCOORD_0 >= 0)
                {
                    Log("BuildMesh: reading TEXCOORD_0 accessor");

                    primitiveUv =
                        doc.ReadVector2(primitive.attributes.TEXCOORD_0);

                    Log("BuildMesh: TEXCOORD_0 read; count=" +
                        primitiveUv.Length);
                }

                int vertexOffset = vertices.Count;

                vertices.AddRange(primitiveVertices);

                Log("BuildMesh: vertices merged; total=" +
                    vertices.Count);

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

                int[] indices;

                if (primitive.indices >= 0)
                {
                    Log("BuildMesh: reading index accessor " +
                        primitive.indices);

                    indices = doc.ReadIndices(primitive.indices);
                }
                else
                {
                    Log("BuildMesh: generating sequential indices");

                    indices = MakeSequential(
                        primitiveVertices.Length);
                }

                Log("BuildMesh: indices read; count=" + indices.Length);

                List<int> triangles =
                    new List<int>(indices.Length);

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

                Log("BuildMesh: triangle list built; count=" +
                    triangles.Count);

                submeshTriangles.Add(triangles);

                int materialIndex =
                    primitive.material >= 0 &&
                    primitive.material < materials.Length - 1
                        ? primitive.material
                        : materials.Length - 1;

                submeshMaterials.Add(materials[materialIndex]);

                Log("BuildMesh: primitive " + p + " complete");
            }

            Log("BuildMesh: combined vertices=" + vertices.Count +
                ", submeshes=" + submeshTriangles.Count);

            if (vertices.Count == 0)
            {
                Log("BuildMesh: no valid vertices; returning");
                return;
            }

            Log("BuildMesh: creating Unity Mesh");

            Mesh mesh = new Mesh();

            Log("BuildMesh: Unity Mesh created");

            mesh.name = string.IsNullOrEmpty(source.name)
                ? gameObject.name + "_Mesh"
                : source.name;

            if (vertices.Count > 65535)
            {
                Log("BuildMesh: setting UInt32 index format");

                mesh.indexFormat =
                    UnityEngine.Rendering.IndexFormat.UInt32;

                Log("BuildMesh: UInt32 index format set");
            }

            Log("BuildMesh: creating IL2CPP vertex list");

            Il2CppSystem.Collections.Generic.List<Vector3>
                il2CppVertices =
                    new Il2CppSystem.Collections.Generic.List<Vector3>();

            Log("BuildMesh: filling IL2CPP vertex list");

            for (int i = 0; i < vertices.Count; i++)
                il2CppVertices.Add(vertices[i]);

            Log("BuildMesh: vertex list filled; count=" +
                vertices.Count);

            Log("BuildMesh: calling Mesh.SetVertices");

            mesh.SetVertices(il2CppVertices);

            Log("BuildMesh: Mesh.SetVertices returned");

            Log("BuildMesh: setting subMeshCount=" +
                submeshTriangles.Count);

            mesh.subMeshCount = submeshTriangles.Count;

            Log("BuildMesh: subMeshCount set");

            for (int i = 0; i < submeshTriangles.Count; i++)
            {
                int[] triangleArray =
                    submeshTriangles[i].ToArray();

                Log("BuildMesh: submesh " + i +
                    ", index count=" + triangleArray.Length);

                Il2CppStructArray<int> il2CppTriangles =
                    new Il2CppStructArray<int>(triangleArray);

                Log("BuildMesh: triangle array created for submesh " + i);

                mesh.SetTriangles(il2CppTriangles, i, false);

                Log("BuildMesh: SetTriangles returned for submesh " + i);
            }

            if (hasNormals && normals.Count == vertices.Count)
            {
                Log("BuildMesh: creating IL2CPP normals list");

                Il2CppSystem.Collections.Generic.List<Vector3>
                    il2CppNormals =
                        new Il2CppSystem.Collections.Generic.List<Vector3>();

                for (int i = 0; i < normals.Count; i++)
                    il2CppNormals.Add(normals[i]);

                Log("BuildMesh: calling Mesh.SetNormals");

                mesh.SetNormals(il2CppNormals);

                Log("BuildMesh: Mesh.SetNormals returned");
            }
            else
            {
                Log("BuildMesh: calling RecalculateNormals");

                mesh.RecalculateNormals();

                Log("BuildMesh: RecalculateNormals returned");
            }

            if (hasUv && uv.Count == vertices.Count)
            {
                Log("BuildMesh: creating IL2CPP UV list");

                Il2CppSystem.Collections.Generic.List<Vector4>
                    il2CppUv =
                        new Il2CppSystem.Collections.Generic.List<Vector4>();

                for (int i = 0; i < uv.Count; i++)
                {
                    Vector2 coordinate = uv[i];

                    il2CppUv.Add(new Vector4(
                        coordinate.x,
                        coordinate.y,
                        0f,
                        0f));
                }

                Log("BuildMesh: calling Mesh.SetUVs");

                mesh.SetUVs(0, il2CppUv);

                Log("BuildMesh: Mesh.SetUVs returned");
            }
            else
            {
                Log("BuildMesh: UVs missing or mismatched; skipped");
            }

            Log("BuildMesh: calling RecalculateBounds");

            mesh.RecalculateBounds();

            Log("BuildMesh: RecalculateBounds returned");

            Log("BuildMesh: adding MeshFilter");

            MeshFilter filter =
                gameObject.AddComponent<MeshFilter>();

            Log("BuildMesh: assigning sharedMesh");

            filter.sharedMesh = mesh;

            Log("BuildMesh: MeshFilter assigned");

            Log("BuildMesh: adding MeshRenderer");

            MeshRenderer renderer =
                gameObject.AddComponent<MeshRenderer>();

            Log("BuildMesh: creating sharedMaterials array");

            renderer.sharedMaterials =
                new Il2CppReferenceArray<Material>(
                    submeshMaterials.ToArray());

            Log("BuildMesh: sharedMaterials assigned");

            if (colliders)
            {
                Log("BuildMesh: adding MeshCollider");

                MeshCollider collider =
                    gameObject.AddComponent<MeshCollider>();

                Log("BuildMesh: assigning collider mesh");

                collider.sharedMesh = mesh;

                Log("BuildMesh: collider mesh assigned");
            }

            Log("BuildMesh EXIT: " + meshLabel);
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
            Debug.Log(
                "[ULTRADROID Models] Parse ENTER; bytes=" +
                (file == null ? -1 : file.Length));

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
            int chunkNumber = 0;

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

                Debug.Log(
                    "[ULTRADROID Models] Parse: chunk " +
                    chunkNumber + ", type=0x" +
                    chunkType.ToString("X8") +
                    ", bytes=" + chunkLength);

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

                chunkNumber++;
            }

            if (jsonBytes == null)
                throw new InvalidDataException(
                    "GLB JSON chunk is missing.");

            string jsonText = Encoding.UTF8
                .GetString(jsonBytes)
                .TrimEnd('\0', ' ', '\r', '\n', '\t');

            Debug.Log(
                "[ULTRADROID Models] Parse: JSON extracted; chars=" +
                jsonText.Length);

            // ============================================================
            // SAFER NEWTONSOFT DESERIALIZATION
            // ============================================================
            Debug.Log("[ULTRADROID Models] Parse: calling Newtonsoft.Json (safer settings)");

            GltfRoot root;

            try
            {
                var settings = new JsonSerializerSettings
                {
                    // Reduce reflection / type resolution pressure on IL2CPP
                    TypeNameHandling = TypeNameHandling.None,
                    MetadataPropertyHandling = MetadataPropertyHandling.Ignore,
                    MissingMemberHandling = MissingMemberHandling.Ignore,
                    NullValueHandling = NullValueHandling.Ignore,
                    DefaultValueHandling = DefaultValueHandling.Populate,
                    // Avoid some heavy converters
                    FloatParseHandling = FloatParseHandling.Double,
                    DateParseHandling = DateParseHandling.None
                };

                root = JsonConvert.DeserializeObject<GltfRoot>(jsonText, settings);

                Debug.Log("[ULTRADROID Models] Parse: Newtonsoft.Json returned");
            }
            catch (Exception ex)
            {
                Debug.LogError(
                    "[ULTRADROID Models] Newtonsoft JSON parsing failed:\n" + ex);
                throw new InvalidDataException(
                    "Failed to deserialize the GLB JSON chunk.", ex);
            }

            if (root == null)
            {
                throw new InvalidDataException(
                    "GLB JSON deserialization returned null.");
            }
            // ============================================================

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

            Debug.Log(
                "[ULTRADROID Models] Parse: validation complete; " +
                "nodes=" + root.nodes.Length +
                ", meshes=" + (root.meshes == null ? 0 : root.meshes.Length) +
                ", accessors=" + root.accessors.Length +
                ", bufferViews=" + root.bufferViews.Length +
                ", binaryBytes=" + (binBytes == null ? 0 : binBytes.Length));

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
            int accessorIndex,
            int components)
        {
            GltfAccessor accessor = GetAccessor(accessorIndex);

            if (accessor.componentType != 5126)
                throw new InvalidDataException(
                    "Expected FLOAT accessor for vector attributes.");

            if (ComponentCount(accessor.type) != components)
                throw new InvalidDataException(
                    "Unexpected accessor type for vector attribute.");

            GltfBufferView view = GetView(accessor.bufferView);

            int elementSize = components * 4;

            int stride = view.byteStride > 0
                ? view.byteStride
                : elementSize;

            if (stride < elementSize)
                throw new InvalidDataException(
                    "Accessor byte stride is too small.");

            int start = checked(
                view.byteOffset + accessor.byteOffset);

            float[] result = new float[
                checked(accessor.count * components)];

            for (int i = 0; i < accessor.count; i++)
            {
                for (int c = 0; c < components; c++)
                {
                    int offset = checked(
                        start + i * stride + c * 4);

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

            int start = checked(
                view.byteOffset + accessor.byteOffset);

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

            if (accessor.count < 0 ||
                accessor.byteOffset < 0)
            {
                throw new InvalidDataException(
                    "Accessor contains a negative count or offset.");
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
