using UnityEngine;

namespace TerrainModify.Example
{
    public class ExampleSoilCollapse : MonoBehaviour
    {
        [SerializeField] private float _collapseRadius = 10f;
        [SerializeField] private Transform[] _uncollapsedAreaPoints;

        private TerrainRuntimeModify _terrainRuntimeModify;


        private void Start()
        {
            _terrainRuntimeModify = FindFirstObjectByType<TerrainRuntimeModify>();
        }

        private void Update()
        {
            _terrainRuntimeModify.LayersIgnoreSoilCollapse(transform.position,
                            new[]
                            {
                                _uncollapsedAreaPoints[0].position,
                                _uncollapsedAreaPoints[1].position,
                                _uncollapsedAreaPoints[2].position,
                                _uncollapsedAreaPoints[3].position
                            },
                             _collapseRadius);
        }
    }
}