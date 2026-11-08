# KSA Content Loader Migration

## Objective

Restore KittenExtensions' XML patching against KSA v2026.11.10.5801 by integrating its existing XPathPatch document with KSA's unified content-loading pipeline. Keep KX's broader patch language and debugger; do not replace it with KSA's `Modify` behavior. Ensure KX changes are present before KSA's stock merge and deserialization stages.

## Findings and Constraints

- KSA now reads the enabled mods' `assets` files into `ContentSource.Rows`. Each file must have an `<Assets>` root; rows are stamped with source mod, file, and ordering metadata.
- `ContentLoader.ReadSource` completes before `ContentPrePass.Resolve` performs KSA's native row and child merges. `ModLibrary.PrepareAll` still runs before that read and remains a suitable place to load and execute KX patches.
- The old `XmlLoader` and `Mod.Load*` transpiler targets are absent. `ContentSerializerCache` is the stock serializer path, keyed by the content tag table's overrides, type, and tag.
- KSA's `Mod` now exposes the unified `assets` paths, not the old separate systems and planet-mesh XML lists.
- Existing `KxAsset` registration targets `AssetBundle.Assets`. The new loader instead binds individual top-level XML tags in `GameContentTags`; custom top-level asset support therefore needs an explicit compatibility design, not only a serializer override.
- Do not alter the pre-existing staged `tooling` submodule change.

## Proposed Changes

- [ ] Replace the obsolete `XmlLoaderPatch` and `Mod.Load*` transpilers in `KittenExtensions/Patches.cs` with a transformation at the `ContentLoader.ReadSource` boundary. Keep KX patch loading and execution ahead of KSA's stock pre-pass.
- [ ] Update mod and file discovery in `KittenExtensions/Patch/Patcher.cs` for the current KSA schema: consume unified `assets` files, retain KX's own patch-file declarations, and respect enabled-mod manifest order. Do not treat KX patch files as game assets.
- [ ] Adapt the KX-to-KSA handoff so the patched XML rows populate `ContentSource.Rows` before KSA's pre-pass. Preserve row ordering, source ownership, file and row attribution, and deterministic precedence for original, modified, added, and removed rows. Reuse valid KSA source stamps where possible and assign equivalent attribution to KX-created rows.
- [ ] Handle the fact that the XPP document groups rows by mod and file while KSA consumes a flat row list. Define and verify the mapping between each patched document row and its source mod, asset path, and manifest/file/row indexes; avoid losing source attribution when applying file-level XPath operations.
- [ ] Update custom XML extension registration in `KittenExtensions/AssetEx.cs` for the per-`ContentTagTable` serializer overrides built by KSA. Preserve nested-element injection where possible, and determine how registered KX custom top-level tags are bound and exposed under the new loader rather than relying on `AssetBundle.Assets`.
- [ ] Remove obsolete serializer-cache and save-serializer replacement assumptions only after confirming they are not needed by the updated content path or save-data extensions.
- [ ] Update `README.md` to describe the current KSA asset-file layout and the KX-before-stock-patching order, without implying that KX adopts KSA's narrower merge behavior.

## Verification

- [ ] Build the solution against the installed KSA v2026.11.10.5801 assemblies with mod packaging disabled; confirm there are no references to removed loader types or methods.
- [ ] Run the existing XPathPatch test suite using `dotnet test XPathPatch/XPP.Tests/XPathPatch.Tests.csproj`.
- [ ] Add focused regression coverage for enabled-mod ordering, file/row ordering, patch additions and removals, and preservation of source attribution in the flattened row list.
- [ ] Verify the complete KX-to-KSA flow: KX patch execution precedes KSA `ContentPrePass`, and the same patched source is used for preload and full load without applying KX patches twice.
- [ ] Smoke-test in KSA with KX debug mode enabled. Confirm the debug document reflects the intended KX input and that patched rows survive KSA deserialization, registration, reference resolution, and loading without content errors.

## Acceptance Criteria

- KX's existing XPath patch operations and debugger continue to operate on the unified asset data.
- KX changes reach KSA before its native `Modify` pre-pass and remain available to both preload and full-load passes.
- Ordering and mod/file attribution remain deterministic, including for rows created or removed by KX patches.
- Existing custom asset and injection APIs have an explicit compatibility outcome; unsupported behavior is documented rather than silently dropped.
- The solution builds against the updated KSA assemblies, the XPathPatch tests pass, and the in-game smoke test completes without new content-loading errors.

## References

- `KittenExtensions/Patches.cs` — current lifecycle hooks and obsolete XML-loader transpiler.
- `KittenExtensions/Patch/Patcher.cs` and `KittenExtensions/Patch/Patcher.Debug.cs` — patch document loading, execution, and debugger flow.
- `KittenExtensions/AssetEx.cs` and `KittenExtensions/Attributes.cs` — custom XML serializer extension registration.
- `README.md` — documented mod XML and patch behavior.
- `XPathPatch/XPP/Patch/Domain.cs` — mod/file document model used by KX.
- KSA v2026.11.10.5801: `ModLibrary.PrepareAll`, `ContentLoader.ReadSource`, `ContentLoader.Load`, `ContentPrePass.Resolve`, `ContentStamps`, `ContentSerializerCache`, `GameContentTags.Build`, and `XmlHelper.BuildAttributeOverrides`.
- KSA developer article, “XML Data Changes: More tools for Modders”: https://ahwoo.com/news/1808420/kitten-space-agency/xml-data-changes-more-tools-for-modders
