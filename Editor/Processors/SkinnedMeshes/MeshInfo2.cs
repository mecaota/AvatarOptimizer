#if true
using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Rendering;

namespace Anatawa12.AvatarOptimizer.Processors.SkinnedMeshes
{
    internal class MeshInfo2
    {
        public Bounds Bounds;
        public readonly List<Vertex> Vertices = new List<Vertex>(0);

        // TexCoordStatus which is 3 bits x 8 = 24 bits
        private ushort _texCoordStatus;

        public readonly List<SubMesh> SubMeshes = new List<SubMesh>(0);

        public readonly List<(string name, float weight)> BlendShapes = new List<(string name, float weight)>(0);

        public readonly List<Bone> Bones = new List<Bone>();

        public bool HasColor { get; set; }

        public MeshInfo2(SkinnedMeshRenderer renderer)
        {
            var mesh = renderer.sharedMesh ? renderer.sharedMesh : new Mesh();
            ReadSkinnedMesh(mesh);

            for (var i = 0; i < mesh.blendShapeCount; i++)
                BlendShapes[i] = (BlendShapes[i].name, renderer.GetBlendShapeWeight(i));

            var sourceMaterials = renderer.sharedMaterials;
            var materialCount = Math.Min(sourceMaterials.Length, SubMeshes.Count);
            for (var i = 0; i < materialCount; i++)
                SubMeshes[i].SharedMaterial = sourceMaterials[i];

            var bones = renderer.bones;
            for (var i = 0; i < bones.Length; i++) Bones[i].Transform = bones[i];
        }

        public MeshInfo2(MeshRenderer renderer)
        {
            var mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
            ReadStaticMesh(mesh);

            Bones.Add(new Bone(Matrix4x4.identity, renderer.transform));

            foreach (var vertex in Vertices)
                vertex.BoneWeights.Add((Bones[0], 1f));

            var sourceMaterials = renderer.sharedMaterials;
            var materialCount = Math.Min(sourceMaterials.Length, SubMeshes.Count);
            for (var i = 0; i < materialCount; i++)
                SubMeshes[i].SharedMaterial = sourceMaterials[i];
        }

        public void ReadSkinnedMesh(Mesh mesh)
        {
            ReadStaticMesh(mesh);

            Bones.Clear();
            Bones.Capacity = Math.Max(Bones.Capacity, mesh.bindposes.Length);
            Bones.AddRange(mesh.bindposes.Select(x => new Bone(x)));

            var bonesPerVertex = mesh.GetBonesPerVertex();
            var allBoneWeights = mesh.GetAllBoneWeights();
            var bonesBase = 0;
            for (var i = 0; i < bonesPerVertex.Length; i++)
            {
                int count = bonesPerVertex[i];
                Vertices[i].BoneWeights.Capacity = count;
                foreach (var boneWeight1 in allBoneWeights.AsReadOnlySpan().Slice(bonesBase, count))
                    Vertices[i].BoneWeights.Add((Bones[boneWeight1.boneIndex], boneWeight1.weight));
                bonesBase += count;
            }

            BlendShapes.Clear();
            var deltaVertices = new Vector3[Vertices.Count];
            var deltaNormals = new Vector3[Vertices.Count];
            var deltaTangents = new Vector3[Vertices.Count];
            for (var i = 0; i < mesh.blendShapeCount; i++)
            {
                Assert.AreEqual(1, mesh.GetBlendShapeFrameCount(i));
                Assert.AreEqual(100.0f, mesh.GetBlendShapeFrameWeight(i, 0));

                mesh.GetBlendShapeFrameVertices(i, 0, deltaVertices, deltaNormals, deltaTangents);

                var shapeName = mesh.GetBlendShapeName(i);

                BlendShapes.Add((shapeName, 0.0f));

                for (var j = 0; j < deltaNormals.Length; j++)
                    Vertices[i].BlendShapes[shapeName] = (deltaVertices[j], deltaNormals[j], deltaTangents[j]);
            }
        }

        public void ReadStaticMesh(Mesh mesh)
        {
            Vertices.Capacity = Math.Max(Vertices.Capacity, mesh.vertexCount);
            Vertices.Clear();
            for (var i = 0; i < mesh.vertexCount; i++) Vertices.Add(new Vertex());

            CopyVertexAttr(mesh.vertices, (x, v) => x.Position = v);
            CopyVertexAttr(mesh.normals, (x, v) => x.Normal = v);
            CopyVertexAttr(mesh.tangents, (x, v) => x.Tangent = v);
            if (mesh.GetVertexAttributeDimension(VertexAttribute.Color) != 0)
            {
                HasColor = true;
                CopyVertexAttr(mesh.colors32, (x, v) => x.Color = v);
            }

            var uv2 = new List<Vector2>(0);
            var uv3 = new List<Vector3>(0);
            var uv4 = new List<Vector4>(0);
            for (var index = 0; index <= 7; index++)
            {
                // ReSharper disable AccessToModifiedClosure
                switch (mesh.GetVertexAttributeDimension(VertexAttribute.TexCoord0 + index))
                {
                    case 2:
                        SetTexCoordStatus(index, TexCoordStatus.Vector2);
                        mesh.GetUVs(index, uv2);
                        CopyVertexAttr(uv2, (x, v) => x.SetTexCoord(index, v));
                        break;
                    case 3:
                        SetTexCoordStatus(index, TexCoordStatus.Vector3);
                        mesh.GetUVs(index, uv3);
                        CopyVertexAttr(uv3, (x, v) => x.SetTexCoord(index, v));
                        break;
                    case 4:
                        SetTexCoordStatus(index, TexCoordStatus.Vector4);
                        mesh.GetUVs(index, uv4);
                        CopyVertexAttr(uv4, (x, v) => x.SetTexCoord(index, v));
                        break;
                }

                // ReSharper restore AccessToModifiedClosure
            }

            var triangles = mesh.triangles;
            SubMeshes.Capacity = Math.Max(SubMeshes.Capacity, mesh.subMeshCount);
            SubMeshes.Clear();
            for (var i = 0; i < SubMeshes.Count; i++)
                SubMeshes.Add(new SubMesh(Vertices, triangles, mesh.GetSubMesh(i)));
        }

        void CopyVertexAttr<T>(T[] attributes, Action<Vertex, T> assign)
        {
            for (var i = 0; i < attributes.Length; i++)
                assign(Vertices[i], attributes[i]);
        }

        void CopyVertexAttr<T>(List<T> attributes, Action<Vertex, T> assign)
        {
            for (var i = 0; i < attributes.Count; i++)
                assign(Vertices[i], attributes[i]);
        }

        private const int BitsPerTexCoordStatus = 2;
        private const int TexCoordStatusMask = (1 << BitsPerTexCoordStatus) - 1;

        public TexCoordStatus GetTexCoordStatus(int index)
        {
            return (TexCoordStatus)((_texCoordStatus >> (index * BitsPerTexCoordStatus)) & TexCoordStatusMask);
        }

        public void SetTexCoordStatus(int index, TexCoordStatus value)
        {
            _texCoordStatus = (ushort)(
                (uint)_texCoordStatus & ~(TexCoordStatusMask << (BitsPerTexCoordStatus * index)) | 
                ((uint)value & TexCoordStatusMask) << (BitsPerTexCoordStatus * index));
        }

        public void Clear()
        {
            Bounds = default;
            Vertices.Clear();
            _texCoordStatus = default;
            SubMeshes.Clear();
            BlendShapes.Clear();
            Bones.Clear();
            HasColor = false;
        }

        public void WriteToMesh(Mesh destMesh)
        {
            destMesh.Clear();

            // Basic Vertex Attributes: vertices, normals, tangents
            {
                var vertices = new Vector3[Vertices.Count];
                var normals = new Vector3[Vertices.Count];
                var tangents = new Vector4[Vertices.Count];
                for (var i = 0; i < Vertices.Count; i++)
                {
                    vertices[i] = Vertices[i].Position;
                    normals[i] = Vertices[i].Normal;
                    tangents[i] = Vertices[i].Tangent;
                }

                destMesh.vertices = vertices;
                destMesh.normals = normals;
                destMesh.tangents = tangents;
            }

            // UVs
            {
                var uv2 = new Vector2[Vertices.Count];
                var uv3 = new Vector3[Vertices.Count];
                var uv4 = new Vector4[Vertices.Count];
                for (var uvIndex = 0; uvIndex < 8; uvIndex++)
                {
                    switch (GetTexCoordStatus(uvIndex))
                    {
                        case TexCoordStatus.NotDefined:
                            // nothing to do
                            break;
                        case TexCoordStatus.Vector2:
                            for (var i = 0; i < Vertices.Count; i++)
                                uv2[i] = Vertices[i].GetTexCoord(uvIndex);
                            destMesh.SetUVs(uvIndex, uv2);
                            break;
                        case TexCoordStatus.Vector3:
                            for (var i = 0; i < Vertices.Count; i++)
                                uv3[i] = Vertices[i].GetTexCoord(uvIndex);
                            destMesh.SetUVs(uvIndex, uv3);
                            break;
                        case TexCoordStatus.Vector4:
                            for (var i = 0; i < Vertices.Count; i++)
                                uv4[i] = Vertices[i].GetTexCoord(uvIndex);
                            destMesh.SetUVs(uvIndex, uv4);
                            break;
                        default:
                            throw new ArgumentOutOfRangeException();
                    }
                }
            }

            // color
            if (HasColor)
            {
                var colors = new Color32[Vertices.Count];
                for (var i = 0; i < Vertices.Count; i++)
                    colors[i] = Vertices[i].Color;
                destMesh.colors32 = colors;
            }

            // bones
            destMesh.bindposes = Bones.Select(x => x.Bindpose).ToArray();

            // triangles and SubMeshes
            {
                // first, set vertex indices
                for (var i = 0; i < Vertices.Count; i++)
                    Vertices[i].AdditionalTemporal = i;

                var triangles = new int[SubMeshes.Sum(x => x.Triangles.Count)];
                var subMeshDescriptors = new SubMeshDescriptor[SubMeshes.Count];
                var trianglesIndex = 0;
                for (var i = 0; i < SubMeshes.Count; i++)
                {
                    subMeshDescriptors[i] = new SubMeshDescriptor(trianglesIndex, SubMeshes[i].Triangles.Count);
                    foreach (var triangle in SubMeshes[i].Triangles)
                        triangles[trianglesIndex++] = triangle.AdditionalTemporal;
                }

                destMesh.triangles = triangles;
                destMesh.subMeshCount = SubMeshes.Count;
                for (var i = 0; i < SubMeshes.Count; i++)
                    destMesh.SetSubMesh(i, subMeshDescriptors[i]);
            }

            // BoneWeights
            if (Vertices.Any(x => x.BoneWeights.Count != 0)){
                for (var i = 0; i < Bones.Count; i++)
                    Bones[i].AdditionalTemporal = i;

                var bonesPerVertex = new NativeArray<byte>(Vertices.Count, Allocator.Temp);
                var allBoneWeights =
                    new NativeArray<BoneWeight1>(Vertices.Sum(x => x.BoneWeights.Count), Allocator.Temp);
                var boneWeightsIndex = 0;
                for (var i = 0; i < Vertices.Count; i++)
                {
                    bonesPerVertex[i] = (byte)Vertices[i].BoneWeights.Count;
                    Vertices[i].BoneWeights.Sort((x, y) => -x.weight.CompareTo(y.weight));
                    foreach (var (bone, weight) in Vertices[i].BoneWeights)
                        allBoneWeights[boneWeightsIndex++] = new BoneWeight1
                            { boneIndex = bone.AdditionalTemporal, weight = weight };
                }

                destMesh.SetBoneWeights(bonesPerVertex, allBoneWeights);
            }

            // BlendShapes
            if (BlendShapes.Count != 0) {
                var blendShapeData = new (Vector3[] position, Vector3[] normal, Vector3[] tangent)[BlendShapes.Count];
                for (var i = 0; i < blendShapeData.Length; i++)
                    blendShapeData[i] = (new Vector3[Vertices.Count], new Vector3[Vertices.Count],
                        new Vector3[Vertices.Count]);

                for (var vertexI = 0; vertexI < Vertices.Count; vertexI++)
                {
                    for (var blendShapeI = 0; blendShapeI < BlendShapes.Count; blendShapeI++)
                    {
                        blendShapeData[blendShapeI].position[vertexI] =
                            Vertices[vertexI].TryGetBlendShape(BlendShapes[blendShapeI].name).position;
                        blendShapeData[blendShapeI].normal[vertexI] =
                            Vertices[vertexI].TryGetBlendShape(BlendShapes[blendShapeI].name).normal;
                        blendShapeData[blendShapeI].tangent[vertexI] =
                            Vertices[vertexI].TryGetBlendShape(BlendShapes[blendShapeI].name).tangent;
                    }
                }

                for (var i = 0; i < BlendShapes.Count; i++)
                    destMesh.AddBlendShapeFrame(BlendShapes[i].name, 100,
                        blendShapeData[i].position, blendShapeData[i].normal, blendShapeData[i].tangent);
            }
        }
    }

    internal class SubMesh
    {
        // size of this must be 3 * n
        public readonly List<Vertex> Triangles = new List<Vertex>();
        public Material SharedMaterial;

        public SubMesh()
        {
        }

        public SubMesh(List<Vertex> vertices) => Triangles = vertices;
        public SubMesh(List<Vertex> vertices, Material sharedMaterial) => 
            (Triangles, SharedMaterial) = (vertices, sharedMaterial);

        public SubMesh(List<Vertex> vertices, ReadOnlySpan<int> triangles, SubMeshDescriptor descriptor)
        {
            Assert.AreEqual(MeshTopology.Triangles, descriptor.topology);
            Triangles.Capacity = descriptor.indexCount;
            foreach (var i in triangles.Slice(descriptor.indexStart, descriptor.indexCount))
                Triangles.Add(vertices[i]);
        }
    }

    internal class Vertex
    {
        // You can use this value for your own usage but methods may clear this value.
        public int AdditionalTemporal;
        public Vector3 Position { get; set; }
        public Vector3 Normal { get; set; }
        public Vector4 Tangent { get; set; }
        public Vector4 TexCoord0 { get; set; }
        public Vector4 TexCoord1 { get; set; }
        public Vector4 TexCoord2 { get; set; }
        public Vector4 TexCoord3 { get; set; }
        public Vector4 TexCoord4 { get; set; }
        public Vector4 TexCoord5 { get; set; }
        public Vector4 TexCoord6 { get; set; }
        public Vector4 TexCoord7 { get; set; }

        public Color32 Color { get; set; } = new Color32(0xff, 0xff, 0xff, 0xff);

        // SkinnedMesh related
        public List<(Bone bone, float weight)> BoneWeights = new List<(Bone, float)>();

        public readonly Dictionary<string, (Vector3 position, Vector3 normal, Vector3 tangent)> BlendShapes = 
            new Dictionary<string, (Vector3 position, Vector3 normal, Vector3 tangent)>();

        public Vector4 GetTexCoord(int index)
        {
            switch (index)
            {
                // @formatter off
                case 0: return TexCoord0;
                case 1: return TexCoord1;
                case 2: return TexCoord2;
                case 3: return TexCoord3;
                case 4: return TexCoord4;
                case 5: return TexCoord5;
                case 6: return TexCoord6;
                case 7: return TexCoord7;
                default: throw new IndexOutOfRangeException("TexCoord index");
                // @formatter on
            }
        }

        public void SetTexCoord(int index, Vector4 value)
        {
            switch (index)
            {
                // @formatter off
                case 0: TexCoord0 = value; break;
                case 1: TexCoord1 = value; break;
                case 2: TexCoord2 = value; break;
                case 3: TexCoord3 = value; break;
                case 4: TexCoord4 = value; break;
                case 5: TexCoord5 = value; break;
                case 6: TexCoord6 = value; break;
                case 7: TexCoord7 = value; break;
                default: throw new IndexOutOfRangeException("TexCoord index");
                // @formatter on
            }
        }

        public (Vector3 position, Vector3 normal, Vector3 tangent) TryGetBlendShape(string name) => 
            BlendShapes.TryGetValue(name, out var value) ? value : default;

        public Vertex()
        {
        }

        private Vertex(Vertex vertex)
        {
            Position = vertex.Position;
            Normal = vertex.Normal;
            Tangent = vertex.Tangent;
            TexCoord0 = vertex.TexCoord0;
            TexCoord1 = vertex.TexCoord1;
            TexCoord2 = vertex.TexCoord2;
            TexCoord3 = vertex.TexCoord3;
            TexCoord4 = vertex.TexCoord4;
            TexCoord5 = vertex.TexCoord5;
            TexCoord6 = vertex.TexCoord6;
            TexCoord7 = vertex.TexCoord7;
            Color = vertex.Color;
            BoneWeights = vertex.BoneWeights.ToList();
            BlendShapes = new Dictionary<string, (Vector3, Vector3, Vector3)>(vertex.BlendShapes);
        }

        public Vertex Clone() => new Vertex(this);
    }

    internal class Bone
    {
        // You can use this value for your own usage but methods may clear this value.
        public int AdditionalTemporal;
        public Matrix4x4 Bindpose;
        public Transform Transform;

        public Bone(Matrix4x4 bindPose) : this(bindPose, null) {}
        public Bone(Matrix4x4 bindPose, Transform transform) => (Bindpose, Transform) = (bindPose, transform);
    }

    internal enum TexCoordStatus
    {
        NotDefined = 0,
        Vector2 = 1,
        Vector3 = 2,
        Vector4 = 3,
    }
}
#endif
