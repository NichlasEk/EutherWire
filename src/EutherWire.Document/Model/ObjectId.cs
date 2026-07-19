namespace EutherWire.Document.Model;

public readonly record struct ObjectId
{
    private ObjectId(string value) => Value = value;

    public string Value { get; }

    public static ObjectId New() => new(Guid.NewGuid().ToString("N"));

    public static ObjectId Parse(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.')))
        {
            throw new FormatException($"Invalid object ID '{value}'.");
        }
        return new ObjectId(value);
    }

    public override string ToString() => Value ?? string.Empty;
}
