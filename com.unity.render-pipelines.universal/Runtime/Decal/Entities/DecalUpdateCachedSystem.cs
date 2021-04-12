using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Jobs;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace UnityEngine.Rendering.Universal
{
    internal class DecalCachedChunk : DecalChunk
    {
        public MaterialPropertyBlock propertyBlock;
        public int passIndexDBuffer;
        public int passIndexEmissive;
        public int passIndexScreenSpace;
        public int passIndexGBuffer;
        public int drawOrder;
        public float drawDistance;
        public bool enabledInstancing;
        public bool isCreated;

        public NativeArray<float4x4> decalToWorlds;
        public NativeArray<float4x4> normalToWorlds;
        public NativeArray<float4x4> sizeOffsets;
        public NativeArray<float2> drawDistances;
        public NativeArray<float2> angleFades;
        public NativeArray<float4> uvScaleBias;
        public NativeArray<bool> affectsTransparencies;
        public NativeArray<int> layerMasks;
        public NativeArray<ulong> sceneLayerMasks;
        public NativeArray<float> fadeFactors;
        public NativeArray<DecalLayerEnum> decalLayerMasks;
        public NativeArray<BoundingSphere> boundingSpheres2;
        public NativeArray<float3> positions;
        public NativeArray<quaternion> rotation;
        public NativeArray<bool> dirty;

        public BoundingSphere[] boundingSpheres;

        public override void Push()
        {
            count++;
        }

        public override void RemoveAtSwapBack(int entityIndex)
        {
            RemoveAtSwapBack(ref decalToWorlds, entityIndex, count);
            RemoveAtSwapBack(ref normalToWorlds, entityIndex, count);
            RemoveAtSwapBack(ref sizeOffsets, entityIndex, count);
            RemoveAtSwapBack(ref drawDistances, entityIndex, count);
            RemoveAtSwapBack(ref angleFades, entityIndex, count);
            RemoveAtSwapBack(ref uvScaleBias, entityIndex, count);
            RemoveAtSwapBack(ref affectsTransparencies, entityIndex, count);
            RemoveAtSwapBack(ref layerMasks, entityIndex, count);
            RemoveAtSwapBack(ref sceneLayerMasks, entityIndex, count);
            RemoveAtSwapBack(ref fadeFactors, entityIndex, count);
            RemoveAtSwapBack(ref decalLayerMasks, entityIndex, count);
            RemoveAtSwapBack(ref boundingSpheres, entityIndex, count);
            RemoveAtSwapBack(ref boundingSpheres2, entityIndex, count);
            RemoveAtSwapBack(ref positions, entityIndex, count);
            RemoveAtSwapBack(ref rotation, entityIndex, count);
            RemoveAtSwapBack(ref dirty, entityIndex, count);
            count--;
        }

        public override void SetCapacity(int newCapacity)
        {
            ResizeNativeArray(ref decalToWorlds, newCapacity);
            ResizeNativeArray(ref normalToWorlds, newCapacity);
            ResizeNativeArray(ref sizeOffsets, newCapacity);
            ResizeNativeArray(ref drawDistances, newCapacity);
            ResizeNativeArray(ref angleFades, newCapacity);
            ResizeNativeArray(ref uvScaleBias, newCapacity);
            ResizeNativeArray(ref affectsTransparencies, newCapacity);
            ResizeNativeArray(ref layerMasks, newCapacity);
            ResizeNativeArray(ref sceneLayerMasks, newCapacity);
            ResizeNativeArray(ref fadeFactors, newCapacity);
            ResizeNativeArray(ref decalLayerMasks, newCapacity);
            ResizeNativeArray(ref boundingSpheres2, newCapacity);
            ResizeNativeArray(ref positions, newCapacity);
            ResizeNativeArray(ref rotation, newCapacity);
            ResizeNativeArray(ref dirty, newCapacity);

            ResizeArray(ref boundingSpheres, newCapacity);
            capacity = newCapacity;
        }

        public override void Dispose()
        {
            if (capacity == 0)
                return;

            decalToWorlds.Dispose();
            normalToWorlds.Dispose();
            sizeOffsets.Dispose();
            drawDistances.Dispose();
            angleFades.Dispose();
            uvScaleBias.Dispose();
            affectsTransparencies.Dispose();
            layerMasks.Dispose();
            sceneLayerMasks.Dispose();
            fadeFactors.Dispose();
            decalLayerMasks.Dispose();
            boundingSpheres2.Dispose();
            positions.Dispose();
            rotation.Dispose();
            dirty.Dispose();
            count = 0;
            capacity = 0;
        }
    }

    internal class DecalUpdateCachedSystem
    {
        private DecalEntityManager m_EntityManager;
        private ProfilingSampler m_Sampler;
        private ProfilingSampler m_SamplerSizeOffset;
        private ProfilingSampler m_SamplerJob;
        private ProfilingSampler m_SamplerProperties;

        public DecalUpdateCachedSystem(DecalEntityManager entityManager)
        {
            m_EntityManager = entityManager;
            m_Sampler = new ProfilingSampler("DecalUpdateCachedSystem.Execute");
            m_SamplerSizeOffset = new ProfilingSampler("DecalUpdateCachedSystem.ExecuteSizeOffset");
            m_SamplerJob = new ProfilingSampler("DecalUpdateCachedSystem.ExecuteJob");
            m_SamplerProperties = new ProfilingSampler("DecalUpdateCachedSystem.ExecuteProperties");
        }

        public void Execute()
        {
            using (new ProfilingScope(null, m_Sampler))
            {
                for (int i = 0; i < m_EntityManager.chunkCount; ++i)
                    Execute(m_EntityManager.entityChunks[i], m_EntityManager.cachedChunks[i], m_EntityManager.entityChunks[i].count);
            }
        }

        private void Execute(DecalEntityChunk entityChunk, DecalCachedChunk cachedChunk, int count)
        {
            if (count == 0)
                return;

            cachedChunk.currentJobHandle.Complete();

            // Make sure draw order is up to date
            var material = entityChunk.material;
            if (material.HasProperty("_DrawOrder"))
                cachedChunk.drawOrder = material.GetInt("_DrawOrder");

            // Shader can change any time in editor, so we have to update passes each time
#if !UNITY_EDITOR
            if (!cachedChunk.isCreated)
#endif
            {
                int passIndexDBuffer = material.FindPass(DecalShaderPassNames.DBufferProjector);
                cachedChunk.passIndexDBuffer = passIndexDBuffer;

                int passIndexEmissive = material.FindPass(DecalShaderPassNames.DecalProjectorForwardEmissive);
                cachedChunk.passIndexEmissive = passIndexEmissive;

                int passIndexScreenSpace = material.FindPass(DecalShaderPassNames.DecalScreenSpaceProjector);
                cachedChunk.passIndexScreenSpace = passIndexScreenSpace;

                int passIndexGBuffer = material.FindPass(DecalShaderPassNames.DecalGBufferProjector);
                cachedChunk.passIndexGBuffer = passIndexGBuffer;

                cachedChunk.enabledInstancing = material.enableInstancing;

                cachedChunk.isCreated = true;
            }

            using (new ProfilingScope(null, m_SamplerJob))
            {
                UpdateTransformsJob updateTransformJob = new UpdateTransformsJob()
                {
                    positions = cachedChunk.positions,
                    rotations = cachedChunk.rotation,
                    dirty = cachedChunk.dirty,
                    sizeOffsets = cachedChunk.sizeOffsets,
                    decalToWorlds = cachedChunk.decalToWorlds,
                    normalToWorlds = cachedChunk.normalToWorlds,
                    boundingSpheres = cachedChunk.boundingSpheres2,
                    minDistance = 0.01f,
                };

                var handle = updateTransformJob.Schedule(entityChunk.transformAccessArray);
                cachedChunk.currentJobHandle = handle;
            }
        }

#if ENABLE_BURST_1_0_0_OR_NEWER
        [Unity.Burst.BurstCompile]
#endif
        public unsafe struct UpdateTransformsJob : IJobParallelForTransform
        {
            private static readonly quaternion k_MinusYtoZRotation = quaternion.EulerXYZ(-math.PI / 2.0f, 0, 0);

            public NativeArray<float3> positions;
            public NativeArray<quaternion> rotations;
            public NativeArray<bool> dirty;

            [ReadOnly] public NativeArray<float4x4> sizeOffsets;
            [WriteOnly] public NativeArray<float4x4> decalToWorlds;
            [WriteOnly] public NativeArray<float4x4> normalToWorlds;
            [WriteOnly] public NativeArray<BoundingSphere> boundingSpheres;

            public float minDistance;

            public void Execute(int index, TransformAccess transform)
            {
                quaternion rotation = math.mul(transform.rotation, k_MinusYtoZRotation);
                bool positionChanged = math.distance(transform.position, positions[index]) > minDistance;
                bool rotationChanged = math.distance(rotation.value, rotations[index].value) > minDistance;
                if (positionChanged)
                    positions[index] = transform.position;
                if (rotationChanged)
                    rotations[index] = rotation;

                // Early out if position did not changed
                if (!positionChanged && !rotationChanged && !dirty[index])
                    return;

                float4x4 sizeOffset = sizeOffsets[index];
                float4x4 localToWorld = float4x4.TRS(positions[index], rotations[index], new float3(1, 1, 1));
                float4x4 decalToWorld = math.mul(localToWorld, sizeOffset);
                decalToWorlds[index] = decalToWorld;
                boundingSpheres[index] = GetDecalProjectBoundingSphere(decalToWorld);

                /*Matrix4x4 decalRotation = Matrix4x4.Rotate(rotations[index]);
                // z/y axis swap for normal to decal space, Unity is column major
                float y0 = decalRotation.m01;
                float y1 = decalRotation.m11;
                float y2 = decalRotation.m21;
                decalRotation.m01 = decalRotation.m02;
                decalRotation.m11 = decalRotation.m12;
                decalRotation.m21 = decalRotation.m22;
                decalRotation.m02 = y0;
                decalRotation.m12 = y1;
                decalRotation.m22 = y2;*/

                float4x4 decalRotation = new float4x4(rotations[index], new float3(0, 0, 0));
                // z/y axis swap for normal to decal space, Unity is column major
                /*float y0 = decalRotation.m01;
                float y1 = decalRotation.m11;
                float y2 = decalRotation.m21;
                decalRotation.m01 = decalRotation.m02;
                decalRotation.m11 = decalRotation.m12;
                decalRotation.m21 = decalRotation.m22;
                decalRotation.m02 = y0;
                decalRotation.m12 = y1;
                decalRotation.m22 = y2;*/

                float4 temp = decalRotation.c1;
                decalRotation.c1 = decalRotation.c2;
                decalRotation.c2 = temp;

                normalToWorlds[index] = decalRotation;
            }

            private BoundingSphere GetDecalProjectBoundingSphere(Matrix4x4 decalToWorld)
            {
                float4 min = new float4(-0.5f, -0.5f, -0.5f, 1.0f);
                float4 max = new float4(0.5f, 0.5f, 0.5f, 1.0f);
                min = math.mul(decalToWorld, min);
                max = math.mul(decalToWorld, max);

                float3 position = ((max + min) / 2f).xyz;
                float radius = math.length(max - min) / 2f;

                BoundingSphere res = new BoundingSphere();
                res.position = position;
                res.radius = radius;
                return res;
            }
        }
    }
}
