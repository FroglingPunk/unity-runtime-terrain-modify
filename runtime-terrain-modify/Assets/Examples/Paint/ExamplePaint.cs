using UnityEngine;

namespace TerrainModify.Example
{
    public class ExamplePaint : MonoBehaviour
    {
        [SerializeField] private Transform[] _paintAreaPoints;
        [SerializeField] private int _paintLayerId = 1;

        private TerrainRuntimeModify _terrainRuntimeModify;


        private void Start()
        {
            _terrainRuntimeModify = FindFirstObjectByType<TerrainRuntimeModify>();
        }

        private void FixedUpdate()
        {
            var paintPoints = new Vector3[_paintAreaPoints.Length];
            for (var i = 0; i < _paintAreaPoints.Length; i++)
            {
                paintPoints[i] = _paintAreaPoints[i].position;
            }

            _terrainRuntimeModify.Paint(paintPoints, _paintLayerId);
        }
    }
}