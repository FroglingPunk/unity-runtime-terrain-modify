using UnityEngine;
using UnityEngine.UI;

namespace TerrainModify.Example
{
    public class ExampleSoilWeight : MonoBehaviour
    {
        [SerializeField] private Transform[] _areaPoints;
        [SerializeField] private Text _textWeight;

        private TerrainRuntimeModify _terrainRuntimeModify;


        private void Start()
        {
            _terrainRuntimeModify = FindFirstObjectByType<TerrainRuntimeModify>();
        }

        private void FixedUpdate()
        {
            var points = new Vector3[_areaPoints.Length];
            for (var i = 0; i < _areaPoints.Length; i++)
            {
                points[i] = _areaPoints[i].position;
            }

            var weight = _terrainRuntimeModify.GetSoilWeight(points);
            _textWeight.text = weight.ToString();
        }
    }
}