
internal static class GltfManualParser
{
    private sealed class Reader
    {
        private readonly string s;
        private int p;

        public Reader(string text)
        {
            s = text ?? throw new ArgumentNullException("text");
        }

        private void White()
        {
            while (p < s.Length && char.IsWhiteSpace(s[p]))
                p++;
        }

        private char Peek()
        {
            White();
            return p < s.Length ? s[p] : '\0';
        }

        private void Expect(char c)
        {
            White();
            if (p >= s.Length || s[p] != c)
                throw new InvalidDataException(
                    "Expected '" + c + "' at JSON position " + p);
            p++;
        }

        public object ReadValue()
        {
            White();

            if (p >= s.Length)
                throw new InvalidDataException("Unexpected end of JSON.");

            char c = s[p];

            if (c == '{') return ReadObject();
            if (c == '[') return ReadArray();
            if (c == '"') return ReadString();

            if (c == '-' || (c >= '0' && c <= '9'))
                return ReadNumber();

            if (Match("true")) return true;
            if (Match("false")) return false;
            if (Match("null")) return null;

            throw new InvalidDataException(
                "Invalid JSON token at position " + p);
        }

        private bool Match(string word)
        {
            if (p + word.Length > s.Length)
                return false;

            if (string.CompareOrdinal(s, p, word, 0, word.Length) != 0)
                return false;

            p += word.Length;
            return true;
        }

        private Dictionary<string, object> ReadObject()
        {
            var result = new Dictionary<string, object>(
                StringComparer.Ordinal);

            Expect('{');

            if (Peek() == '}')
            {
                p++;
                return result;
            }

            while (true)
            {
                White();

                if (Peek() != '"')
                    throw new InvalidDataException(
                        "Expected JSON object key at position " + p);

                string key = ReadString();
                Expect(':');

                if (result.ContainsKey(key))
                    throw new InvalidDataException(
                        "Duplicate JSON key: " + key);

                result.Add(key, ReadValue());

                char next = Peek();

                if (next == '}')
                {
                    p++;
                    return result;
                }

                Expect(',');
            }
        }

        private List<object> ReadArray()
        {
            var result = new List<object>();

            Expect('[');

            if (Peek() == ']')
            {
                p++;
                return result;
            }

            while (true)
            {
                result.Add(ReadValue());

                char next = Peek();

                if (next == ']')
                {
                    p++;
                    return result;
                }

                Expect(',');
            }
        }

        private string ReadString()
        {
            Expect('"');
            var result = new StringBuilder();

            while (p < s.Length)
            {
                char c = s[p++];

                if (c == '"')
                    return result.ToString();

                if (c < 0x20)
                    throw new InvalidDataException(
                        "Unescaped control character in JSON string.");

                if (c != '\\')
                {
                    result.Append(c);
                    continue;
                }

                if (p >= s.Length)
                    throw new InvalidDataException(
                        "Incomplete JSON escape.");

                char e = s[p++];

                switch (e)
                {
                    case '"': result.Append('"'); break;
                    case '\\': result.Append('\\'); break;
                    case '/': result.Append('/'); break;
                    case 'b': result.Append('\b'); break;
                    case 'f': result.Append('\f'); break;
                    case 'n': result.Append('\n'); break;
                    case 'r': result.Append('\r'); break;
                    case 't': result.Append('\t'); break;

                    case 'u':
                        if (p + 4 > s.Length)
                            throw new InvalidDataException(
                                "Incomplete Unicode escape.");

                        int code;
                        if (!int.TryParse(
                            s.Substring(p, 4),
                            NumberStyles.HexNumber,
                            CultureInfo.InvariantCulture,
                            out code))
                        {
                            throw new InvalidDataException(
                                "Invalid Unicode escape.");
                        }

                        result.Append((char)code);
                        p += 4;
                        break;

                    default:
                        throw new InvalidDataException(
                            "Invalid JSON escape: \\" + e);
                }
            }

            throw new InvalidDataException("Unterminated JSON string.");
        }

        private double ReadNumber()
        {
            int start = p;

            if (s[p] == '-')
                p++;

            if (p >= s.Length)
                throw new InvalidDataException("Invalid JSON number.");

            if (s[p] == '0')
            {
                p++;
            }
            else
            {
                if (s[p] < '1' || s[p] > '9')
                    throw new InvalidDataException("Invalid JSON number.");

                while (p < s.Length && s[p] >= '0' && s[p] <= '9')
                    p++;
            }

            if (p < s.Length && s[p] == '.')
            {
                p++;

                int fractionStart = p;

                while (p < s.Length && s[p] >= '0' && s[p] <= '9')
                    p++;

                if (p == fractionStart)
                    throw new InvalidDataException(
                        "Invalid JSON fractional number.");
            }

            if (p < s.Length && (s[p] == 'e' || s[p] == 'E'))
            {
                p++;

                if (p < s.Length && (s[p] == '+' || s[p] == '-'))
                    p++;

                int exponentStart = p;

                while (p < s.Length && s[p] >= '0' && s[p] <= '9')
                    p++;

                if (p == exponentStart)
                    throw new InvalidDataException(
                        "Invalid JSON exponent.");
            }

            double value;

            if (!double.TryParse(
                s.Substring(start, p - start),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out value))
            {
                throw new InvalidDataException(
                    "Could not parse JSON number.");
            }

            if (double.IsNaN(value) || double.IsInfinity(value))
                throw new InvalidDataException(
                    "Non-finite JSON number.");

            return value;
        }

        public void RequireEnd()
        {
            White();

            if (p != s.Length)
                throw new InvalidDataException(
                    "Unexpected trailing JSON data at position " + p);
        }
    }

    private static Dictionary<string, object> Obj(object value)
    {
        return value as Dictionary<string, object>;
    }

    private static List<object> Arr(object value)
    {
        return value as List<object>;
    }

    private static object Get(
        Dictionary<string, object> obj, string key)
    {
        object value;

        return obj != null && obj.TryGetValue(key, out value)
            ? value
            : null;
    }

    private static int Int(
        Dictionary<string, object> obj, string key, int fallback = 0)
    {
        object value = Get(obj, key);

        if (value == null)
            return fallback;

        if (!(value is double))
            throw new InvalidDataException(
                "Expected integer field: " + key);

        double number = (double)value;

        if (number != Math.Truncate(number) ||
            number < int.MinValue || number > int.MaxValue)
        {
            throw new InvalidDataException(
                "Invalid integer field: " + key);
        }

        return (int)number;
    }

    private static string Str(
        Dictionary<string, object> obj, string key, string fallback = null)
    {
        object value = Get(obj, key);

        if (value == null)
            return fallback;

        string result = value as string;

        if (result == null)
            throw new InvalidDataException(
                "Expected string field: " + key);

        return result;
    }

    private static bool Bool(
        Dictionary<string, object> obj, string key, bool fallback = false)
    {
        object value = Get(obj, key);

        if (value == null)
            return fallback;

        if (!(value is bool))
            throw new InvalidDataException(
                "Expected boolean field: " + key);

        return (bool)value;
    }

    private static float[] Floats(object value, string field)
    {
        if (value == null)
            return null;

        List<object> array = Arr(value);

        if (array == null)
            throw new InvalidDataException(
                "Expected numeric array: " + field);

        float[] result = new float[array.Count];

        for (int i = 0; i < array.Count; i++)
        {
            if (!(array[i] is double))
                throw new InvalidDataException(
                    "Expected number in " + field);

            double number = (double)array[i];

            if (number < -float.MaxValue || number > float.MaxValue)
                throw new InvalidDataException(
                    "Number outside float range in " + field);

            result[i] = (float)number;
        }

        return result;
    }

    private static int[] Ints(object value, string field)
    {
        if (value == null)
            return null;

        List<object> array = Arr(value);

        if (array == null)
            throw new InvalidDataException(
                "Expected integer array: " + field);

        int[] result = new int[array.Count];

        for (int i = 0; i < array.Count; i++)
        {
            if (!(array[i] is double))
                throw new InvalidDataException(
                    "Expected integer in " + field);

            double number = (double)array[i];

            if (number != Math.Truncate(number) ||
                number < int.MinValue || number > int.MaxValue)
            {
                throw new InvalidDataException(
                    "Invalid integer in " + field);
            }

            result[i] = (int)number;
        }

        return result;
    }

    private static T[] Array<T>(
        object value, string field, Func<object, T> convert)
    {
        if (value == null)
            return null;

        List<object> source = Arr(value);

        if (source == null)
            throw new InvalidDataException(
                "Expected array: " + field);

        T[] result = new T[source.Count];

        for (int i = 0; i < source.Count; i++)
            result[i] = convert(source[i]);

        return result;
    }

    public static GltfRoot ParseRoot(string text)
    {
        var reader = new Reader(text);
        object parsed = reader.ReadValue();
        reader.RequireEnd();

        Dictionary<string, object> obj = Obj(parsed);

        if (obj == null)
            throw new InvalidDataException(
                "glTF JSON root must be an object.");

        var root = new GltfRoot();

        Dictionary<string, object> asset = Obj(Get(obj, "asset"));

        if (asset != null)
            root.asset = new GltfAsset
            {
                version = Str(asset, "version")
            };

        root.scene = Int(obj, "scene", 0);

        root.scenes = Array(
            Get(obj, "scenes"), "scenes", value =>
            {
                var o = Obj(value);
                if (o == null)
                    throw new InvalidDataException("Invalid scene entry.");

                return new GltfScene
                {
                    nodes = Ints(Get(o, "nodes"), "scenes.nodes")
                };
            });

        root.nodes = Array(
            Get(obj, "nodes"), "nodes", value =>
            {
                var o = Obj(value);
                if (o == null)
                    throw new InvalidDataException("Invalid node entry.");

                return new GltfNode
                {
                    name = Str(o, "name"),
                    mesh = Int(o, "mesh", -1),
                    children = Ints(Get(o, "children"), "nodes.children"),
                    translation = Floats(Get(o, "translation"), "nodes.translation"),
                    rotation = Floats(Get(o, "rotation"), "nodes.rotation"),
                    scale = Floats(Get(o, "scale"), "nodes.scale"),
                    matrix = Floats(Get(o, "matrix"), "nodes.matrix")
                };
            });

        root.meshes = Array(
            Get(obj, "meshes"), "meshes", value =>
            {
                var o = Obj(value);
                if (o == null)
                    throw new InvalidDataException("Invalid mesh entry.");

                return new GltfMesh
                {
                    name = Str(o, "name"),
                    primitives = Array(
                        Get(o, "primitives"), "meshes.primitives", primitiveValue =>
                        {
                            var primitive = Obj(primitiveValue);
                            if (primitive == null)
                                throw new InvalidDataException("Invalid primitive.");

                            var attributes = new GltfAttributes();
                            var attributeObject = Obj(Get(primitive, "attributes"));

                            if (attributeObject != null)
                            {
                                attributes.POSITION = Int(attributeObject, "POSITION", -1);
                                attributes.NORMAL = Int(attributeObject, "NORMAL", -1);
                                attributes.TANGENT = Int(attributeObject, "TANGENT", -1);
                                attributes.TEXCOORD_0 = Int(attributeObject, "TEXCOORD_0", -1);
                            }

                            return new GltfPrimitive
                            {
                                attributes = attributes,
                                indices = Int(primitive, "indices", -1),
                                material = Int(primitive, "material", -1),
                                mode = Int(primitive, "mode", 4)
                            };
                        })
                };
            });

        root.materials = Array(
            Get(obj, "materials"), "materials", value =>
            {
                var o = Obj(value);
                if (o == null)
                    throw new InvalidDataException("Invalid material entry.");

                var material = new GltfMaterial
                {
                    name = Str(o, "name"),
                    emissiveFactor = Floats(Get(o, "emissiveFactor"), "materials.emissiveFactor")
                };

                var pbr = Obj(Get(o, "pbrMetallicRoughness"));

                if (pbr != null)
                {
                    var converted = new PbrMetallicRoughness
                    {
                        baseColorFactor = Floats(Get(pbr, "baseColorFactor"), "baseColorFactor"),
                        metallicFactor = FloatValue(pbr, "metallicFactor", 1f),
                        roughnessFactor = FloatValue(pbr, "roughnessFactor", 1f)
                    };

                    var baseTexture = Obj(Get(pbr, "baseColorTexture"));

                    if (baseTexture != null)
                    {
                        converted.baseColorTexture = new GltfTextureInfo
                        {
                            index = Int(baseTexture, "index", -1)
                        };
                    }

                    material.pbrMetallicRoughness = converted;
                }

                return material;
            });

        root.textures = Array(
            Get(obj, "textures"), "textures", value =>
            {
                var o = Obj(value);
                if (o == null)
                    throw new InvalidDataException("Invalid texture entry.");

                return new GltfTexture
                {
                    source = Int(o, "source", -1)
                };
            });

        root.images = Array(
            Get(obj, "images"), "images", value =>
            {
                var o = Obj(value);
                if (o == null)
                    throw new InvalidDataException("Invalid image entry.");

                return new GltfImage
                {
                    bufferView = Int(o, "bufferView", -1),
                    mimeType = Str(o, "mimeType")
                };
            });

        root.bufferViews = Array(
            Get(obj, "bufferViews"), "bufferViews", value =>
            {
                var o = Obj(value);
                if (o == null)
                    throw new InvalidDataException("Invalid bufferView entry.");

                return new GltfBufferView
                {
                    buffer = Int(o, "buffer", 0),
                    byteOffset = Int(o, "byteOffset", 0),
                    byteLength = Int(o, "byteLength", 0),
                    byteStride = Int(o, "byteStride", 0)
                };
            });

        root.accessors = Array(
            Get(obj, "accessors"), "accessors", value =>
            {
                var o = Obj(value);
                if (o == null)
                    throw new InvalidDataException("Invalid accessor entry.");

                return new GltfAccessor
                {
                    bufferView = Int(o, "bufferView", -1),
                    byteOffset = Int(o, "byteOffset", 0),
                    componentType = Int(o, "componentType", 0),
                    count = Int(o, "count", 0),
                    type = Str(o, "type"),
                    normalized = Bool(o, "normalized", false)
                };
            });

        return root;
    }

    private static float FloatValue(
        Dictionary<string, object> obj, string key, float fallback)
    {
        object value = Get(obj, key);

        if (value == null)
            return fallback;

        if (!(value is double))
            throw new InvalidDataException(
                "Expected number field: " + key);

        double number = (double)value;

        if (number < -float.MaxValue || number > float.MaxValue)
            throw new InvalidDataException(
                "Number outside float range: " + key);

        return (float)number;
    }
}
