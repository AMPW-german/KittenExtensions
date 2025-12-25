
using System;

#pragma warning disable CS9113

namespace KittenExtensions;

[AttributeUsage(AttributeTargets.Class)]
internal class KxAssetAttribute(string xmlElement) : Attribute;

[AttributeUsage(AttributeTargets.Class)]
internal class KxAssetInjectAttribute(Type parent, string member, string xmlElement) : Attribute;