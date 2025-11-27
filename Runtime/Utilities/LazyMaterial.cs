/*
 * StarRailNPRShader - Fan-made shaders for Unity URP attempting to replicate
 * the shading of Honkai: Star Rail.
 * https://github.com/stalomeow/StarRailNPRShader
 *
 * Copyright (C) 2023 Stalo <stalowork@163.com>
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this program. If not, see <https://www.gnu.org/licenses/>.
 */

using UnityEngine;
using UnityEngine.Rendering;

namespace Illusion.Rendering
{
    public class LazyMaterial
    {
        private readonly string _shaderName;

        private Material _material;

        public LazyMaterial(string shaderName)
        {
            _shaderName = shaderName;
        }

        public Material Value
        {
            get
            {
                if (_material == null)
                {
                    var shader = Shader.Find(_shaderName);
                    _material = CoreUtils.CreateEngineMaterial(shader);
                }

                return _material;
            }
        }

        public void DestroyCache()
        {
            CoreUtils.Destroy(_material);
            _material = null;
        }
    }
}
