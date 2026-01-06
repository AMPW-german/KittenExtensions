
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Xml;
using System.Xml.Serialization;
using System.Xml.XPath;

namespace KittenExtensions.Patch;

public abstract class XmlOp
{
  public enum Position
  {
    Default,
    Replace,
    Merge,
    Append,
    Prepend,
    Before,
    After
  }

  public const string MergeIdAttr = "_MergeId";
  public const string DefaultMergeId = "Id";
  public const string AnyMergeId = "*";
  public const string NoneMergeId = "-";
  public const string MergePosAttr = "_MergePos";

  [XmlAttribute("Path")]
  public string Path = ".";

  [XmlIgnore]
  public XmlElement Element;

  public abstract void Execute(XPathNavigator nav);

  protected static XmlNode ToNode(XmlNode cur, object child) => child is string strChild
    ? cur.OwnerDocument.CreateTextNode(strChild)
    : cur.OwnerDocument.ImportNode((XmlNode)child, true);

  public static void Merge(XmlElement source, XmlElement target)
  {
    var attrs = source.Attributes;
    for (var i = 0; i < attrs.Count; i++)
    {
      var attr = attrs[i];
      if (attr.Name is MergePosAttr or MergeIdAttr)
        continue;
      target.SetAttribute(attr.Name, attr.Value);
    }

    var mergePos = Position.Append;
    if (source.GetAttribute(MergePosAttr) is string posStr && posStr != "")
    {
      if (!Enum.TryParse(posStr, out mergePos))
        throw new InvalidOperationException($"Invalid {MergePosAttr} '{posStr}'");

      mergePos = mergePos switch
      {
        Position.Append or Position.Prepend => mergePos,
        _ => throw new InvalidOperationException($"Invalid {MergePosAttr} '{mergePos}'"),
      };
    }

    var inserter = new Inserter(target, mergePos);

    var children = source.ChildNodes;
    for (var i = 0; i < children.Count; i++)
    {
      switch (children[i])
      {
        case XmlText text:
          inserter.Insert(target.OwnerDocument.ImportNode(text, true));
          break;
        case XmlElement el:
          string idAttr = GetMergeId(el);
          var id = el.GetAttribute(idAttr);
          if (FindChildElement(target, el.Name, idAttr, id) is XmlElement tgtEl)
            Merge(el, tgtEl);
          else
            inserter.Insert(target.OwnerDocument.ImportNode(el, true));
          break;
        default:
          throw new InvalidOperationException($"Invalid merge child type {children[i].GetType().Name}");
      }
    }
  }

  private static string GetMergeId(XmlElement el)
  {
    var id = el.GetAttribute(MergeIdAttr);
    if (!string.IsNullOrEmpty(id))
      return id;
    if (el.HasAttribute(DefaultMergeId))
      return DefaultMergeId;
    return AnyMergeId;
  }

  private static XmlElement FindChildElement(XmlElement parent, string name, string idAttr, string id)
  {
    if (idAttr == NoneMergeId)
      return null;
    var children = parent.ChildNodes;
    var isAny = idAttr == AnyMergeId;
    for (var i = 0; i < children.Count; i++)
    {
      if (children[i] is not XmlElement el)
        continue;
      if (el.Name != name)
        continue;
      if (isAny || el.GetAttribute(idAttr) == id)
        return el;
    }
    return null;
  }

  public struct Inserter(XmlElement el, Position pos)
  {
    private readonly XmlElement el = el;
    private readonly Position pos = pos;
    private XmlNode last;

    public void Insert(XmlNode node)
    {
      switch (pos)
      {
        case Position.Replace:
        case Position.Append:
          el.AppendChild(node);
          break;
        case Position.Prepend:
          el.InsertAfter(node, last);
          last = node;
          break;
        case Position.After:
          el.ParentNode.InsertAfter(node, last ?? el);
          last = node;
          break;
        case Position.Before:
          if (last == null)
            el.ParentNode.InsertBefore(node, el);
          else
            el.InsertAfter(node, last);
          last = node;
          break;
        default:
          throw new InvalidOperationException($"{pos}");
      }
    }
  }
}

public class XmlOpCollection : XmlOp
{
  [XmlElement("Update", typeof(XmlUpdateOp))]
  [XmlElement("Delete", typeof(XmlDeleteOp))]
  [XmlElement("Copy", typeof(XmlCopyOp))]
  [XmlElement("If", typeof(XmlIfOp))]
  [XmlElement("IfAny", typeof(XmlIfAnyOp))]
  [XmlElement("IfNone", typeof(XmlIfNoneOp))]
  [XmlElement("With", typeof(XmlWithOp))]
  public List<XmlOp> Ops;

  public override void Execute(XPathNavigator nav)
  {
    foreach (var op in Ops)
      op.Execute(nav);
  }
}

[XmlRoot("Patch")]
public class XmlPatch : XmlOpCollection
{
  // TODO: priority? order? something to allow running before and after other mods patches
}

public abstract class XmlChildrenOp : XmlOp
{
  [XmlText(typeof(string))]
  [XmlAnyElement]
  public List<object> Children = [];

  public string StringValue
  {
    get
    {
      if (Children.Count == 0)
        return "";
      if (Children.Count > 1 || Children[0] is not string strVal)
        throw new InvalidOperationException($"Contents are not string for {GetType().Name} '{Path}'");
      return strVal;
    }
  }
}

public class XmlOpElementPopulator
{
  public static void Populate(XmlElement element, object op)
  {
    if (op == null)
      return;
    Get(op.GetType())?.Populate(element, op, [], 0);
  }

  private static readonly Dictionary<Type, XmlOpElementPopulator> popByType = [];

  private static XmlOpElementPopulator Get(Type type)
  {
    if (type == null)
      return null;
    if (popByType.TryGetValue(type, out var pop))
      return pop;

    if (type.IsAssignableTo(typeof(XmlOp)))
    {
      pop = popByType[type] = new();

      foreach (var field in type.GetFields(
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.FlattenHierarchy))
      {
        if (!field.FieldType.IsAssignableTo(typeof(XmlOp)) && !IsList(field.FieldType))
          continue;
        var hasAttr = false;
        foreach (var attr in field.GetCustomAttributes())
        {
          if (attr is XmlAnyElementAttribute)
          {
            pop.anyField = field;
            hasAttr = true;
          }
          else if (attr is XmlElementAttribute elAttr)
          {
            var name = elAttr.ElementName;
            if (string.IsNullOrEmpty(name))
              name = field.Name;
            pop.opFields.Add(name, field);
            hasAttr = true;
          }
        }
        if (!hasAttr)
          pop.opFields.Add(field.Name, field);
      }

      return pop;
    }
    else if (IsList(type))
      return popByType[type] = new() { isList = true };
    else
      return null;
  }

  private static bool IsList(Type type) =>
    type.IsConstructedGenericType && type.GetGenericTypeDefinition() == typeof(List<>);

  private bool isList = false;
  private FieldInfo anyField;
  private readonly Dictionary<string, FieldInfo> opFields = [];

  private XmlOpElementPopulator() { }

  private void Populate(XmlElement element, object obj, Dictionary<(XmlNode, FieldInfo), int> counts, int listPos)
  {
    if (obj == null)
      return;

    if (isList)
    {
      var list = (IList)obj;
      obj = list[listPos];
      Get(obj?.GetType())?.Populate(element, list[listPos], counts, 0);
      return;
    }

    if (obj is not XmlOp op)
      return;

    op.Element = element;

    var children = element.ChildNodes;
    for (var i = 0; i < children.Count; i++)
    {
      if (children[i] is not XmlElement child)
        continue;

      if (!opFields.TryGetValue(child.Name, out var field) && anyField == null)
        continue;
      field ??= anyField;

      var lkey = (element, field);
      var lpos = counts.GetValueOrDefault(lkey);
      counts[lkey] = lpos + 1;

      Get(field.FieldType)?.Populate(child, field.GetValue(obj), counts, lpos);
    }
  }
}