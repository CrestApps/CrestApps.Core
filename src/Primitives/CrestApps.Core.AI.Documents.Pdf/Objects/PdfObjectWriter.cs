using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace CrestApps.Core.AI.Documents.Pdf.Objects;

/// <summary>
/// Writes a <see cref="PdfDocument"/> as a PDF file.
/// </summary>
/// <remarks>
/// Only the objects reachable from the catalog and the document information are written, numbered again
/// from one, so whatever an edit stopped using leaves the file with it. The file has a classic
/// cross-reference table and no object streams, which every reader opens.
/// </remarks>
internal static class PdfObjectWriter
{
    private static readonly byte[] _binaryComment = [(byte)'%', 0xE2, 0xE3, 0xCF, 0xD3, (byte)'\n'];

    /// <summary>
    /// Writes a document.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <param name="security">Encrypts the file as it is written, or <see langword="null"/> to write it unencrypted.</param>
    /// <returns>The file.</returns>
    public static byte[] Write(PdfDocument document, IPdfObjectEncryptor security = null)
    {
        ArgumentNullException.ThrowIfNull(document);

        document.CompleteForSaving();
        document.Pages.WriteTree();

        var roots = new List<PdfReference> { document.Catalog.Reference ?? document.AddObject(document.Catalog) };

        if (document.InfoReference is not null)
        {
            roots.Add(document.InfoReference);
        }

        var numbers = Collect(document, roots);
        var output = new PdfOutput();

        output.Write("%PDF-" + Header(document.Version, security) + "\n");
        output.Write(_binaryComment);

        var offsets = new long[numbers.Count + 1];
        var identifier = Identifier(document);

        security?.Prepare(identifier[0]);

        foreach (var (reference, number) in numbers)
        {
            offsets[number] = output.Position;
            output.Write(number.ToString(CultureInfo.InvariantCulture) + " 0 obj\n");

            var context = new ObjectContext(numbers, security, number);

            WriteTopLevel(output, reference.Value, context);
            output.Write("\nendobj\n");
        }

        var encryptNumber = 0;

        if (security is not null)
        {
            encryptNumber = numbers.Count + 1;
            Array.Resize(ref offsets, encryptNumber + 1);
            offsets[encryptNumber] = output.Position;
            output.Write(encryptNumber.ToString(CultureInfo.InvariantCulture) + " 0 obj\n");
            WriteItem(output, security.CreateEncryptionDictionary(), new ObjectContext(numbers, null, encryptNumber), false);
            output.Write("\nendobj\n");
        }

        var xref = output.Position;
        var size = offsets.Length;

        output.Write("xref\n0 " + size.ToString(CultureInfo.InvariantCulture) + "\n");
        output.Write("0000000000 65535 f\r\n");

        for (var number = 1; number < size; number++)
        {
            output.Write(offsets[number].ToString("D10", CultureInfo.InvariantCulture) + " 00000 n\r\n");
        }

        output.Write("trailer\n<< /Size " + size.ToString(CultureInfo.InvariantCulture));
        output.Write(" /Root " + numbers[roots[0]].ToString(CultureInfo.InvariantCulture) + " 0 R");

        if (document.InfoReference is not null && numbers.TryGetValue(document.InfoReference, out var info))
        {
            output.Write(" /Info " + info.ToString(CultureInfo.InvariantCulture) + " 0 R");
        }

        if (encryptNumber > 0)
        {
            output.Write(" /Encrypt " + encryptNumber.ToString(CultureInfo.InvariantCulture) + " 0 R");
        }

        output.Write(" /ID [<" + Convert.ToHexString(identifier[0]) + "> <" + Convert.ToHexString(identifier[1]) + ">] >>\n");
        output.Write("startxref\n" + xref.ToString(CultureInfo.InvariantCulture) + "\n%%EOF\n");

        return output.ToArray();
    }

    /// <summary>
    /// Writes a single value as PDF syntax, as it would appear inside an object.
    /// </summary>
    /// <param name="item">The value. A reference is written as <c>n 0 R</c> with its own number.</param>
    /// <returns>The text.</returns>
    public static string ToSyntax(PdfItem item)
    {
        var output = new PdfOutput();

        WriteItem(output, item, new ObjectContext(null, null, 0), false);

        return Encoding.Latin1.GetString(output.ToArray());
    }

    private static string Header(string version, IPdfObjectEncryptor security)
    {
        if (!double.TryParse(version, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            parsed = 1.7;
        }

        // Transparency needs 1.4 and AES 1.6; newer encryption is identified by its own extension level.
        var required = security?.MinimumVersion ?? 1.4;

        return Math.Clamp(Math.Max(parsed, required), 1.4, 2.0).ToString("0.0", CultureInfo.InvariantCulture);
    }

    private static byte[][] Identifier(PdfDocument document)
    {
        var first = document.FileIdentifier is { Count: > 0 } existing && existing[0] is { Length: > 0 } original
            ? original
            : RandomNumberGenerator.GetBytes(16);

        return [first, RandomNumberGenerator.GetBytes(16)];
    }

    private static Dictionary<PdfReference, int> Collect(PdfDocument document, List<PdfReference> roots)
    {
        var numbers = new Dictionary<PdfReference, int>();
        var queue = new Queue<PdfReference>();

        foreach (var root in roots)
        {
            if (numbers.TryAdd(root, numbers.Count + 1))
            {
                queue.Enqueue(root);
            }
        }

        while (queue.Count > 0)
        {
            var reference = queue.Dequeue();
            var pending = new Stack<PdfItem>();

            pending.Push(reference.Value);

            var top = reference.Value;

            while (pending.Count > 0)
            {
                var item = pending.Pop();

                switch (item)
                {
                    case PdfReference child:
                        Enqueue(child);

                        break;

                    case PdfObject value when !ReferenceEquals(value, top) && (value.IsIndirect || value is PdfDictionary { Stream: not null }):
                        // A stream must be an object of its own, and an object stored on its own is referred to, not copied.
                        Enqueue(value.Reference ?? document.AddObject(value));

                        break;

                    case PdfArray array:
                        foreach (var element in array.Elements)
                        {
                            pending.Push(element);
                        }

                        break;

                    case PdfDictionary dictionary:
                        foreach (var value in dictionary.Elements.Values)
                        {
                            pending.Push(value);
                        }

                        break;
                }
            }
        }

        return numbers;

        void Enqueue(PdfReference child)
        {
            if (numbers.TryAdd(child, numbers.Count + 1))
            {
                queue.Enqueue(child);
            }
        }
    }

    private static void WriteTopLevel(PdfOutput output, PdfItem item, ObjectContext context)
    {
        if (item is PdfDictionary { Stream: not null } stream)
        {
            var data = context.Security?.EncryptStream(stream, stream.Stream.Value, context.ObjectNumber) ?? stream.Stream.Value;

            WriteDictionary(output, stream, context, data.Length);
            output.Write("\nstream\n");
            output.Write(data);
            output.Write("\nendstream");

            return;
        }

        WriteItem(output, item, context, true);
    }

    private static void WriteItem(PdfOutput output, PdfItem item, ObjectContext context, bool topLevel)
    {
        switch (item)
        {
            case null:
            case PdfNull:
                output.Write("null");

                break;

            case PdfBoolean boolean:
                output.Write(boolean.Value
                    ? "true"
                    : "false");

                break;

            case PdfInteger number:
                output.Write(number.Value.ToString(CultureInfo.InvariantCulture));

                break;

            case PdfReal number:
                output.Write(PdfObjectModel.Format(number.Value));

                break;

            case PdfName name:
                WriteName(output, name.Value);

                break;

            case PdfString text:
                WriteString(output, context.Security?.EncryptString(text.Bytes, context.ObjectNumber) ?? text.Bytes, text.IsHex || context.Security is not null);

                break;

            case PdfRectangle rectangle:
                output.Write("[" + PdfObjectModel.Format(rectangle.X1) + " " + PdfObjectModel.Format(rectangle.Y1) + " " + PdfObjectModel.Format(rectangle.X2) + " " + PdfObjectModel.Format(rectangle.Y2) + "]");

                break;

            case PdfReference reference:
                WriteReference(output, reference, context);

                break;

            case PdfObject value when !topLevel && value.IsIndirect:
                WriteReference(output, value.Reference, context);

                break;

            case PdfArray array:
                output.Write("[");

                for (var index = 0; index < array.Elements.Count; index++)
                {
                    if (index > 0)
                    {
                        output.Write(" ");
                    }

                    WriteItem(output, array.Elements[index], context, false);
                }

                output.Write("]");

                break;

            case PdfDictionary dictionary:
                WriteDictionary(output, dictionary, context, null);

                break;

            default:
                output.Write("null");

                break;
        }
    }

    private static void WriteDictionary(PdfOutput output, PdfDictionary dictionary, ObjectContext context, int? streamLength)
    {
        output.Write("<<");

        foreach (var (key, value) in dictionary.Elements)
        {
            if (streamLength is not null && key == "/Length")
            {
                continue;
            }

            output.Write(" ");
            WriteName(output, key);
            output.Write(" ");
            WriteItem(output, value, context, false);
        }

        if (streamLength is not null)
        {
            output.Write(" /Length " + streamLength.Value.ToString(CultureInfo.InvariantCulture));
        }

        output.Write(" >>");
    }

    private static void WriteReference(PdfOutput output, PdfReference reference, ObjectContext context)
    {
        if (context.Numbers is null)
        {
            output.Write(reference.ObjectNumber.ToString(CultureInfo.InvariantCulture) + " " + reference.Generation.ToString(CultureInfo.InvariantCulture) + " R");

            return;
        }

        output.Write(context.Numbers.TryGetValue(reference, out var number)
            ? number.ToString(CultureInfo.InvariantCulture) + " 0 R"
            : "null");
    }

    private static void WriteName(PdfOutput output, string name)
    {
        var bytes = Encoding.UTF8.GetBytes(name.StartsWith('/') ? name[1..] : name);
        var builder = new StringBuilder("/", bytes.Length + 1);

        foreach (var value in bytes)
        {
            if (value is < 0x21 or > 0x7E or (byte)'#' or (byte)'(' or (byte)')' or (byte)'<' or (byte)'>' or (byte)'[' or (byte)']' or (byte)'{' or (byte)'}' or (byte)'/' or (byte)'%')
            {
                builder.Append('#').Append(value.ToString("X2", CultureInfo.InvariantCulture));
            }
            else
            {
                builder.Append((char)value);
            }
        }

        output.Write(builder.ToString());
    }

    private static void WriteString(PdfOutput output, byte[] bytes, bool hex)
    {
        if (hex || !bytes.All(value => value is >= 0x20 and <= 0x7E or (byte)'\n' or (byte)'\r' or (byte)'\t'))
        {
            output.Write("<" + Convert.ToHexString(bytes) + ">");

            return;
        }

        var builder = new StringBuilder("(", bytes.Length + 2);

        foreach (var value in bytes)
        {
            switch (value)
            {
                case (byte)'(':
                case (byte)')':
                case (byte)'\\':
                    builder.Append('\\').Append((char)value);

                    break;

                case (byte)'\n':
                    builder.Append("\\n");

                    break;

                case (byte)'\r':
                    builder.Append("\\r");

                    break;

                case (byte)'\t':
                    builder.Append("\\t");

                    break;

                default:
                    builder.Append((char)value);

                    break;
            }
        }

        output.Write(builder.Append(')').ToString());
    }

    /// <summary>
    /// What writing one object needs: the new object numbers, and the encryption for the object.
    /// </summary>
    /// <param name="Numbers">The number each written object gets, or <see langword="null"/> to keep the numbers it has.</param>
    /// <param name="Security">The encryption, or <see langword="null"/>.</param>
    /// <param name="ObjectNumber">The number of the object being written.</param>
    private readonly record struct ObjectContext(Dictionary<PdfReference, int> Numbers, IPdfObjectEncryptor Security, int ObjectNumber);

    /// <summary>
    /// The bytes being written, and where the next one goes.
    /// </summary>
    private sealed class PdfOutput
    {
        private readonly MemoryStream _buffer = new();

        /// <summary>
        /// Gets the number of bytes written.
        /// </summary>
        public long Position => _buffer.Position;

        /// <summary>
        /// Writes text, every character as one byte.
        /// </summary>
        /// <param name="text">The text.</param>
        public void Write(string text)
        {
            _buffer.Write(Encoding.Latin1.GetBytes(text));
        }

        /// <summary>
        /// Writes bytes.
        /// </summary>
        /// <param name="bytes">The bytes.</param>
        public void Write(byte[] bytes)
        {
            _buffer.Write(bytes);
        }

        /// <summary>
        /// Returns everything written.
        /// </summary>
        /// <returns>The bytes.</returns>
        public byte[] ToArray()
        {
            return _buffer.ToArray();
        }
    }
}
