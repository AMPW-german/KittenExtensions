
using System;

namespace StarMap.API;

[AttributeUsage(AttributeTargets.Class)]
internal class StarMapModAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Method)]
internal class StarMapBeforeMainAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Method)]
internal class StarMapImmediateLoadAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Method)]
internal class StarMapAllModsLoadedAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Method)]
internal class StarMapUnloadAttribute : Attribute { }