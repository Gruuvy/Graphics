namespace UnityEngine.Rendering
{
    /// <summary>
    /// Common code for all Data-Driven Lens Flare used
    /// </summary>
    public sealed class LensFlareCommonSRP
    {
        private static LensFlareCommonSRP m_Instance = null;
        private static readonly object m_Padlock = new object();
        private static System.Collections.Generic.List<LensFlareComponentSRP> m_Data = new System.Collections.Generic.List<LensFlareComponentSRP>();

        private LensFlareCommonSRP()
        {
        }

        /// <summary>
        /// Current unique instance
        /// </summary>
        public static LensFlareCommonSRP Instance
        {
            get
            {
                if (m_Instance == null)
                {
                    lock (m_Padlock)
                    {
                        if (m_Instance == null)
                        {
                            m_Instance = new LensFlareCommonSRP();
                        }
                    }
                }
                return m_Instance;
            }
        }

        private System.Collections.Generic.List<LensFlareComponentSRP> Data { get { return LensFlareCommonSRP.m_Data; } }

        /// <summary>
        /// Return the pool of Lens Flare added
        /// </summary>
        /// <returns>The Lens Flare Pool</returns>
        public System.Collections.Generic.List<LensFlareComponentSRP> GetData()
        {
            return Data;
        }

        /// <summary>
        /// Check if we have at least one Lens Flare added on the pool
        /// </summary>
        /// <returns>true if no Lens Flare were added</returns>
        public bool IsEmpty()
        {
            return Data.Count == 0;
        }

        /// <summary>
        /// Add a new lens flare component on the pool.
        /// </summary>
        /// <param name="newData">The new data added</param>
        public void AddData(LensFlareComponentSRP newData)
        {
            Debug.Assert(Instance == this, "LensFlareCommonSRP can have only one instance");

            if (!m_Data.Contains(newData))
            {
                m_Data.Add(newData);
            }
        }

        /// <summary>
        /// Attenuation by Light Shape for Point Light
        /// </summary>
        /// <returns>Attenuation Factor</returns>
        static public float ShapeAttenuationPointLight()
        {
            return 1.0f;
        }

        /// <summary>
        /// Attenuation by Light Shape for Directional Light
        /// </summary>
        /// <param name="forward">Forward Vector of Directional Light</param>
        /// <param name="wo">Vector pointing to the eye</param>
        /// <returns>Attenuation Factor</returns>
        static public float ShapeAttenuationDirLight(Vector3 forward, Vector3 wo)
        {
            return Mathf.Max(Vector3.Dot(forward, wo), 0.0f);
        }

        /// <summary>
        /// Attenuation by Light Shape for Spot Light with Cone Shape
        /// </summary>
        /// <param name="forward">Forward Vector of Directional Light</param>
        /// <param name="wo">Vector pointing to the eye</param>
        /// <param name="spotAngle">The angle of the light's spotlight cone in degrees.</param>
        /// <param name="innerSpotPercent01">Get the inner spot radius between 0 and 1.</param>
        /// <returns>Attenuation Factor</returns>
        static public float ShapeAttenuationSpotConeLight(Vector3 forward, Vector3 wo, float spotAngle, float innerSpotPercent01)
        {
            float outerDot = Mathf.Max(Mathf.Cos(0.5f * spotAngle * Mathf.Deg2Rad), 0.0f);
            float innerDot = Mathf.Max(Mathf.Cos(0.5f * spotAngle * Mathf.Deg2Rad * innerSpotPercent01), 0.0f);
            float dot = Mathf.Max(Vector3.Dot(forward, wo), 0.0f);
            return Mathf.Clamp01((dot - outerDot) / (innerDot - outerDot));
        }

        /// <summary>
        /// Attenuation by Light Shape for Spot Light with Box Shape
        /// </summary>
        /// <param name="forward">Forward Vector of Directional Light</param>
        /// <param name="wo">Vector pointing to the eye</param>
        /// <returns>Attenuation Factor</returns>
        static public float ShapeAttenuationSpotBoxLight(Vector3 forward, Vector3 wo)
        {
            return Mathf.Max(Mathf.Sign(Vector3.Dot(forward, wo)), 0.0f);
        }

        /// <summary>
        /// Attenuation by Light Shape for Spot Light with Pyramid Shape
        /// </summary>
        /// <param name="forward">Forward Vector of Directional Light</param>
        /// <param name="wo">Vector pointing to the eye</param>
        /// <returns>Attenuation Factor</returns>
        static public float ShapeAttenuationSpotPyramidLight(Vector3 forward, Vector3 wo)
        {
            return ShapeAttenuationSpotBoxLight(forward, wo);
        }

        /// <summary>
        /// Attenuation by Light Shape for Area Light with Tube Shape
        /// </summary>
        /// <param name="lightPositionWS">World Space position of the Light</param>
        /// <param name="lightSide">Vector pointing to the side (right or left) or the light</param>
        /// <param name="lightWidth">Width (half extent) of the tube light</param>
        /// <param name="cam">Camera rendering the Tube Light</param>
        /// <returns>Attenuation Factor</returns>
        static public float ShapeAttenuationAreaTubeLight(Vector3 lightPositionWS, Vector3 lightSide, float lightWidth, Camera cam)
        {
            // Ref: https://hal.archives-ouvertes.fr/hal-02155101/document
            // Listing 1.6. Analytic line-diffuse integration.
            float Fpo(float d, float l)
            {
                return l / (d * (d * d + l * l)) + Mathf.Atan(l / d) / (d * d);
            }

            float Fwt(float d, float l)
            {
                return l * l / (d * (d * d + l * l));
            }

            Vector3 p1Global = lightPositionWS + lightSide * lightWidth * 0.5f;
            Vector3 p2Global = lightPositionWS - lightSide * lightWidth * 0.5f;
            Vector3 p1Front = lightPositionWS + cam.transform.right * lightWidth * 0.5f;
            Vector3 p2Front = lightPositionWS - cam.transform.right * lightWidth * 0.5f;

            Vector3 p1World = cam.transform.InverseTransformPoint(p1Global);
            Vector3 p2World = cam.transform.InverseTransformPoint(p2Global);
            Vector3 p1WorldFront = cam.transform.InverseTransformPoint(p1Front);
            Vector3 p2WorldFront = cam.transform.InverseTransformPoint(p2Front);

            float DiffLineIntegral(Vector3 p1, Vector3 p2)
            {
                float diffIntegral;
                // tangent
                Vector3 wt = (p2 - p1).normalized;
                // clamping
                if (p1.z <= 0.0 && p2.z <= 0.0)
                {
                    diffIntegral = 0.0f;
                }
                else
                {
                    if (p1.z < 0.0)
                        p1 = (p1 * p2.z - p2 * p1.z) / (+p2.z - p1.z);
                    if (p2.z < 0.0)
                        p2 = (-p1 * p2.z + p2 * p1.z) / (-p2.z + p1.z);
                    // parameterization
                    float l1 = Vector3.Dot(p1, wt);
                    float l2 = Vector3.Dot(p2, wt);
                    // shading point orthonormal projection on the line
                    Vector3 po = p1 - l1 * wt;
                    // distance to line
                    float d = po.magnitude;
                    // integral
                    float integral = (Fpo(d, l2) - Fpo(d, l1)) * po.z + (Fwt(d, l2) - Fwt(d, l1)) * wt.z;
                    diffIntegral = integral / Mathf.PI;
                }

                return diffIntegral;
            }

            float frontModulation = DiffLineIntegral(p1WorldFront, p2WorldFront);
            float worldModulation = DiffLineIntegral(p1World, p2World);

            return frontModulation > 0.0f ? worldModulation / frontModulation : 1.0f;
        }

        /// <summary>
        /// Attenuation by Light Shape for Area Light with Rectangular Shape
        /// </summary>
        /// <param name="forward">Forward Vector of Directional Light</param>
        /// <param name="wo">Vector pointing to the eye</param>
        /// <returns>Attenuation Factor</returns>
        static public float ShapeAttenuationAreaRectangleLight(Vector3 forward, Vector3 wo)
        {
            return ShapeAttenuationDirLight(forward, wo);
        }

        /// <summary>
        /// Attenuation by Light Shape for Area Light with Disc Shape
        /// </summary>
        /// <param name="forward">Forward Vector of Directional Light</param>
        /// <param name="wo">Vector pointing to the eye</param>
        /// <returns>Attenuation Factor</returns>
        static public float ShapeAttenuationAreaDiscLight(Vector3 forward, Vector3 wo)
        {
            return ShapeAttenuationDirLight(forward, wo);
        }

        static Vector4 GetFlareData0(Vector2 screenPos, Vector2 translationScale, Vector2 vLocalScreenRatio, float angleDeg, float position, float angularOffset, Vector2 positionOffset, bool autoRotate)
        {
            float globalCos0 = Mathf.Cos(-angularOffset * Mathf.Deg2Rad);
            float globalSin0 = Mathf.Sin(-angularOffset * Mathf.Deg2Rad);

            Vector2 rayOff = -translationScale * (screenPos + screenPos * (position - 1.0f));
            rayOff = new Vector2(globalCos0 * rayOff.x - globalSin0 * rayOff.y,
                globalSin0 * rayOff.x + globalCos0 * rayOff.y);

            float rotation = angleDeg;

            rotation += 180.0f;
            if (!autoRotate)
            {
                rotation = Mathf.Abs(rotation) < 1e-4f ? -360.0f : -rotation;
            }
            else
            {
                Vector2 pos = (rayOff.normalized * vLocalScreenRatio) * translationScale;
                rotation -= Mathf.Rad2Deg * Mathf.Atan2(pos.y, pos.x);
            }
            rotation *= Mathf.Deg2Rad;
            float localCos0 = Mathf.Cos(-rotation);
            float localSin0 = Mathf.Sin(-rotation);

            return new Vector4(localCos0, localSin0, positionOffset.x, -positionOffset.y);
        }

        static Vector2 GetLensFlareRayOffset(Vector2 screenPos, float position, float globalCos0, float globalSin0)
        {
            Vector2 rayOff = -(screenPos + screenPos * (position - 1.0f));
            return new Vector2(globalCos0 * rayOff.x - globalSin0 * rayOff.y,
                globalSin0 * rayOff.x + globalCos0 * rayOff.y);
        }

        /// <summary>
        /// Effective Job of drawing the set of Lens Flare registered
        /// </summary>
        /// <param name="lensFlareShader">Lens Flare material (HDRP or URP shader)</param>
        /// <param name="lensFlares">Set of Lens Flare</param>
        /// <param name="cam">Camera</param>
        /// <param name="actualWidth">Width actually used for rendering after dynamic resolution and XR is applied.</param>
        /// <param name="actualHeight">Height actually used for rendering after dynamic resolution and XR is applied.</param>
        /// <param name="usePanini">Set if use Panani Projection</param>
        /// <param name="paniniDistance">Distance used for Panini projection</param>
        /// <param name="paniniCropToFit">CropToFit parameter used for Panini projection</param>
        /// <param name="cmd">Command Buffer</param>
        /// <param name="colorBuffer">Source Render Target which contains the Color Buffer</param>
        /// <param name="GetLensFlareLightAttenuation">Delegate to which return return the Attenuation of the light based on their shape which uses the functions ShapeAttenuation...(...), must reimplemented per SRP</param>
        /// <param name="_FlareTex">ShaderID for the FlareTex</param>
        /// <param name="_FlareColorValue">ShaderID for the FlareColor</param>
        /// <param name="_FlareData0">ShaderID for the FlareData0</param>
        /// <param name="_FlareData1">ShaderID for the FlareData1</param>
        /// <param name="_FlareData2">ShaderID for the FlareData2</param>
        /// <param name="_FlareData3">ShaderID for the FlareData3</param>
        /// <param name="_FlareData4">ShaderID for the FlareData4</param>
        /// <param name="_FlareData5">ShaderID for the FlareData5</param>
        /// <param name="debugView">Debug View which setup black background to see only Lens Flare</param>
        static public void DoLensFlareDataDrivenCommon(Material lensFlareShader, LensFlareCommonSRP lensFlares, Camera cam, float actualWidth, float actualHeight,
            bool usePanini, float paniniDistance, float paniniCropToFit,
            Rendering.CommandBuffer cmd,
            Rendering.RenderTargetIdentifier colorBuffer,
            System.Func<Light, Camera, Vector3, float> GetLensFlareLightAttenuation,
            int _FlareTex, int _FlareColorValue, int _FlareData0, int _FlareData1, int _FlareData2, int _FlareData3, int _FlareData4, int _FlareData5,
            bool debugView)
        {
            Vector2 vScreenRatio;

            if (lensFlares.IsEmpty())
                return;

            Vector2 screenSize = new Vector2(actualWidth, actualHeight);
            float screenRatio = screenSize.x / screenSize.y;
            vScreenRatio = new Vector2(screenRatio, 1.0f);

            Rendering.CoreUtils.SetRenderTarget(cmd, colorBuffer);
            if (debugView)
            {
                // Background pitch black to see only the Flares
                cmd.ClearRenderTarget(false, true, Color.black);
            }

#if UNITY_EDITOR
            if (cam.cameraType == CameraType.SceneView)
            {
                // Determine whether the "Animated Materials" checkbox is checked for the current view.
                for (int i = 0; i < UnityEditor.SceneView.sceneViews.Count; i++) // Using a foreach on an ArrayList generates garbage ...
                {
                    var sv = UnityEditor.SceneView.sceneViews[i] as UnityEditor.SceneView;
                    if (sv.camera == cam && !sv.sceneViewState.flaresEnabled)
                    {
                        return;
                    }
                }
            }
#endif

            foreach (LensFlareComponentSRP comp in lensFlares.GetData())
            {
                if (comp == null)
                    continue;

                LensFlareDataSRP data = comp.lensFlareData;

                if (!comp.enabled ||
                    !comp.gameObject.activeSelf ||
                    !comp.gameObject.activeInHierarchy ||
                    data == null ||
                    data.elements == null ||
                    data.elements.Length == 0)
                    continue;

                Light light = comp.GetComponent<Light>();

                Vector3 positionWS;
                Vector3 viewportPos;

                bool isDirLight = false;
                if (light != null && light.type == LightType.Directional)
                {
                    positionWS = -light.transform.forward * cam.farClipPlane;
                    isDirLight = true;
                }
                else
                {
                    positionWS = comp.transform.position;
                }

                viewportPos = cam.WorldToViewportPoint(positionWS);

                if (usePanini && cam == Camera.main)
                {
                    viewportPos = DoPaniniProjection(viewportPos, actualWidth, actualHeight, cam.fieldOfView, paniniCropToFit, paniniDistance);
                }

                if (viewportPos.z < 0.0f)
                    continue;

                if (!comp.allowOffScreen)
                {
                    if (viewportPos.x < 0.0f || viewportPos.x > 1.0f ||
                        viewportPos.y < 0.0f || viewportPos.y > 1.0f)
                        continue;
                }

                Vector4 modulationByColor = Vector4.one;
                Vector4 modulationAttenuation = Vector4.one;
                Vector3 diffToObject = positionWS - cam.transform.position;
                float distToObject = diffToObject.magnitude;
                float coefDistSample = distToObject / comp.maxAttenuationDistance;
                float coefScaleSample = distToObject / comp.maxAttenuationScale;
                float distanceAttenuation = !isDirLight && comp.distanceAttenuationCurve.length > 0 ? comp.distanceAttenuationCurve.Evaluate(coefDistSample) : 1.0f;
                float scaleByDistance = !isDirLight && comp.scaleByDistanceCurve.length >= 1 ? comp.scaleByDistanceCurve.Evaluate(coefScaleSample) : 1.0f;

                Color globalColorModulation = Color.white;

                if (light != null)
                {
                    if (comp.attenuationByLightShape)
                        globalColorModulation *= GetLensFlareLightAttenuation(light, cam, -diffToObject.normalized);
                }

                globalColorModulation *= distanceAttenuation;

                Vector3 dir = (cam.transform.position - comp.transform.position).normalized;
                Vector3 screenPosZ = cam.WorldToViewportPoint(positionWS + dir * comp.occlusionOffset);
                Vector2 occlusionRadiusEdgeScreenPos0 = (Vector2)cam.WorldToViewportPoint(positionWS);
                Vector2 occlusionRadiusEdgeScreenPos1 = (Vector2)cam.WorldToViewportPoint(positionWS + cam.transform.up * comp.occlusionRadius);
                float occlusionRadius = (occlusionRadiusEdgeScreenPos1 - occlusionRadiusEdgeScreenPos0).magnitude;
                cmd.SetGlobalVector(_FlareData1, new Vector4(occlusionRadius, comp.sampleCount, screenPosZ.z, actualHeight / actualWidth));

                if (comp.useOcclusion)
                {
                    cmd.EnableShaderKeyword("FLARE_OCCLUSION");
                }
                else
                {
                    cmd.DisableShaderKeyword("FLARE_OCCLUSION");
                }

                foreach (LensFlareDataElementSRP element in data.elements)
                {
                    if (element == null ||
                        (element.lensFlareTexture == null && element.flareType == SRPLensFlareType.Image) ||
                        element.localIntensity <= 0.0f ||
                        element.count <= 0)
                        continue;

                    Color colorModulation = globalColorModulation;
                    if (light != null && element.modulateByLightColor)
                    {
                        if (light.useColorTemperature)
                            colorModulation *= light.color * Mathf.CorrelatedColorTemperatureToRGB(light.colorTemperature);
                        else
                            colorModulation *= light.color;
                    }

                    Color curColor = colorModulation;
                    Vector2 screenPos = new Vector2(2.0f * viewportPos.x - 1.0f, 1.0f - 2.0f * viewportPos.y);
                    Vector2 translationScale = new Vector2(element.translationScale.x, element.translationScale.y);
                    Texture texture = element.lensFlareTexture;
                    float elemAspectRatio = element.sizeXY.x / element.sizeXY.y;
                    float usedAspectRatio;
                    if (element.flareType == SRPLensFlareType.Image)
                        usedAspectRatio = element.preserveAspectRatio ? ((((float)texture.height) / (float)texture.width)) : 1.0f;
                    else
                        usedAspectRatio = 1.0f;

                    float rotation = element.rotation;
                    Vector4 tint = Vector4.Scale(element.tint, curColor);
                    Vector2 radPos = new Vector2(Mathf.Abs(screenPos.x), Mathf.Abs(screenPos.y));
                    float radius = Mathf.Max(radPos.x, radPos.y); // l1 norm (instead of l2 norm)
                    float radialsScaleRadius = comp.radialScreenAttenuationCurve.length > 0 ? comp.radialScreenAttenuationCurve.Evaluate(radius) : 1.0f;

                    Vector2 elemSizeXY;
                    if (element.preserveAspectRatio)
                    {
                        if (usedAspectRatio >= 1.0f)
                        {
                            elemSizeXY = new Vector2(element.sizeXY.x / usedAspectRatio, element.sizeXY.y);
                        }
                        else
                        {
                            elemSizeXY = new Vector2(element.sizeXY.x, element.sizeXY.y * usedAspectRatio);
                        }
                    }
                    else
                    {
                        elemSizeXY = new Vector2(element.sizeXY.x, element.sizeXY.y);
                    }
                    float scaleSize = 0.1f; // Arbitrary value
                    Vector2 size = new Vector2(elemSizeXY.x, elemSizeXY.y);
                    float combinedScale = scaleByDistance * scaleSize * element.uniformScale * comp.scale;
                    size *= combinedScale;

                    Vector4 gradientModulation = new Vector4(1.0f, 1.0f, 1.0f, 1.0f);

                    float currentIntensity = comp.intensity * element.localIntensity * radialsScaleRadius * distanceAttenuation;

                    if (currentIntensity <= 0.0f)
                        continue;

                    curColor *= element.tint;
                    curColor *= currentIntensity;

                    float globalCos0 = Mathf.Cos(-element.angularOffset * Mathf.Deg2Rad);
                    float globalSin0 = Mathf.Sin(-element.angularOffset * Mathf.Deg2Rad);

                    float position = 2.0f * element.position;

                    SRPLensFlareBlendMode blendMode = element.blendMode;
                    int materialPass;
                    if (blendMode == SRPLensFlareBlendMode.Additive)
                        materialPass = 0;
                    else if (blendMode == SRPLensFlareBlendMode.Screen)
                        materialPass = 1;
                    else if (blendMode == SRPLensFlareBlendMode.Premultiply)
                        materialPass = 2;
                    else if (blendMode == SRPLensFlareBlendMode.Lerp)
                        materialPass = 3;
                    else
                        materialPass = 0;

                    if (element.flareType == SRPLensFlareType.Image)
                    {
                        cmd.DisableShaderKeyword("FLARE_CIRCLE");
                        cmd.DisableShaderKeyword("FLARE_POLYGON");
                    }
                    else if (element.flareType == SRPLensFlareType.Circle)
                    {
                        cmd.EnableShaderKeyword("FLARE_CIRCLE");
                        cmd.DisableShaderKeyword("FLARE_POLYGON");
                    }
                    else if (element.flareType == SRPLensFlareType.Polygon)
                    {
                        cmd.DisableShaderKeyword("FLARE_CIRCLE");
                        cmd.EnableShaderKeyword("FLARE_POLYGON");
                    }

                    if (element.flareType == SRPLensFlareType.Circle ||
                        element.flareType == SRPLensFlareType.Polygon)
                    {
                        if (element.inverseSDF)
                        {
                            cmd.EnableShaderKeyword("FLARE_INVERSE_SDF");
                        }
                        else
                        {
                            cmd.DisableShaderKeyword("FLARE_INVERSE_SDF");
                        }
                    }
                    else
                    {
                        cmd.DisableShaderKeyword("FLARE_INVERSE_SDF");
                    }

                    if (element.lensFlareTexture != null)
                        cmd.SetGlobalTexture(_FlareTex, element.lensFlareTexture);

                    float usedGradientPosition = Mathf.Clamp01((1.0f - element.edgeOffset) - 1e-6f);
                    if (element.flareType == SRPLensFlareType.Polygon)
                        usedGradientPosition = Mathf.Pow(usedGradientPosition + 1.0f, 5);

                    Vector2 ComputeLocalSize(Vector2 rayOff, Vector2 rayOff0, Vector2 curSize, AnimationCurve distortionCurve)
                    {
                        Vector2 rayOffZ = GetLensFlareRayOffset(screenPos, position, globalCos0, globalSin0);
                        Vector2 localRadPos;
                        float localRadius;
                        if (!element.distortionRelativeToCenter)
                        {
                            localRadPos = (rayOff - rayOff0) * 0.5f;
                            localRadius = Mathf.Clamp01(Mathf.Max(Mathf.Abs(localRadPos.x), Mathf.Abs(localRadPos.y))); // l1 norm (instead of l2 norm)
                        }
                        else
                        {
                            localRadPos = screenPos + (rayOff + new Vector2(element.positionOffset.x, -element.positionOffset.y)) * element.translationScale;
                            localRadius = Mathf.Clamp01(localRadPos.magnitude); // l2 norm (instead of l1 norm)
                        }

                        float localLerpValue = Mathf.Clamp01(distortionCurve.Evaluate(localRadius));
                        return new Vector2(Mathf.Lerp(curSize.x, element.targetSizeDistortion.x * combinedScale / usedAspectRatio, localLerpValue),
                            Mathf.Lerp(curSize.y, element.targetSizeDistortion.y * combinedScale, localLerpValue));
                    }

                    float usedSDFRoundness = element.sdfRoundness;

                    cmd.SetGlobalVector(_FlareData5, new Vector4(comp.allowOffScreen ? 1.0f : -1.0f, usedGradientPosition, Mathf.Exp(Mathf.Lerp(0.0f, 4.0f, Mathf.Clamp01(1.0f - element.fallOff))), 0.0f));
                    if (element.flareType == SRPLensFlareType.Polygon)
                    {
                        float invSide = 1.0f / (float)element.sideCount;
                        float rCos = Mathf.Cos(Mathf.PI * invSide);
                        float roundValue = rCos * usedSDFRoundness;
                        float r = rCos - roundValue;
                        float an = 2.0f * Mathf.PI * invSide;
                        float he = r * Mathf.Tan(0.5f * an);
                        cmd.SetGlobalVector(_FlareData4, new Vector4(usedSDFRoundness, r, an, he));
                    }
                    else
                    {
                        cmd.SetGlobalVector(_FlareData4, new Vector4(usedSDFRoundness, 0.0f, 0.0f, 0.0f));
                    }

                    if (!element.allowMultipleElement || element.count == 1)
                    {
                        Vector4 flareData0 = GetFlareData0(screenPos, element.translationScale, vScreenRatio, element.rotation, position, element.angularOffset, element.positionOffset, element.autoRotate);

                        Vector2 rayOff = GetLensFlareRayOffset(screenPos, position, globalCos0, globalSin0);
                        Vector2 localSize = size;

                        if (element.enableRadialDistortion)
                        {
                            Vector2 rayOff0 = GetLensFlareRayOffset(screenPos, 0.0f, globalCos0, globalSin0);
                            localSize = ComputeLocalSize(rayOff, rayOff0, localSize, element.distortionCurve);
                        }

                        cmd.SetGlobalVector(_FlareData0, flareData0);
                        cmd.SetGlobalVector(_FlareData2, new Vector4(screenPos.x, screenPos.y, localSize.x, localSize.y));
                        cmd.SetGlobalVector(_FlareData3, new Vector4(rayOff.x * element.translationScale.x, rayOff.y * element.translationScale.y, 1.0f / (float)element.sideCount, 0.0f));
                        cmd.SetGlobalVector(_FlareColorValue, curColor);

                        UnityEngine.Rendering.Blitter.DrawQuad(cmd, lensFlareShader, materialPass);
                    }
                    else
                    {
                        float dLength = 2.0f * element.lengthSpread / ((float)(element.count - 1));

                        if (element.distribution == SRPLensFlareDistribution.Uniform)
                        {
                            for (int elemIdx = 0; elemIdx < element.count; ++elemIdx)
                            {
                                Vector2 rayOff = GetLensFlareRayOffset(screenPos, position, globalCos0, globalSin0);

                                Vector2 localSize = size;
                                if (element.enableRadialDistortion)
                                {
                                    Vector2 rayOff0 = GetLensFlareRayOffset(screenPos, 0.0f, globalCos0, globalSin0);
                                    localSize = ComputeLocalSize(rayOff, rayOff0, localSize, element.distortionCurve);
                                }

                                float timeScale = element.count >= 2 ? ((float)elemIdx) / ((float)(element.count - 1)) : 0.5f;

                                Color col = element.colorGradient.Evaluate(timeScale);

                                Vector4 flareData0 = GetFlareData0(screenPos, element.translationScale, vScreenRatio, element.rotation, position, element.angularOffset, element.positionOffset, element.autoRotate);
                                cmd.SetGlobalVector(_FlareData0, flareData0);
                                cmd.SetGlobalVector(_FlareData2, new Vector4(screenPos.x, screenPos.y, localSize.x, localSize.y));
                                cmd.SetGlobalVector(_FlareData3, new Vector4(rayOff.x * element.translationScale.x, rayOff.y * element.translationScale.y, 1.0f / (float)element.sideCount, 0.0f));
                                cmd.SetGlobalVector(_FlareColorValue, curColor * col);

                                UnityEngine.Rendering.Blitter.DrawQuad(cmd, lensFlareShader, materialPass);
                                position += dLength;
                            }
                        }
                        else if (element.distribution == SRPLensFlareDistribution.Random)
                        {
                            Random.State backupRandState = UnityEngine.Random.state;
                            Random.InitState(element.seed);
                            Vector2 side = new Vector2(Mathf.Sin(-element.angularOffset * Mathf.Deg2Rad), Mathf.Cos(-element.angularOffset * Mathf.Deg2Rad));
                            side *= element.positionVariation.y;
                            float RandomRange(float min, float max)
                            {
                                return Random.Range(min, max);
                            }

                            for (int elemIdx = 0; elemIdx < element.count; ++elemIdx)
                            {
                                float localIntensity = RandomRange(-1.0f, 1.0f) * element.intensityVariation + 1.0f;

                                Vector2 rayOff = GetLensFlareRayOffset(screenPos, position, globalCos0, globalSin0);
                                Vector2 localSize = size;
                                if (element.enableRadialDistortion)
                                {
                                    Vector2 rayOff0 = GetLensFlareRayOffset(screenPos, 0.0f, globalCos0, globalSin0);
                                    localSize = ComputeLocalSize(rayOff, rayOff0, localSize, element.distortionCurve);
                                }

                                localSize += localSize * (element.scaleVariation * RandomRange(-1.0f, 1.0f));

                                Color randCol = element.colorGradient.Evaluate(RandomRange(0.0f, 1.0f));

                                Vector2 localPositionOffset = element.positionOffset + RandomRange(-1.0f, 1.0f) * side;

                                float localRotation = element.rotation + RandomRange(-Mathf.PI, Mathf.PI) * element.rotationVariation;

                                if (localIntensity > 0.0f)
                                {
                                    Vector4 flareData0 = GetFlareData0(screenPos, element.translationScale, vScreenRatio, localRotation, position, element.angularOffset, localPositionOffset, element.autoRotate);
                                    cmd.SetGlobalVector(_FlareData0, flareData0);
                                    cmd.SetGlobalVector(_FlareData2, new Vector4(screenPos.x, screenPos.y, localSize.x, localSize.y));
                                    cmd.SetGlobalVector(_FlareData3, new Vector4(rayOff.x * element.translationScale.x, rayOff.y * element.translationScale.y, 1.0f / (float)element.sideCount, 0.0f));
                                    cmd.SetGlobalVector(_FlareColorValue, curColor * randCol * localIntensity);

                                    UnityEngine.Rendering.Blitter.DrawQuad(cmd, lensFlareShader, materialPass);
                                }

                                position += dLength;
                                position += 0.5f * dLength * RandomRange(-1.0f, 1.0f) * element.positionVariation.x;
                            }
                            Random.state = backupRandState;
                        }
                        else if (element.distribution == SRPLensFlareDistribution.Curve)
                        {
                            for (int elemIdx = 0; elemIdx < element.count; ++elemIdx)
                            {
                                float timeScale = element.count >= 2 ? ((float)elemIdx) / ((float)(element.count - 1)) : 0.5f;

                                Color col = element.colorGradient.Evaluate(timeScale);

                                float positionSpacing = element.positionCurve.length > 0 ? element.positionCurve.Evaluate(timeScale) : 1.0f;

                                float localPos = position + 2.0f * element.lengthSpread * positionSpacing;
                                Vector2 rayOff = GetLensFlareRayOffset(screenPos, localPos, globalCos0, globalSin0);
                                Vector2 localSize = size;
                                if (element.enableRadialDistortion)
                                {
                                    Vector2 rayOff0 = GetLensFlareRayOffset(screenPos, 0.0f, globalCos0, globalSin0);
                                    localSize = ComputeLocalSize(rayOff, rayOff0, localSize, element.distortionCurve);
                                }
                                float sizeCurveValue = element.scaleCurve.length > 0 ? element.scaleCurve.Evaluate(timeScale) : 1.0f;
                                localSize *= sizeCurveValue;

                                Vector4 flareData0 = GetFlareData0(screenPos, element.translationScale, vScreenRatio, element.rotation, localPos, element.angularOffset, element.positionOffset, element.autoRotate);
                                cmd.SetGlobalVector(_FlareData0, flareData0);
                                cmd.SetGlobalVector(_FlareData2, new Vector4(screenPos.x, screenPos.y, localSize.x, localSize.y));
                                cmd.SetGlobalVector(_FlareData3, new Vector4(rayOff.x * element.translationScale.x, rayOff.y * element.translationScale.y, 1.0f / (float)element.sideCount, 0.0f));
                                cmd.SetGlobalVector(_FlareColorValue, curColor * col);

                                UnityEngine.Rendering.Blitter.DrawQuad(cmd, lensFlareShader, materialPass);
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Remove a lens flare data which exist in the pool.
        /// </summary>
        /// <param name="data">The data which exist in the pool</param>
        public void RemoveData(LensFlareComponentSRP data)
        {
            Debug.Assert(Instance == this, "LensFlareCommonSRP can have only one instance");

            if (m_Data.Contains(data))
            {
                m_Data.Remove(data);
            }
        }

        #region Panini Projection
        static Vector2 DoPaniniProjection(Vector2 screenPos, float actualWidth, float actualHeight, float fieldOfView, float paniniProjectionCropToFit, float paniniProjectionDistance)
        {
            Vector2 viewExtents = CalcViewExtents(actualWidth, actualHeight, fieldOfView);
            Vector2 cropExtents = CalcCropExtents(actualWidth, actualHeight, fieldOfView, paniniProjectionDistance);

            float scaleX = cropExtents.x / viewExtents.x;
            float scaleY = cropExtents.y / viewExtents.y;
            float scaleF = Mathf.Min(scaleX, scaleY);

            float paniniD = paniniProjectionDistance;
            float paniniS = Mathf.Lerp(1.0f, Mathf.Clamp01(scaleF), paniniProjectionCropToFit);

            Vector2 pos = new Vector2(2.0f * screenPos.x - 1.0f, 2.0f * screenPos.y - 1.0f);

            Vector2 projPos = Panini_Generic_Inv(pos * viewExtents, paniniD) / (viewExtents * paniniS);

            return new Vector2(0.5f * projPos.x + 0.5f, 0.5f * projPos.y + 0.5f);
        }

        static Vector2 CalcViewExtents(float actualWidth, float actualHeight, float fieldOfView)
        {
            float fovY = fieldOfView * Mathf.Deg2Rad;
            float aspect = actualWidth / actualHeight;

            float viewExtY = Mathf.Tan(0.5f * fovY);
            float viewExtX = aspect * viewExtY;

            return new Vector2(viewExtX, viewExtY);
        }

        static Vector2 CalcCropExtents(float actualWidth, float actualHeight, float fieldOfView, float d)
        {
            // given
            //    S----------- E--X-------
            //    |    `  ~.  /,´
            //    |-- ---    Q
            //    |        ,/    `
            //  1 |      ,´/       `
            //    |    ,´ /         ´
            //    |  ,´  /           ´
            //    |,`   /             ,
            //    O    /
            //    |   /               ,
            //  d |  /
            //    | /                ,
            //    |/                .
            //    P
            //    |              ´
            //    |         , ´
            //    +-    ´
            //
            // have X
            // want to find E

            float viewDist = 1.0f + d;

            Vector2 projPos = CalcViewExtents(actualWidth, actualHeight, fieldOfView);
            float projHyp = Mathf.Sqrt(projPos.x * projPos.x + 1.0f);

            float cylDistMinusD = 1.0f / projHyp;
            float cylDist = cylDistMinusD + d;
            Vector2 cylPos = projPos * cylDistMinusD;

            return cylPos * (viewDist / cylDist);
        }

        static Vector2 Panini_Generic_Inv(Vector2 projPos, float d)
        {
            // given
            //    S----------- E--X-------
            //    |    `  ~.  /,´
            //    |-- ---    Q
            //    |        ,/    `
            //  1 |      ,´/       `
            //    |    ,´ /         ´
            //    |  ,´  /           ´
            //    |,`   /             ,
            //    O    /
            //    |   /               ,
            //  d |  /
            //    | /                ,
            //    |/                .
            //    P
            //    |              ´
            //    |         , ´
            //    +-    ´
            //
            // have X
            // want to find E

            float viewDist = 1.0f + d;
            float projHyp = Mathf.Sqrt(projPos.x * projPos.x + 1.0f);

            float cylDistMinusD = 1.0f / projHyp;
            float cylDist = cylDistMinusD + d;
            Vector2 cylPos = projPos * cylDistMinusD;

            return cylPos * (viewDist / cylDist);
        }

        #endregion
    }
}
