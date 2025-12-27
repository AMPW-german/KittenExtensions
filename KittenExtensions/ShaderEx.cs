
using System;
using System.Collections.Generic;
using System.Xml.Serialization;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;
using Core;
using KSA;

namespace KittenExtensions;

[KxAsset("ShaderEx")]
[KxAssetInject(typeof(GaugeComponent), nameof(GaugeComponent.FragmentShader), "FragmentEx")]
public class ShaderEx : ShaderReference, IBinder
{
  [XmlElement("TextureBinding", typeof(TextureBindingReference))]
  public List<SerializedId> XmlBindings = [];

  [XmlIgnore]
  public List<IShaderBinding> Bindings;

  public override void OnDataLoad(Mod mod)
  {
    base.OnDataLoad(mod);
    foreach (var binding in XmlBindings)
      binding.OnDataLoad(mod);

    ModLibrary.RegisterBinder(this);
  }

  public void Bind(Renderer renderer, StagingPool stagingPool)
  {
    // FileReference already implements ILoader and uses an internal virtual method we can't override
    // Bind happens after Load, so all the bindings should be ready here as well
    Bindings = new(XmlBindings.Count);
    foreach (var binding in XmlBindings)
      Bindings.Add(((IShaderBinding)binding).Get());
  }

  public static DescriptorPoolEx GaugeCreateDescriptorPool(
    Device device, DescriptorPoolEx.CreateInfo createInfo, VkAllocator allocator, GaugeComponent component)
  {
    if (component.FragmentShader.Get() is not ShaderEx frag)
      return device.CreateDescriptorPool(createInfo, allocator);

    // add room in descriptor pool for new bindings
    Span<VkDescriptorPoolSize> poolSizes = stackalloc VkDescriptorPoolSize[TYPE_COUNT];
    for (var i = 0; i < TYPE_COUNT; i++)
      poolSizes[i] = new VkDescriptorPoolSize { Type = DESCRIPTOR_TYPES[i], DescriptorCount = 0 };

    // always have gauge font atlas
    poolSizes[TypeIndex(VkDescriptorType.CombinedImageSampler)].DescriptorCount = 1;

    foreach (var binding in frag.Bindings)
      poolSizes[TypeIndex(binding.DescriptorType)].DescriptorCount += binding.DescriptorCount;


    var nonZero = 0;
    for (var i = 0; i < TYPE_COUNT; i++)
      if (poolSizes[i].DescriptorCount > 0)
        poolSizes[nonZero++] = poolSizes[i];

    poolSizes = poolSizes[..nonZero];

    return device.CreateDescriptorPool(createInfo with { PoolSizes = poolSizes }, allocator);
  }

  public static DescriptorSetLayoutEx GaugeCreateDescriptorSetLayout(
    Device device, scoped DescriptorSetLayoutEx.CreateInfo createInfo, VkAllocator allocator, GaugeComponent component)
  {
    if (component.FragmentShader.Get() is not ShaderEx frag)
      return device.CreateDescriptorSetLayout(createInfo, allocator);

    var bindingCount = frag.Bindings.Count;
    Span<VkDescriptorSetLayoutBinding> bindings = stackalloc VkDescriptorSetLayoutBinding[1 + bindingCount];
    bindings[0] = createInfo.Bindings[0];
    for (var i = 0; i < bindingCount; i++)
    {
      var binding = frag.Bindings[i];
      bindings[i + 1] = new VkDescriptorSetLayoutBinding
      {
        Binding = i + 1,
        DescriptorType = binding.DescriptorType,
        DescriptorCount = binding.DescriptorCount,
        StageFlags = VkShaderStageFlags.FragmentBit,
      };
    }
    createInfo.Bindings = bindings;
    return device.CreateDescriptorSetLayout(createInfo, allocator);
  }

  public static unsafe void GaugeUpdateDescriptorSets(
    Device device,
    ReadOnlySpan<VkWriteDescriptorSet> pDescriptorWrites,
    ReadOnlySpan<VkCopyDescriptorSet> pDescriptorCopies,
    GaugeComponent component)
  {
    if (component.FragmentShader.Get() is not ShaderEx frag)
    {
      device.UpdateDescriptorSets(pDescriptorWrites, pDescriptorCopies);
      return;
    }

    var bindingCount = frag.Bindings.Count;
    Span<int> writeCounts = [0, 0, 0];
    foreach (var binding in frag.Bindings)
      writeCounts[(int)TypeWriteType(binding.DescriptorType)] += binding.DescriptorCount;

    VkDescriptorImageInfo* imageInfos =
      stackalloc VkDescriptorImageInfo[writeCounts[(int)WriteType.ImageInfo]];
    VkDescriptorBufferInfo* bufferInfos =
      stackalloc VkDescriptorBufferInfo[writeCounts[(int)WriteType.BufferInfo]];
    VkBufferView* texelBufferViews =
      stackalloc VkBufferView[writeCounts[(int)WriteType.TexelBufferView]];

    Span<VkWriteDescriptorSet> writes = stackalloc VkWriteDescriptorSet[bindingCount + 1];
    Span<int> writeIndices = [0, 0, 0];

    writes[0] = pDescriptorWrites[0];
    var dset = writes[0].DstSet;

    for (var i = 0; i < bindingCount; i++)
    {
      var binding = frag.Bindings[i];
      var wtype = TypeWriteType(binding.DescriptorType);
      var start = writeIndices[(int)wtype];
      var count = binding.DescriptorCount;

      writes[i + 1] = new VkWriteDescriptorSet
      {
        DescriptorType = binding.DescriptorType,
        DstBinding = i + 1,
        DescriptorCount = count,
        DstSet = dset,
        ImageInfo = &imageInfos[writeIndices[(int)WriteType.ImageInfo]],
        BufferInfo = &bufferInfos[writeIndices[(int)WriteType.BufferInfo]],
        TexelBufferView = &texelBufferViews[writeIndices[(int)WriteType.TexelBufferView]],
      };

      binding.WriteDescriptors(wtype switch
      {
        WriteType.ImageInfo => new() { ImageInfo = new(&imageInfos[start], count) },
        WriteType.BufferInfo => new() { BufferInfo = new(&bufferInfos[start], count) },
        WriteType.TexelBufferView => new() { TexelBufferView = new(&texelBufferViews[start], count) },
        _ => throw new InvalidOperationException($"{wtype}"),
      });

      writeIndices[(int)wtype] += count;
    }

    device.UpdateDescriptorSets(writes, pDescriptorCopies);
  }

  private const int TYPE_COUNT = 2;
  private static readonly VkDescriptorType[] DESCRIPTOR_TYPES =
  [
    VkDescriptorType.CombinedImageSampler,
    VkDescriptorType.UniformBufferDynamic,
  ];
  private static int TypeIndex(VkDescriptorType type) => type switch
  {
    VkDescriptorType.CombinedImageSampler => 0,
    VkDescriptorType.UniformBufferDynamic => 1,
    _ => throw new NotSupportedException($"{type}"),
  };

  private enum WriteType { ImageInfo = 0, BufferInfo = 1, TexelBufferView = 2 }
  private static WriteType TypeWriteType(VkDescriptorType type) => type switch
  {
    VkDescriptorType.CombinedImageSampler => WriteType.ImageInfo,
    VkDescriptorType.UniformBufferDynamic => WriteType.BufferInfo,
    _ => throw new NotSupportedException($"{type}"),
  };
}
