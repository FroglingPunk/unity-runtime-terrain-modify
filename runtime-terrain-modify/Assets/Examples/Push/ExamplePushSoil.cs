using UnityEngine;

namespace TerrainModify.Example
{
    public class ExamplePushSoil : MonoBehaviour
    {
        [SerializeField] private Transform[] _bladeAreaPoints;
        [SerializeField] private float _forwardHeapLength = 2f;

        private TerrainRuntimeModify _terrainRuntimeModify;


        private void Start()
        {
            _terrainRuntimeModify = FindFirstObjectByType<TerrainRuntimeModify>();
        }

        private void FixedUpdate()
        {
            var bladePoints = new Vector3[_bladeAreaPoints.Length];
            for (var i = 0; i < _bladeAreaPoints.Length; i++)
            {
                bladePoints[i] = _bladeAreaPoints[i].position;
            }

            _terrainRuntimeModify.SoilPush(bladePoints, _forwardHeapLength);
        }
    }
}