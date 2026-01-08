
using System.Collections.Generic;
using System.Xml;
using System.Xml.XPath;

namespace KittenExtensions.Patch;

public abstract class OpExecContext(XPathNavigator Nav)
{
  public readonly XPathNavigator Nav = Nav;

  public abstract OpExecContext WithPatch(DeserializedPatch patch);
  public abstract OpExecContext WithNav(XPathNavigator nav);
  public abstract OpExecution Execution(XmlOp op);
  public abstract OpAction Action(
    OpActionType Type, XmlNode Target, object Source = null, OpPosition Pos = OpPosition.Default);

  public abstract void End();
}

public class DefaultOpExecContext(XPathNavigator Nav) : OpExecContext(Nav)
{
  private DeserializedPatch patch = null;
  private XmlOp op = null;

  public override OpExecContext WithPatch(DeserializedPatch patch) =>
    new DefaultOpExecContext(Nav) { patch = patch };
  public override OpExecContext WithNav(XPathNavigator nav) =>
    new DefaultOpExecContext(nav) { patch = patch };

  public override OpExecution Execution(XmlOp op) =>
    new(op, new DefaultOpExecContext(Nav) { patch = patch, op = op });
  public override OpAction Action(
    OpActionType Type, XmlNode Target, object Source = null, OpPosition Pos = OpPosition.Default
  ) => new(op, this, Type, Target, Source, Pos);

  public override void End() { }
}

public enum ContextType
{
  Root,
  Patch,
  Nav,
  Exec,
  Action,
}

public class DebugOpExecContext : OpExecContext
{
  public readonly DebugOpExecContext Parent;
  public readonly ContextType Type;
  private DeserializedPatch patch;
  private OpExecution exec;
  private OpAction action;
  public readonly List<DebugOpExecContext> Children = [];

  public DeserializedPatch ContextPatch => patch;
  public OpExecution ContextExec => exec;
  public OpAction ContextAction => action;

  public bool Ended { get; set; } = false;

  public static DebugOpExecContext NewRoot(XPathNavigator nav) => new(nav);

  private DebugOpExecContext(XPathNavigator nav) : base(nav)
  {
    Type = ContextType.Root;
    Parent = null;
    patch = null;
    exec = null;
    action = null;
  }

  private DebugOpExecContext(ContextType type, DebugOpExecContext parent, XPathNavigator nav) : base(nav)
  {
    Type = type;
    Parent = parent;
    patch = parent.patch;
    exec = parent.exec;
    action = null;
  }

  public override OpExecContext WithPatch(DeserializedPatch patch)
  {
    var child = new DebugOpExecContext(ContextType.Patch, this, Nav) { patch = patch };
    Children.Add(child);
    return child;
  }

  public override OpExecContext WithNav(XPathNavigator nav)
  {
    var child = new DebugOpExecContext(ContextType.Nav, this, nav);
    Children.Add(child);
    return child;
  }

  public override OpExecution Execution(XmlOp op)
  {
    var child = new DebugOpExecContext(ContextType.Exec, this, Nav);
    child.exec = new(op, child);
    Children.Add(child);
    return child.exec;
  }

  public override OpAction Action(
    OpActionType Type, XmlNode Target,
    object Source = null, OpPosition Pos = OpPosition.Default)
  {
    var child = new DebugOpExecContext(ContextType.Action, this, Nav);
    child.action = new(child.exec.Op, child, Type, Target, Source, Pos);
    Children.Add(child);
    return child.action;
  }

  public override void End() => Ended = true;
}