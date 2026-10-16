// ULTRADROID Models API - first GLB 2.0 loader prototype
// Target: Unity / BepInEx IL2CPP. Source scaffold; compile against the exact game references.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
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

        /// <summary>Loads a binary glTF 2.0 (.glb) file into the active Unity scene.</summary>
        public static GameObject LoadGLB(string path, Transform parent = null, ModelLoadOptions options = null)
        {
            if (string.IsNullOrEmpty(path)) throw new ArgumentException("A GLB path is required.", "path");
            if (!File.Exists(path)) throw new FileNotFoundException("GLB file was not found.", path);
            options = options ?? new ModelLoadOptions();

            byte[] file = File.ReadAllBytes(path);
            GlbDocument doc = GlbDocument.Parse(file);
            GameObject root = new GameObject(string.IsNullOrEmpty(options.Name) ? Path.GetFileNameWithoutExtension(path) : options.Name);
            if (parent != null) root.transform.SetParent(parent, false);
            root.transform.localPosition = options.Position;
            root.transform.localRotation = Quaternion.Euler(options.RotationEuler);
            root.transform.localScale = options.Scale;

            try
            {
                Texture2D[] textures = BuildTextures(doc, options.LoadTextures);
                Material[] materials = BuildMaterials(doc, textures);
                BuildScene(doc, root.transform, materials, options.CreateColliders);
                return root;
            }
            catch
            {
                UnityEngine.Object.Destroy(root);
                throw;
            }
        }

        private static Texture2D[] BuildTextures(GlbDocument d, bool load)
        {
            Texture2D[] result = new Texture2D[d.json.images == null ? 0 : d.json.images.Length];
            if (!load || d.json.images == null) return result;
            for (int i = 0; i < d.json.images.Length; i++)
            {
                GltfImage image = d.json.images[i];
                if (image.bufferView < 0 || image.bufferView >= d.json.bufferViews.Length) continue;
                byte[] bytes = d.ReadBufferView(image.bufferView);
                Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, true);
                if (ImageConversion.LoadImage(texture, bytes, false))
                {
                    texture.name = "ULTRADROID_GLTF_Image_" + i;
                    texture.wrapMode = TextureWrapMode.Repeat;
                    texture.filterMode = FilterMode.Bilinear;
                    result[i] = texture;
                }
                else UnityEngine.Object.Destroy(texture);
            }
            return result;
        }

        private static Material[] BuildMaterials(GlbDocument d, Texture2D[] textures)
        {
            int count = d.json.materials == null ? 0 : d.json.materials.Length;
            Material[] result = new Material[count + 1];
            Shader shader = Shader.Find("Standard");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            for (int i = 0; i < count; i++)
            {
                GltfMaterial source = d.json.materials[i];
                Material mat = new Material(shader);
                mat.name = string.IsNullOrEmpty(source.name) ? "GLTF_Material_" + i : source.name;
                if (source.pbrMetallicRoughness != null)
                {
                    PbrMetallicRoughness pbr = source.pbrMetallicRoughness;
                    if (pbr.baseColorFactor != null && pbr.baseColorFactor.Length >= 4)
                        mat.color = new Color(pbr.baseColorFactor[0], pbr.baseColorFactor[1], pbr.baseColorFactor[2], pbr.baseColorFactor[3]);
                    if (pbr.baseColorTexture != null)
                    {
                        int ti = GetImageIndex(d, pbr.baseColorTexture.index);
                        if (ti >= 0 && ti < textures.Length && textures[ti] != null && mat.HasProperty("_MainTex"))
                            mat.mainTexture = textures[ti];
                    }
                    if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", pbr.metallicFactor);
                    if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", 1f - pbr.roughnessFactor);
                }
                if (source.emissiveFactor != null && source.emissiveFactor.Length >= 3 && mat.HasProperty("_EmissionColor"))
                {
                    Color emission = new Color(source.emissiveFactor[0], source.emissiveFactor[1], source.emissiveFactor[2]);
                    mat.EnableKeyword("_EMISSION");
                    mat.SetColor("_EmissionColor", emission);
                }
                result[i] = mat;
            }
            result[count] = new Material(shader) { name = "GLTF_Default_Material" };
            return result;
        }

        private static int GetImageIndex(GlbDocument d, int textureIndex)
        {
            if (d.json.textures == null || textureIndex < 0 || textureIndex >= d.json.textures.Length) return -1;
            return d.json.textures[textureIndex].source;
        }

        private static void BuildScene(GlbDocument d, Transform root, Material[] materials, bool colliders)
        {
            if (d.json.nodes == null || d.json.nodes.Length == 0) return;
            int sceneIndex = d.json.scene;
            int[] roots = null;
            if (d.json.scenes != null && d.json.scenes.Length > 0)
            {
                if (sceneIndex < 0 || sceneIndex >= d.json.scenes.Length) sceneIndex = 0;
                roots = d.json.scenes[sceneIndex].nodes;
            }
            if (roots == null || roots.Length == 0)
            {
                HashSet<int> childIndices = new HashSet<int>();
                foreach (GltfNode n in d.json.nodes) if (n.children != null) foreach (int c in n.children) childIndices.Add(c);
                List<int> inferredRoots = new List<int>();
                for (int i = 0; i < d.json.nodes.Length; i++) if (!childIndices.Contains(i)) inferredRoots.Add(i);
                roots = inferredRoots.ToArray();
            }
            HashSet<int> recursionGuard = new HashSet<int>();
            foreach (int nodeIndex in roots) BuildNode(d, nodeIndex, root, materials, colliders, recursionGuard);
        }

        private static void BuildNode(GlbDocument d, int index, Transform parent, Material[] materials, bool colliders, HashSet<int> guard)
        {
            if (index < 0 || index >= d.json.nodes.Length || guard.Contains(index)) return;
            guard.Add(index);
            GltfNode node = d.json.nodes[index];
            GameObject go = new GameObject(string.IsNullOrEmpty(node.name) ? "GLTF_Node_" + index : node.name);
            go.transform.SetParent(parent, false);
            ApplyNodeTransform(go.transform, node);
            if (node.mesh >= 0 && d.json.meshes != null && node.mesh < d.json.meshes.Length)
                BuildMesh(d, d.json.meshes[node.mesh], go, materials, colliders);
            if (node.children != null) foreach (int child in node.children) BuildNode(d, child, go.transform, materials, colliders, guard);
            guard.Remove(index);
        }

        private static void ApplyNodeTransform(Transform t, GltfNode n)
        {
            if (n.matrix != null && n.matrix.Length == 16)
            {
                Matrix4x4 m = new Matrix4x4();
                // glTF matrices are column-major.
                for (int r = 0; r < 4; r++) for (int c = 0; c < 4; c++) m[r, c] = n.matrix[c * 4 + r];
                t.localPosition = m.GetColumn(3);
                t.localRotation = m.rotation;
                t.localScale = m.lossyScale;
                return;
            }
            if (n.translation != null && n.translation.Length >= 3) t.localPosition = new Vector3(n.translation[0], n.translation[1], n.translation[2]);
            if (n.rotation != null && n.rotation.Length >= 4) t.localRotation = new Quaternion(n.rotation[0], n.rotation[1], n.rotation[2], n.rotation[3]);
            if (n.scale != null && n.scale.Length >= 3) t.localScale = new Vector3(n.scale[0], n.scale[1], n.scale[2]);
        }

        private static void BuildMesh(GlbDocument d, GltfMesh source, GameObject go, Material[] materials, bool colliders)
        {
            if (source.primitives == null || source.primitives.Length == 0) return;
            // Unity Mesh supports one material per submesh; combine primitive data into a single Mesh.
            List<Vector3> vertices = new List<Vector3>();
            List<Vector3> normals = new List<Vector3>();
            List<Vector2> uv = new List<Vector2>();
            List<List<int>> submeshTriangles = new List<List<int>>();
            List<Material> submeshMaterials = new List<Material>();
            bool hasNormals = true, hasUv = true;
            foreach (GltfPrimitive p in source.primitives)
            {
                if (p.attributes == null || p.attributes.POSITION < 0) continue;
                Vector3[] pv = d.ReadVector3(p.attributes.POSITION);
                Vector3[] pn = p.attributes.NORMAL >= 0 ? d.ReadVector3(p.attributes.NORMAL) : null;
                Vector2[] pu = p.attributes.TEXCOORD_0 >= 0 ? d.ReadVector2(p.attributes.TEXCOORD_0) : null;
                int offset = vertices.Count;
                vertices.AddRange(pv);
                if (pn == null || pn.Length != pv.Length) hasNormals = false; else normals.AddRange(pn);
                if (pu == null || pu.Length != pv.Length) hasUv = false; else uv.AddRange(pu);
                int[] indices = p.indices >= 0 ? d.ReadIndices(p.indices) : MakeSequential(pv.Length);
                List<int> triangles = new List<int>(indices.Length);
                for (int j = 0; j + 2 < indices.Length; j += 3)
                {
                    // glTF and Unity use the same triangle winding; no flip here.
                    triangles.Add(offset + indices[j]); triangles.Add(offset + indices[j + 1]); triangles.Add(offset + indices[j + 2]);
                }
                submeshTriangles.Add(triangles);
                int mi = p.material >= 0 && p.material < materials.Length - 1 ? p.material : materials.Length - 1;
                submeshMaterials.Add(materials[mi]);
            }
            if (vertices.Count == 0) return;
            Mesh mesh = new Mesh();
            mesh.name = string.IsNullOrEmpty(source.name) ? go.name + "_Mesh" : source.name;
            if (vertices.Count > 65535) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(vertices);
            mesh.subMeshCount = submeshTriangles.Count;
            for (int i = 0; i < submeshTriangles.Count; i++) mesh.SetTriangles(submeshTriangles[i], i, false);
            if (hasNormals && normals.Count == vertices.Count) mesh.SetNormals(normals); else mesh.RecalculateNormals();
            if (hasUv && uv.Count == vertices.Count) mesh.SetUVs(0, uv);
            mesh.RecalculateBounds();
            MeshFilter filter = go.AddComponent<MeshFilter>(); filter.sharedMesh = mesh;
            MeshRenderer renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterials = submeshMaterials.ToArray();
            if (colliders) { MeshCollider collider = go.AddComponent<MeshCollider>(); collider.sharedMesh = mesh; }
        }

        private static int[] MakeSequential(int count) { int[] a = new int[count]; for (int i = 0; i < count; i++) a[i] = i; return a; }
    }

    internal sealed class GlbDocument
    {
        internal GltfRoot json;
        internal byte[] binary;
        internal static GlbDocument Parse(byte[] file)
        {
            if (file == null || file.Length < 20) throw new InvalidDataException("File is too small to be a GLB.");
            if (ReadU32(file, 0) != 0x46546C67) throw new InvalidDataException("Invalid GLB magic; expected glTF.");
            if (ReadU32(file, 4) != 2) throw new InvalidDataException("Only GLB version 2 is supported.");
            uint declaredLength = ReadU32(file, 8);
            if (declaredLength > file.Length || declaredLength < 20) throw new InvalidDataException("GLB declared length is invalid.");
            byte[] jsonBytes = null, binBytes = null;
            int pos = 12;
            while (pos + 8 <= declaredLength)
            {
                uint length = ReadU32(file, pos); uint type = ReadU32(file, pos + 4); pos += 8;
                if (length > declaredLength - pos) throw new InvalidDataException("GLB chunk exceeds file length.");
                byte[] chunk = new byte[length]; Buffer.BlockCopy(file, pos, chunk, 0, (int)length); pos += (int)length;
                if (type == 0x4E4F534A) jsonBytes = chunk;
                else if (type == 0x004E4942) binBytes = chunk;
            }
            if (jsonBytes == null) throw new InvalidDataException("GLB JSON chunk is missing.");
            string jsonText = Encoding.UTF8.GetString(jsonBytes).TrimEnd('\0', ' ', '\r', '\n', '\t');
            GltfRoot root = JsonUtility.FromJson<GltfRoot>(jsonText);
            if (root == null || root.asset == null || root.asset.version == null || !root.asset.version.StartsWith("2")) throw new InvalidDataException("The file is not glTF 2.x.");
            return new GlbDocument { json = root, binary = binBytes ?? new byte[0] };
        }
        internal byte[] ReadBufferView(int index)
        {
            GltfBufferView v = GetView(index); byte[] b = new byte[v.byteLength]; Buffer.BlockCopy(binary, v.byteOffset, b, 0, v.byteLength); return b;
        }
        internal Vector3[] ReadVector3(int accessorIndex) { float[] f = ReadFloats(accessorIndex, 3); Vector3[] a = new Vector3[f.Length / 3]; for (int i=0;i<a.Length;i++) a[i]=new Vector3(f[i*3],f[i*3+1],f[i*3+2]); return a; }
        internal Vector2[] ReadVector2(int accessorIndex) { float[] f = ReadFloats(accessorIndex, 2); Vector2[] a = new Vector2[f.Length / 2]; for (int i=0;i<a.Length;i++) a[i]=new Vector2(f[i*2],f[i*2+1]); return a; }
        internal float[] ReadFloats(int accessorIndex, int components)
        {
            GltfAccessor a = GetAccessor(accessorIndex); if (a.componentType != 5126) throw new InvalidDataException("Expected FLOAT accessor for vector attributes.");
            if (ComponentCount(a.type) != components) throw new InvalidDataException("Unexpected accessor type for vector attribute.");
            GltfBufferView v = GetView(a.bufferView); int stride = v.byteStride > 0 ? v.byteStride : components * 4; int start = v.byteOffset + a.byteOffset;
            float[] result = new float[a.count * components];
            for (int i=0;i<a.count;i++) for (int c=0;c<components;c++) result[i*components+c] = BitConverter.ToSingle(binary, start + i*stride + c*4);
            return result;
        }
        internal int[] ReadIndices(int accessorIndex)
        {
            GltfAccessor a = GetAccessor(accessorIndex); GltfBufferView v = GetView(a.bufferView); int size = a.componentType == 5121 ? 1 : a.componentType == 5123 ? 2 : a.componentType == 5125 ? 4 : 0;
            if (size == 0) throw new InvalidDataException("Unsupported index component type.");
            int stride = v.byteStride > 0 ? v.byteStride : size; int start = v.byteOffset + a.byteOffset; int[] result = new int[a.count];
            for (int i=0;i<a.count;i++) { int p=start+i*stride; if(size==1) result[i]=binary[p]; else if(size==2) result[i]=BitConverter.ToUInt16(binary,p); else { uint value=BitConverter.ToUInt32(binary,p); if(value>int.MaxValue) throw new InvalidDataException("Index is too large."); result[i]=(int)value; } }
            return result;
        }
        private GltfAccessor GetAccessor(int i) { if (json.accessors == null || i < 0 || i >= json.accessors.Length) throw new InvalidDataException("Accessor index is invalid."); return json.accessors[i]; }
        private GltfBufferView GetView(int i) { if (json.bufferViews == null || i < 0 || i >= json.bufferViews.Length) throw new InvalidDataException("Buffer view index is invalid."); GltfBufferView v=json.bufferViews[i]; if(v.byteOffset<0||v.byteLength<0||v.byteOffset+v.byteLength>binary.Length) throw new InvalidDataException("Buffer view is outside the binary chunk."); return v; }
        private static int ComponentCount(string type) { if(type=="SCALAR")return 1;if(type=="VEC2")return 2;if(type=="VEC3")return 3;if(type=="VEC4")return 4;throw new InvalidDataException("Unsupported accessor type: "+type); }
        private static uint ReadU32(byte[] b, int i) { return BitConverter.ToUInt32(b, i); }
    }

    [Serializable] internal class GltfRoot { public GltfAsset asset; public int scene; public GltfScene[] scenes; public GltfNode[] nodes; public GltfMesh[] meshes; public GltfMaterial[] materials; public GltfTexture[] textures; public GltfImage[] images; public GltfBufferView[] bufferViews; public GltfAccessor[] accessors; }
    [Serializable] internal class GltfAsset { public string version; }
    [Serializable] internal class GltfScene { public int[] nodes; }
    [Serializable] internal class GltfNode { public string name; public int mesh = -1; public int[] children; public float[] translation; public float[] rotation; public float[] scale; public float[] matrix; }
    [Serializable] internal class GltfMesh { public string name; public GltfPrimitive[] primitives; }
    [Serializable] internal class GltfPrimitive { public GltfAttributes attributes; public int indices = -1; public int material = -1; public int mode = 4; }
    [Serializable] internal class GltfAttributes { public int POSITION = -1; public int NORMAL = -1; public int TANGENT = -1; public int TEXCOORD_0 = -1; }
    [Serializable] internal class GltfMaterial { public string name; public PbrMetallicRoughness pbrMetallicRoughness; public float[] emissiveFactor; }
    [Serializable] internal class PbrMetallicRoughness { public float[] baseColorFactor; public GltfTextureInfo baseColorTexture; public float metallicFactor = 1f; public float roughnessFactor = 1f; }
    [Serializable] internal class GltfTextureInfo { public int index; }
    [Serializable] internal class GltfTexture { public int source = -1; }
    [Serializable] internal class GltfImage { public int bufferView = -1; public string mimeType; }
    [Serializable] internal class GltfBufferView { public int buffer; public int byteOffset; public int byteLength; public int byteStride; }
    [Serializable] internal class GltfAccessor { public int bufferView = -1; public int byteOffset; public int componentType; public int count; public string type; public bool normalized; }
}
