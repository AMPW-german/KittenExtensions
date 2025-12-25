
using System;
using System.Collections.Generic;
using System.Xml.Serialization;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;
using KSA;

namespace KittenExtensions;

[KxAsset("ShaderEx")]
[KxAssetInject(typeof(GaugeComponent), nameof(GaugeComponent.FragmentShader), "FragmentEx")]
public class ShaderEx : ShaderReference
{
  [XmlElement("TextureBinding", typeof(TextureReference))]
  public List<TextureReference> TextureBindings = [];

  public override void OnDataLoad(Mod mod)
  {
    base.OnDataLoad(mod);
    foreach (var tex in TextureBindings)
      tex.OnDataLoad(mod);
  }

  public static DescriptorPoolEx GaugeCreateDescriptorPool(
    Device device, DescriptorPoolEx.CreateInfo createInfo, VkAllocator allocator, GaugeComponent component)
  {
    if (component.FragmentShader.Get() is ShaderEx frag)
    {
      // add room in descriptor pool for new textures
      var texCount = frag.TextureBindings.Count;
      createInfo.PoolSizes[0].DescriptorCount += texCount;
    }
    return device.CreateDescriptorPool(createInfo, allocator);
  }

  public static DescriptorSetLayoutEx GaugeCreateDescriptorSetLayout(
    Device device, scoped DescriptorSetLayoutEx.CreateInfo createInfo, VkAllocator allocator, GaugeComponent component)
  {
    if (component.FragmentShader.Get() is not ShaderEx frag)
      return device.CreateDescriptorSetLayout(createInfo, allocator);

    // add extra bindings for new textures
    var texCount = frag.TextureBindings.Count;
    Span<VkDescriptorSetLayoutBinding> bindings = stackalloc VkDescriptorSetLayoutBinding[1 + texCount];
    bindings[0] = createInfo.Bindings[0];
    for (var i = 0; i < texCount; i++)
    {
      bindings[i + 1] = new VkDescriptorSetLayoutBinding
      {
        Binding = i + 1,
        DescriptorType = VkDescriptorType.CombinedImageSampler,
        DescriptorCount = 1,
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

    // write bindings for new textures
    var texCount = frag.TextureBindings.Count;
    Span<VkWriteDescriptorSet> writes = stackalloc VkWriteDescriptorSet[texCount + 1];
    VkDescriptorImageInfo* images = stackalloc VkDescriptorImageInfo[texCount];

    writes[0] = pDescriptorWrites[0];
    var sampler = writes[0].ImageInfo->Sampler;
    var dset = writes[0].DstSet;

    for (var i = 0; i < texCount; i++)
    {
      images[i] = new VkDescriptorImageInfo
      {
        ImageView = frag.TextureBindings[i].ImageView,
        ImageLayout = VkImageLayout.ShaderReadOnlyOptimal,
        Sampler = sampler,
      };
      writes[i + 1] = new VkWriteDescriptorSet
      {
        DescriptorType = VkDescriptorType.CombinedImageSampler,
        DstBinding = i + 1,
        DescriptorCount = 1,
        DstSet = dset,
        ImageInfo = &images[i]
      };
    }

    device.UpdateDescriptorSets(writes, pDescriptorCopies);
  }
}
