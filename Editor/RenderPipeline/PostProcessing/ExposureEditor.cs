using Illusion.Rendering.PostProcessing;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;

namespace Illusion.Rendering.Editor
{
    [CustomEditor(typeof(Exposure))]
    internal sealed class ExposureEditor : VolumeComponentEditor
    {
        private SerializedDataParameter _mode;
        private SerializedDataParameter _meteringMode;

        private SerializedDataParameter _fixedExposure;
        private SerializedDataParameter _compensation;
        private SerializedDataParameter _limitMin;
        private SerializedDataParameter _limitMax;
        private SerializedDataParameter _curveMap;
        private SerializedDataParameter _curveMin;
        private SerializedDataParameter _curveMax;

        private SerializedDataParameter _adaptationMode;
        private SerializedDataParameter _adaptationSpeedDarkToLight;
        private SerializedDataParameter _adaptationSpeedLightToDark;

        private SerializedDataParameter _weightTextureMask;

        private SerializedDataParameter _histogramPercentages;
        private SerializedDataParameter _histogramCurveRemapping;

        // SerializedDataParameter _CenterAroundTarget;
        private SerializedDataParameter _proceduralCenter;
        private SerializedDataParameter _proceduralRadii;
        private SerializedDataParameter _proceduralSoftness;
        private SerializedDataParameter _proceduralMinIntensity;
        private SerializedDataParameter _proceduralMaxIntensity;

        private SerializedDataParameter _targetMidGray;

        private int _repaintsAfterChange;
        private int _settingsForDoubleRefreshHash;

        public override void OnEnable()
        {
            var o = new PropertyFetcher<Exposure>(serializedObject);

            _mode = Unpack(o.Find(x => x.mode));
            _meteringMode = Unpack(o.Find(x => x.meteringMode));

            _fixedExposure = Unpack(o.Find(x => x.fixedExposure));
            _compensation = Unpack(o.Find(x => x.compensation));
            _limitMin = Unpack(o.Find(x => x.limitMin));
            _limitMax = Unpack(o.Find(x => x.limitMax));
            _curveMap = Unpack(o.Find(x => x.curveMap));
            _curveMin = Unpack(o.Find(x => x.limitMinCurveMap));
            _curveMax = Unpack(o.Find(x => x.limitMaxCurveMap));

            _adaptationMode = Unpack(o.Find(x => x.adaptationMode));
            _adaptationSpeedDarkToLight = Unpack(o.Find(x => x.adaptationSpeedDarkToLight));
            _adaptationSpeedLightToDark = Unpack(o.Find(x => x.adaptationSpeedLightToDark));

            _weightTextureMask = Unpack(o.Find(x => x.weightTextureMask));

            _histogramPercentages = Unpack(o.Find(x => x.histogramPercentages));
            _histogramCurveRemapping = Unpack(o.Find(x => x.histogramUseCurveRemapping));

            // _CenterAroundTarget = Unpack(o.Find(x => x.centerAroundExposureTarget));
            _proceduralCenter = Unpack(o.Find(x => x.proceduralCenter));
            _proceduralRadii = Unpack(o.Find(x => x.proceduralRadii));
            _proceduralSoftness = Unpack(o.Find(x => x.proceduralSoftness));
            _proceduralMinIntensity = Unpack(o.Find(x => x.maskMinIntensity));
            _proceduralMaxIntensity = Unpack(o.Find(x => x.maskMaxIntensity));

            _targetMidGray = Unpack(o.Find(x => x.targetMidGray));
        }

        public override void OnInspectorGUI()
        {
            PropertyField(_mode);

            int mode = _mode.value.intValue;
            // if (mode == (int)ExposureMode.UsePhysicalCamera)
            // {
            //     PropertyField(_Compensation);
            // }
            // else 
            if (mode == (int)ExposureMode.Fixed)
            {
                DoExposurePropertyField(_fixedExposure);
                PropertyField(_compensation);
            }
            else
            {
                EditorGUILayout.Space();

                PropertyField(_meteringMode);
                if (_meteringMode.value.intValue == (int)MeteringMode.MaskWeighted)
                    PropertyField(_weightTextureMask);

                if (_meteringMode.value.intValue == (int)MeteringMode.ProceduralMask)
                {
                    // PropertyField(_CenterAroundTarget);

                    var centerLabel = EditorGUIUtility.TrTextContent("Center", "Sets the center of the procedural metering mask ([0,0] being bottom left of the screen and [1,1] top right of the screen)");
                    var centerValue = _proceduralCenter.value.vector2Value;

                    // if (_CenterAroundTarget.value.boolValue)
                    // {
                    //     centerLabel = EditorGUIUtility.TrTextContent("Offset", "Sets an offset to the mask center");
                    //     _ProceduralCenter.value.vector2Value = new Vector2(Mathf.Clamp(centerValue.x, -0.5f, 0.5f), Mathf.Clamp(centerValue.y, -0.5f, 0.5f));
                    // }
                    // else
                    {
                        _proceduralCenter.value.vector2Value = new Vector2(Mathf.Clamp01(centerValue.x), Mathf.Clamp01(centerValue.y));
                    }

                    PropertyField(_proceduralCenter, centerLabel);
                    var radiiValue = _proceduralRadii.value.vector2Value;
                    _proceduralRadii.value.vector2Value = new Vector2(Mathf.Clamp01(radiiValue.x), Mathf.Clamp01(radiiValue.y));
                    PropertyField(_proceduralRadii, EditorGUIUtility.TrTextContent("Radius", "Sets the radius of the procedural mask, in terms of fraction of the screen (i.e. 0.5 means a radius that stretch half of the screen)."));
                    PropertyField(_proceduralSoftness, EditorGUIUtility.TrTextContent("Softness", "Sets the softness of the mask, the higher the value the less influence is given to pixels at the edge of the mask"));
                    PropertyField(_proceduralMinIntensity);
                    PropertyField(_proceduralMaxIntensity);

                    EditorGUILayout.Space();
                }

                // if (mode == (int)ExposureMode.CurveMapping)
                // {
                //     PropertyField(_CurveMap);
                //     PropertyField(_CurveMin, EditorGUIUtility.TrTextContent("Limit Min"));
                //     PropertyField(_CurveMax, EditorGUIUtility.TrTextContent("Limit Max"));
                // }
                // else 
                if (!(mode == (int)ExposureMode.AutomaticHistogram && _histogramCurveRemapping.value.boolValue))
                {
                    DoExposurePropertyField(_limitMin);
                    DoExposurePropertyField(_limitMax);
                }

                PropertyField(_compensation);

                if (mode == (int)ExposureMode.AutomaticHistogram)
                {
                    PropertyField(_histogramPercentages);
                    PropertyField(_histogramCurveRemapping, EditorGUIUtility.TrTextContent("Use Curve Remapping"));
                    if (_histogramCurveRemapping.value.boolValue)
                    {
                        PropertyField(_curveMap);
                        PropertyField(_curveMin, EditorGUIUtility.TrTextContent("Limit Min"));
                        PropertyField(_curveMax, EditorGUIUtility.TrTextContent("Limit Max"));
                    }
                }

                PropertyField(_adaptationMode, EditorGUIUtility.TrTextContent("Mode"));

                if (_adaptationMode.value.intValue == (int)AdaptationMode.Progressive)
                {
                    PropertyField(_adaptationSpeedDarkToLight, EditorGUIUtility.TrTextContent("Speed Dark to Light"));
                    PropertyField(_adaptationSpeedLightToDark, EditorGUIUtility.TrTextContent("Speed Light to Dark"));
                }

                PropertyField(_targetMidGray, EditorGUIUtility.TrTextContent("Target Mid Grey", "Sets the desired Mid gray level used by the auto exposure (i.e. to what grey value the auto exposure system maps the average scene luminance)."));
            }

            // Since automatic exposure works on 2 frames (automatic exposure is computed from previous frame data), we need to trigger the scene repaint twice if
            // some of the changes that will lead to different results are changed.
            int automaticCurrSettingHash = _limitMin.value.floatValue.GetHashCode() +
                17 * _limitMax.value.floatValue.GetHashCode() +
                17 * _compensation.value.floatValue.GetHashCode();

            if (
                // mode == (int)ExposureMode.Automatic || 
                mode == (int)ExposureMode.AutomaticHistogram)
            {
                if (automaticCurrSettingHash != _settingsForDoubleRefreshHash)
                {
                    _repaintsAfterChange = 2;
                }
                else
                {
                    _repaintsAfterChange = Mathf.Max(0, _repaintsAfterChange - 1);
                }
                _settingsForDoubleRefreshHash = automaticCurrSettingHash;

                if (_repaintsAfterChange > 0)
                {
                    SceneView.RepaintAll();
                }
            }
        }

        // TODO: See if this can be refactored into a custom VolumeParameterDrawer
        private void DoExposurePropertyField(SerializedDataParameter exposureProperty)
        {
            using (var scope = new OverridablePropertyScope(exposureProperty, exposureProperty.displayName, this))
            {
                if (!scope.displayed)
                    return;

                using (new EditorGUILayout.VerticalScope())
                {
                    EditorGUILayout.LabelField(scope.label);

                    var xOffset = EditorGUIUtility.labelWidth + 2;

                    var lineRect = EditorGUILayout.GetControlRect();
                    lineRect.x += xOffset;
                    lineRect.width -= xOffset;

                    var sliderRect = lineRect;
                    sliderRect.y -= EditorGUIUtility.singleLineHeight;
                    LightSliderUIDrawer.SetSerializedObject(serializedObject);
                    LightSliderUIDrawer.DrawExposureSlider(exposureProperty.value, sliderRect);

                    // GUIContent.none disables horizontal scrolling, use TrTextContent and adjust the rect to make it work.
                    lineRect.x -= EditorGUIUtility.labelWidth + 2;
                    lineRect.y += EditorGUIUtility.standardVerticalSpacing;
                    lineRect.width += EditorGUIUtility.labelWidth + 2;
                    EditorGUI.PropertyField(lineRect, exposureProperty.value, EditorGUIUtility.TrTextContent(" "));
                }
            }
        }
    }
}