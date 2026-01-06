
using System;
using System.Xml;

namespace KittenExtensions.Patch;

public ref struct LineBuilder(Span<char> buf)
{
  private readonly Span<char> buf = buf;
  private int length = 0;

  public ReadOnlySpan<char> Line => buf[..length];

  public void Clear() => length = 0;

  public void Add(ReadOnlySpan<char> data)
  {
    data.CopyTo(buf[length..]);
    length += data.Length;
  }

  public void Add(char c)
  {
    buf[length++] = c;
  }

  public void Add<T>(T val, ReadOnlySpan<char> fmt = "g") where T : ISpanFormattable
  {
    val.TryFormat(buf[length..], out var len, fmt, null);
    length += len;
  }
}

public ref struct XmlDisplayBuilder(Span<char> buf)
{
  private LineBuilder line = new(buf);

  public ReadOnlySpan<char> Line => line.Line;

  public void Reset() =>
      line.Clear();

  public void NodeInline(XmlNode node)
  {
    if (node is XmlElement el)
      ElementInline(el);
    else if (node is XmlText text)
      TextInline(text);
    else if (node is XmlComment comment)
      CommentInline(comment);
    else if (node is XmlProcessingInstruction proc)
      ProcInline(proc);
    else
      throw new NotImplementedException($"{node.GetType().Name}");
  }

  public void ElementInline(XmlElement el)
  {
    ElOpenStart(el.Name);
    ElAttrsInline(el.Attributes);

    if (!el.HasChildNodes)
    {
      ElEnd(true);
      return;
    }

    ElEnd();

    var children = el.ChildNodes;
    for (var i = 0; i < children.Count; i++)
      NodeInline(children[i]);

    ElementClose(el.Name);
  }

  public void TextInline(XmlText text)
  {
    line.Add(text.Value);
  }

  public void CommentInline(XmlComment comment)
  {
    line.Add("<!--");
    line.Add(comment.Value);
    line.Add("-->");
  }

  public void ProcInline(XmlProcessingInstruction proc)
  {
    line.Add("<?");
    line.Add(proc.Name);
    line.Add(' ');
    line.Add(proc.Value);
    line.Add("?>");
  }

  public void ElementOpen(XmlElement el, bool selfClose = false)
  {
    ElOpenStart(el.Name);
    ElAttrsInline(el.Attributes);
    ElEnd(selfClose);
  }

  public void ElementClose(ReadOnlySpan<char> name)
  {
    line.Add("</");
    line.Add(name);
    line.Add('>');
  }

  private void ElOpenStart(ReadOnlySpan<char> name)
  {
    line.Add('<');
    line.Add(name);
  }

  private void ElEnd(bool selfClose = false)
  {
    if (selfClose)
      line.Add(" />");
    else
      line.Add('>');
  }

  private void ElAttrsInline(XmlAttributeCollection attrs)
  {
    for (var i = 0; i < attrs.Count; i++)
    {
      var attr = attrs[i];
      line.Add(' ');
      ElAttr(attr.Name, attr.Value);
    }
  }

  private void ElAttr(ReadOnlySpan<char> name, ReadOnlySpan<char> value)
  {
    line.Add(name);
    line.Add("=\"");
    line.Add(value);
    line.Add('"');
  }
}