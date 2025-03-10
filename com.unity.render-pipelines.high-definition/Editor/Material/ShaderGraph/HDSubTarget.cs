using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;
using UnityEditor.ShaderGraph;
using UnityEditor.ShaderGraph.Internal;
using UnityEditor.Graphing;
using UnityEditor.ShaderGraph.Legacy;
using UnityEditor.Rendering.HighDefinition.ShaderGraph.Legacy;
using UnityEditor.VFX;
using static UnityEngine.Rendering.HighDefinition.HDMaterialProperties;
using static UnityEditor.Rendering.HighDefinition.HDShaderUtils;

namespace UnityEditor.Rendering.HighDefinition.ShaderGraph
{
    abstract class HDSubTarget : SubTarget<HDTarget>, IHasMetadata,
        IRequiresData<SystemData>, IVersionable<ShaderGraphVersion>, IRequireVFXContext
    {
        SystemData m_SystemData;
        protected bool m_MigrateFromOldCrossPipelineSG; // Use only for the migration to shader stack architecture
        protected bool m_MigrateFromOldSG; // Use only for the migration from early shader stack architecture to recent one

        // Interface Properties
        SystemData IRequiresData<SystemData>.data
        {
            get => m_SystemData;
            set => m_SystemData = value;
        }

        // Public properties
        public SystemData systemData
        {
            get => m_SystemData;
            set => m_SystemData = value;
        }

        // VFX Properties
        protected VFXContext m_ContextVFX = null;
        protected VFXContextCompiledData m_ContextDataVFX;
        protected bool TargetsVFX() => m_ContextVFX != null;

        protected virtual int ComputeMaterialNeedsUpdateHash() => 0;

        public override bool IsActive() => true;

        protected abstract ShaderID shaderID { get; }
        protected abstract string customInspector { get; }
        protected abstract GUID subTargetAssetGuid { get; }
        protected abstract string renderType { get; }
        protected abstract string renderQueue { get; }
        protected abstract string templatePath { get; }
        protected abstract string[] templateMaterialDirectories { get; }
        protected abstract FieldDescriptor subShaderField { get; }
        protected abstract string subShaderInclude { get; }

        protected virtual string postDecalsInclude => null;
        protected virtual string raytracingInclude => null;
        protected virtual string pathtracingInclude => null;
        protected virtual bool supportPathtracing => false;
        protected virtual bool supportRaytracing => false;

        public virtual string identifier => GetType().Name;

        public virtual ScriptableObject GetMetadataObject()
        {
            var hdMetadata = ScriptableObject.CreateInstance<HDMetadata>();
            hdMetadata.shaderID = shaderID;
            hdMetadata.migrateFromOldCrossPipelineSG = m_MigrateFromOldCrossPipelineSG;
            return hdMetadata;
        }

        ShaderGraphVersion IVersionable<ShaderGraphVersion>.version
        {
            get => systemData.version;
            set => systemData.version = value;
        }

        // Generate migration description steps to migrate HD shader targets
        internal static MigrationDescription<ShaderGraphVersion, HDSubTarget> migrationSteps => MigrationDescription.New(
            Enum.GetValues(typeof(ShaderGraphVersion)).Cast<ShaderGraphVersion>().Select(
                version => MigrationStep.New(version, (HDSubTarget t) => t.MigrateTo(version))
                ).ToArray()
        );

        /// <summary>
        /// Override this method to handle migration in inherited subtargets
        /// </summary>
        /// <param name="version">The current version of the migration</param>
        internal virtual void MigrateTo(ShaderGraphVersion version)
        {
        }

        static readonly GUID kSourceCodeGuid = new GUID("c09e6e9062cbd5a48900c48a0c2ed1c2");  // HDSubTarget.cs

        public override void Setup(ref TargetSetupContext context)
        {
            context.AddAssetDependency(kSourceCodeGuid, AssetCollection.Flags.SourceDependency);
            context.AddAssetDependency(subTargetAssetGuid, AssetCollection.Flags.SourceDependency);
            var inspector = TargetsVFX() ? VFXHDRPSubTarget.Inspector : customInspector;
            if (!context.HasCustomEditorForRenderPipeline(typeof(HDRenderPipelineAsset)))
                context.AddCustomEditorForRenderPipeline(inspector, typeof(HDRenderPipelineAsset));

            if (migrationSteps.Migrate(this))
                OnBeforeSerialize();

            // Migration hack to have the case where SG doesn't have version yet but is already upgraded to the stack system
            if (!systemData.firstTimeMigrationExecuted)
            {
                // Force the initial migration step
                MigrateTo(ShaderGraphVersion.FirstTimeMigration);
                systemData.firstTimeMigrationExecuted = true;
                OnBeforeSerialize();
                systemData.materialNeedsUpdateHash = ComputeMaterialNeedsUpdateHash();
            }

            foreach (var subShader in EnumerateSubShaders())
            {
                // patch render type and render queue from pass declaration:
                var patchedSubShader = subShader;
                patchedSubShader.renderType = renderType;
                patchedSubShader.renderQueue = renderQueue;
                context.AddSubShader(patchedSubShader);
            }
        }

        protected SubShaderDescriptor PostProcessSubShader(SubShaderDescriptor subShaderDescriptor)
        {
            if (TargetsVFX())
                subShaderDescriptor = VFXSubTarget.PostProcessSubShader(subShaderDescriptor, m_ContextVFX, m_ContextDataVFX);

            if (String.IsNullOrEmpty(subShaderDescriptor.pipelineTag))
                subShaderDescriptor.pipelineTag = HDRenderPipeline.k_ShaderTagName;

            var passes = subShaderDescriptor.passes.ToArray();
            PassCollection finalPasses = new PassCollection();
            for (int i = 0; i < passes.Length; i++)
            {
                var passDescriptor = passes[i].descriptor;
                passDescriptor.passTemplatePath = templatePath;
                passDescriptor.sharedTemplateDirectories = templateMaterialDirectories;

                // Add the subShader to enable fields that depends on it
                var originalRequireFields = passDescriptor.requiredFields;
                // Duplicate require fields to avoid unwanted shared list modification
                passDescriptor.requiredFields = new FieldCollection();
                if (originalRequireFields != null)
                    foreach (var field in originalRequireFields)
                        passDescriptor.requiredFields.Add(field.field);
                passDescriptor.requiredFields.Add(subShaderField);

                IncludeCollection finalIncludes = new IncludeCollection();

                // Replace include placeholders if necessary:
                foreach (var include in passDescriptor.includes)
                {
                    var path = include.path;

                    if (path == CoreIncludes.kPassPlaceholder)
                        path = subShaderInclude;
                    if (path == CoreIncludes.kPostDecalsPlaceholder)
                        path = postDecalsInclude;
                    if (path == CoreIncludes.kRaytracingPlaceholder)
                        path = raytracingInclude;
                    if (path == CoreIncludes.kPathtracingPlaceholder)
                        path = pathtracingInclude;

                    if (!String.IsNullOrEmpty(path))
                        finalIncludes.Add(path, include.location, include.fieldConditions);
                }
                passDescriptor.includes = finalIncludes;

                // Replace valid pixel blocks by automatic thing so we don't have to write them
                var tmpCtx = new TargetActiveBlockContext(new List<BlockFieldDescriptor>(), passDescriptor);
                GetActiveBlocks(ref tmpCtx);
                if (passDescriptor.validPixelBlocks == null)
                    passDescriptor.validPixelBlocks = tmpCtx.activeBlocks.Where(b => b.shaderStage == ShaderStage.Fragment).ToArray();
                if (passDescriptor.validVertexBlocks == null)
                    passDescriptor.validVertexBlocks = tmpCtx.activeBlocks.Where(b => b.shaderStage == ShaderStage.Vertex).ToArray();

                // Add various collections, most are init in HDShaderPasses.cs
                passDescriptor.keywords = passDescriptor.keywords == null ? new KeywordCollection() : new KeywordCollection { passDescriptor.keywords }; // Duplicate keywords to avoid side effects (static list modification)

                passDescriptor.fieldDependencies = passDescriptor.fieldDependencies == null ? new DependencyCollection() : new DependencyCollection { passDescriptor.fieldDependencies }; // Duplicate fieldDependencies to avoid side effects (static list modification)
                passDescriptor.fieldDependencies.Add(CoreFieldDependencies.Default);

                CollectPassKeywords(ref passDescriptor);


                finalPasses.Add(passDescriptor, passes[i].fieldConditions);
            }

            subShaderDescriptor.passes = finalPasses;

            return subShaderDescriptor;
        }

        protected virtual void CollectPassKeywords(ref PassDescriptor pass) {}

        public override void GetFields(ref TargetFieldContext context)
        {
            // Common properties between all HD master nodes
            // Dots
            context.AddField(HDFields.DotsInstancing, systemData.dotsInstancing);

            // VFX Setup
            if (TargetsVFX())
                VFXSubTarget.GetFields(ref context, m_ContextVFX);
        }

        protected abstract IEnumerable<SubShaderDescriptor> EnumerateSubShaders();

        public override void GetPropertiesGUI(ref TargetPropertyGUIContext context, Action onChange, Action<String> registerUndo)
        {
            var gui = new SubTargetPropertiesGUI(context, onChange, registerUndo, systemData, null, null);
            AddInspectorPropertyBlocks(gui);
            context.Add(gui);
        }

        protected abstract void AddInspectorPropertyBlocks(SubTargetPropertiesGUI blockList);

        public override object saveContext
        {
            get
            {
                int hash = ComputeMaterialNeedsUpdateHash();
                bool needsUpdate = hash != systemData.materialNeedsUpdateHash;
                if (needsUpdate)
                    systemData.materialNeedsUpdateHash = hash;

                return new HDSaveContext { updateMaterials = needsUpdate };
            }
        }

        public void ConfigureContextData(VFXContext context, VFXContextCompiledData data)
        {
            m_ContextVFX = context;
            m_ContextDataVFX = data;
        }
    }
}
