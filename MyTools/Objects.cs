namespace My;

public enum ObjectShape
{
    Cube,
    Cuboid,
    Sphere,
    Cylinder,
    Cone,
    Pyramid,
    Plane,
    Rectangle,
    Circle,
    Triangle,
    Custom
}

public readonly record struct ObjectColor(string Value)
{
    public static ObjectColor Hex(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Die Farbe darf nicht leer sein.", nameof(value));
        }

        string color = value.StartsWith('#') ? value : $"#{value}";

        if (color.Length != 7 || color.Skip(1).Any(character => !Uri.IsHexDigit(character)))
        {
            throw new ArgumentException("Die Farbe muss als Hex-Wert angegeben werden, z. B. #ff0000.", nameof(value));
        }

        return new ObjectColor(color.ToUpperInvariant());
    }

    public static ObjectColor Rgb(byte red, byte green, byte blue)
    {
        return Hex($"{red:X2}{green:X2}{blue:X2}");
    }

    public static ObjectColor Named(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Der Farbname darf nicht leer sein.", nameof(name));
        }

        return new ObjectColor(name.Trim());
    }

    public override string ToString()
    {
        return Value;
    }
}

public readonly record struct ObjectSize(double Width, double Height, double Depth)
{
    public static ObjectSize All(double value)
    {
        return new ObjectSize(value, value, value);
    }

    public static ObjectSize TwoD(double width, double height)
    {
        return new ObjectSize(width, height, 0);
    }

    public static ObjectSize ThreeD(double width, double height, double depth)
    {
        return new ObjectSize(width, height, depth);
    }

    public void Validate(bool is3D)
    {
        ValidatePositive(Width, nameof(Width));
        ValidatePositive(Height, nameof(Height));

        if (is3D)
        {
            ValidatePositive(Depth, nameof(Depth));
        }
        else if (Depth < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(Depth), "Die Tiefe darf nicht kleiner als 0 sein.");
        }
    }

    private static void ValidatePositive(double value, string name)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(name, "Die Länge muss größer als 0 sein.");
        }
    }
}

public readonly record struct ObjectPosition(double X, double Y, double Z)
{
    public static ObjectPosition TwoD(double x, double y)
    {
        return new ObjectPosition(x, y, 0);
    }

    public static ObjectPosition ThreeD(double x, double y, double z)
    {
        return new ObjectPosition(x, y, z);
    }
}

public readonly record struct ObjectRotation(double X, double Y, double Z)
{
    public static ObjectRotation None => new(0, 0, 0);
}

public sealed record ObjectShadow(
    bool Enabled = true,
    ObjectColor? Color = null,
    double OffsetX = 0.25,
    double OffsetY = -0.25,
    double OffsetZ = 0.25,
    double Blur = 0.5,
    double Opacity = 0.35)
{
    public void Validate()
    {
        if (Blur < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(Blur), "Die Schatten-Weichzeichnung darf nicht kleiner als 0 sein.");
        }

        if (Opacity is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(Opacity), "Die Deckkraft muss zwischen 0 und 1 liegen.");
        }
    }
}

public sealed record ObjectMaterial(
    double Opacity = 1,
    bool Wireframe = false,
    double Roughness = 0.5,
    double Metallic = 0)
{
    public void Validate()
    {
        ValidateRange(Opacity, nameof(Opacity));
        ValidateRange(Roughness, nameof(Roughness));
        ValidateRange(Metallic, nameof(Metallic));
    }

    private static void ValidateRange(double value, string name)
    {
        if (value is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(name, "Der Wert muss zwischen 0 und 1 liegen.");
        }
    }
}

public sealed record SceneObject(
    ObjectShape Shape,
    bool Is3D,
    ObjectColor Color,
    IReadOnlyDictionary<string, ObjectColor> PartColors,
    ObjectSize Size,
    IReadOnlyDictionary<string, double> PartLengths,
    ObjectPosition Position,
    ObjectRotation Rotation,
    ObjectShadow Shadow,
    ObjectMaterial Material,
    string? Name = null)
{
    public override string ToString()
    {
        string dimensions = Is3D
            ? $"{Size.Width} x {Size.Height} x {Size.Depth}"
            : $"{Size.Width} x {Size.Height}";

        string type = Is3D ? "3D" : "2D";
        string name = string.IsNullOrWhiteSpace(Name) ? Shape.ToString() : Name;

        return $"{name}: {type}-{Shape}, Farbe {Color}, Größe {dimensions}, Position ({Position.X}, {Position.Y}, {Position.Z})";
    }
}

public static class Objects
{
    public static SceneObject Create(
        ObjectShape shape,
        ObjectColor? color = null,
        ObjectSize? size = null,
        ObjectPosition? position = null,
        bool is3D = true,
        ObjectRotation? rotation = null,
        ObjectShadow? shadow = null,
        ObjectMaterial? material = null,
        IReadOnlyDictionary<string, ObjectColor>? partColors = null,
        IReadOnlyDictionary<string, double>? partLengths = null,
        string? name = null)
    {
        ObjectSize finalSize = size ?? ObjectSize.All(1);
        ObjectShadow finalShadow = shadow ?? new ObjectShadow();
        ObjectMaterial finalMaterial = material ?? new ObjectMaterial();
        IReadOnlyDictionary<string, double> finalPartLengths = partLengths ?? new Dictionary<string, double>();

        finalSize.Validate(is3D);
        finalShadow.Validate();
        finalMaterial.Validate();
        ValidatePartLengths(finalPartLengths);

        return new SceneObject(
            shape,
            is3D,
            color ?? ObjectColor.Hex("#FFFFFF"),
            partColors ?? new Dictionary<string, ObjectColor>(),
            finalSize,
            finalPartLengths,
            position ?? ObjectPosition.ThreeD(0, 0, 0),
            rotation ?? ObjectRotation.None,
            finalShadow,
            finalMaterial,
            name);
    }

    public static ObjectColor Color(string value)
    {
        return value.StartsWith('#') ? ObjectColor.Hex(value) : ObjectColor.Named(value);
    }

    public static ObjectColor Rgb(byte red, byte green, byte blue)
    {
        return ObjectColor.Rgb(red, green, blue);
    }

    public static ObjectSize Size(double all)
    {
        return ObjectSize.All(all);
    }

    public static ObjectSize Size(double width, double height)
    {
        return ObjectSize.TwoD(width, height);
    }

    public static ObjectSize Size(double width, double height, double depth)
    {
        return ObjectSize.ThreeD(width, height, depth);
    }

    public static ObjectPosition Position(double x, double y)
    {
        return ObjectPosition.TwoD(x, y);
    }

    public static ObjectPosition Position(double x, double y, double z)
    {
        return ObjectPosition.ThreeD(x, y, z);
    }

    public static ObjectShadow Shadow(
        bool enabled = true,
        ObjectColor? color = null,
        double offsetX = 0.25,
        double offsetY = -0.25,
        double offsetZ = 0.25,
        double blur = 0.5,
        double opacity = 0.35)
    {
        return new ObjectShadow(enabled, color, offsetX, offsetY, offsetZ, blur, opacity);
    }

    public static ObjectMaterial Material(
        double opacity = 1,
        bool wireframe = false,
        double roughness = 0.5,
        double metallic = 0)
    {
        return new ObjectMaterial(opacity, wireframe, roughness, metallic);
    }

    private static void ValidatePartLengths(IReadOnlyDictionary<string, double> partLengths)
    {
        foreach (KeyValuePair<string, double> partLength in partLengths)
        {
            if (string.IsNullOrWhiteSpace(partLength.Key))
            {
                throw new ArgumentException("Ein Teil-Name darf nicht leer sein.", nameof(partLengths));
            }

            if (partLength.Value <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(partLengths), "Einzelne Längen müssen größer als 0 sein.");
            }
        }
    }
}
