using UnityEngine;

namespace TerrainModify
{
    public static class MathfExtensions
    {
        /// <summary>
        /// Нахождение минимальных и максимальных значений каждой оси среди множества векторов
        /// </summary>
        public static void FindMinMaxVector(this Vector3[] vectors, out Vector3 min, out Vector3 max)
        {
            if (vectors.Length < 2)
            {
                min = max = Vector3.zero;
                return;
            }

            min = vectors[0];
            max = vectors[1];

            for (var i = 0; i < vectors.Length; i++)
            {
                var point = vectors[i];
                if (point.x < min.x)
                {
                    min.x = point.x;
                }
                else if (point.x > max.x)
                {
                    max.x = point.x;
                }

                if (point.z < min.z)
                {
                    min.z = point.z;
                }
                else if (point.z > max.z)
                {
                    max.z = point.z;
                }
            }
        }

        /// <summary>
        /// Нахождение минимальных и максимальных значений каждой оси среди множества векторов
        /// </summary>
        public static void FindMinMaxVector(this Vector2Int[] vectors, out Vector2Int min, out Vector2Int max)
        {
            if (vectors.Length < 2)
            {
                min = max = Vector2Int.zero;
                return;
            }

            min = vectors[0];
            max = vectors[1];

            for (var i = 0; i < vectors.Length; i++)
            {
                var point = vectors[i];
                if (point.x < min.x)
                {
                    min.x = point.x;
                }
                else if (point.x > max.x)
                {
                    max.x = point.x;
                }

                if (point.y < min.y)
                {
                    min.y = point.y;
                }
                else if (point.y > max.y)
                {
                    max.y = point.y;
                }
            }
        }

        /// <summary>
        /// Находится ли точка в треугольнике
        /// </summary>
        public static bool PointInTriangle(int tp1X, int tp1Y, int tp2X, int tp2Y, int tp3X, int tp3Y, int pointX, int pointY)
        {
            var a = (tp1X - pointX) * (tp2Y - tp1Y) - (tp2X - tp1X) * (tp1Y - pointY);
            var b = (tp2X - pointX) * (tp3Y - tp2Y) - (tp3X - tp2X) * (tp2Y - pointY);
            var c = (tp3X - pointX) * (tp1Y - tp3Y) - (tp1X - tp3X) * (tp3Y - pointY);

            return (a >= 0 && b >= 0 && c >= 0) || (a <= 0 && b <= 0 && c <= 0);
        }
    }
}