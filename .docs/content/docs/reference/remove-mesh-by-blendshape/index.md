---
title: Remove Mesh By BlendShape
weight: 25
---

# Remove Mesh By BlendShape

Remove vertices transformed by specified BlendShape and their polygons.

This component should be added to a GameObject which has a SkinnedMeshRenderer component. (Kind: [Modifying Edit Skinned Mesh Component](../../component-kind/edit-skinned-mesh-components#modifying-component))

## Benefits

By removing polygons which are hidden by clothes or something, you can reduce rendering cost, BlendShape processing cost, etc. without affecting the appearance so much.
You can use this component to easily remove polygons with BlendShapes for shrinking parts of the body, which many avatars have.

## Settings

![component.png](component.png)

You'll see list of blend shapes and check to select blendShapes.
If some vertices in your mesh is moved more than `Tolerance` by selected BlendShape, this component will remove the vertices. 
