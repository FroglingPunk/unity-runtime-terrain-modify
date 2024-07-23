using System.Collections.Generic;
using TerrainModify;
using UnityEngine;

[RequireComponent(typeof(Terrain))]
public class TerrainRuntimeModify : MonoBehaviour
{
    // Плотность грунта в кг/мкуб
    [SerializeField] private float _soilDensity = 2200f;
    // Толщина плотного слоя в world-метрах
    [SerializeField] private float _denseSoilLayerThickness = 0.5f;
    // Множитель сыпучести грунта
    [SerializeField] private float _flowFactor = 1f;

    private Terrain _terrain;
    private TerrainData _terrainData;
    private Vector3 _terrainSize;
    private int _heightmapResolution;
    private int _alphamapResolution;
    private int _alphamapLayersCount;
    private Vector3 _terrainPosition;

    private float[,] _immutableHeightsmap;
    private float[,] _denseHeightsmap;
    private float[,] _looseHeightmap;

    private float this[int h, int w] => _looseHeightmap[h, w] + _denseHeightsmap[h, w] + _immutableHeightsmap[h, w];


    private void Start()
    {
        _terrain = GetComponent<Terrain>();
        var terrainDataCopy = Instantiate(_terrain.terrainData);

        _terrain.terrainData = terrainDataCopy;
        var terrainCollider = GetComponent<TerrainCollider>();
        terrainCollider.terrainData = terrainDataCopy;

        _terrainData = terrainDataCopy;
        _heightmapResolution = _terrainData.heightmapResolution;
        _alphamapResolution = _terrainData.alphamapResolution;
        _alphamapLayersCount = _terrainData.alphamapLayers;
        _terrainSize = _terrainData.size;

        _terrainPosition = transform.position;

        // Карта высот делится на 3 слоя, сумма которых равна карте высот.
        // Данный подход пока реализуется только в ComplexModifySnowcat

        // _immutable - неизменяемый слой, не осыпается, не копается, учитывается при подсчёте веса
        // Всегда равен стартовому значению карты высот с вычтенной толщиной плотного слоя
        _immutableHeightsmap = _terrainData.GetHeights(0, 0, _heightmapResolution, _heightmapResolution);

        // _dense - плотный слой, не осыпается, можно копать, но выталкиваемые значения преобразуются в рыхлый слой, учитывается при подсчёте веса
        // При старте равен значению параметра толщины плотного слоя, в дальнейшем может только уменьшаться
        _denseHeightsmap = new float[_heightmapResolution, _heightmapResolution];

        // _loose - рыхлый слой, осыпается, можно копать, выталкиваемые значения также передаются в рыхлые слои, учитывается при подсчёте веса
        // При старте равен нулю, в дальнейшем можен меняться в результате копания и осыпания 
        _looseHeightmap = new float[_heightmapResolution, _heightmapResolution];


        var localTerrainDenseSoilLayerThickness = ConvertWorldToTerrainRelativePosition(new Vector3(0, _denseSoilLayerThickness, 0)).y;

        for (var h = 0; h < _heightmapResolution; h++)
        {
            for (var w = 0; w < _heightmapResolution; w++)
            {
                if (_immutableHeightsmap[h, w] < localTerrainDenseSoilLayerThickness)
                {
                    _denseHeightsmap[h, w] = _immutableHeightsmap[h, w];
                    _immutableHeightsmap[h, w] = 0;
                }
                else
                {
                    _denseHeightsmap[h, w] = localTerrainDenseSoilLayerThickness;
                    _immutableHeightsmap[h, w] -= localTerrainDenseSoilLayerThickness;
                }
            }
        }
    }


    #region API Modify

    /// <summary>
    /// Толкание грунта
    /// </summary>
    /// <param name="bladeRectWorldPoints">Точки четырёхугольных областей из которых выталкивается грунт</param>
    /// <param name="forwardHeapLength">Дистанция перед областями на которую выталкивается грунт</param>
    public void SoilPush(Vector3[] bladeRectWorldPoints, float forwardHeapLength)
    {
        // Массивы точек обязательно должны представлять четырёхугольники
        if (bladeRectWorldPoints.Length % 4 != 0)
        {
            return;
        }

        var bladeWorldPointsCount = bladeRectWorldPoints.Length;
        var rectsCount = bladeWorldPointsCount / 4;

        // для каждого четырёхугольника blade подбираются ещё 2 точки (EFL, EFR) что находятся на отдалении forwardHeapLength от двух передних точек (FL, FR)
        // и создаётся новый массив который хранит уже все точки
        // в массиве эти точки хранятся следующим образом :
        // FL|BL|FR|BR|...|EFL|EFR|...
        // где |...| это повторения прошлых точек в зависимости от количества четырёхугольников
        var allBladePoints = new Vector3[bladeWorldPointsCount + bladeWorldPointsCount / 2];

        for (var i = 0; i < bladeRectWorldPoints.Length; i += 2)
        {
            allBladePoints[i] = bladeRectWorldPoints[i];
            allBladePoints[i + 1] = bladeRectWorldPoints[i + 1];

            allBladePoints[i / 2 + bladeWorldPointsCount] = bladeRectWorldPoints[i] + (bladeRectWorldPoints[i] - bladeRectWorldPoints[i + 1]).normalized * forwardHeapLength;
        }

        allBladePoints.FindMinMaxVector(out var bottomLeftPoint, out var topRightPoint);

        bottomLeftPoint = ClampPointPositionInTerrain(bottomLeftPoint);
        topRightPoint = ClampPointPositionInTerrain(topRightPoint);

        var heightmapWidth = (int)(Mathf.Abs(topRightPoint.x - bottomLeftPoint.x) / _terrainSize.x * _heightmapResolution);
        var heightmapHeight = (int)(Mathf.Abs(topRightPoint.z - bottomLeftPoint.z) / _terrainSize.z * _heightmapResolution);

        CreateTerrainRectPointIds(out var tpx, out var tpy, bottomLeftPoint, 0, 0, _heightmapResolution);

        // для всех точек что хранят координаты WorldPosition находятся их X:Y в двумерном массиве карты высот террейна
        // в массиве эти точки хранятся следующим образом :
        // FLX|FLY|BLX|BLY|FRX|FRY|BRX|BRY|...|EFLX|EFLY|EFRX|EFRY|...
        // где |...| это повторения прошлых точек в зависимости от количества четырёхугольников
        var terrainPoints = new int[allBladePoints.Length * 2];
        for (var i = 0; i < allBladePoints.Length; i++)
        {
            CreateTerrainRectPointIds(out var x, out var y, allBladePoints[i], tpx, tpy, _heightmapResolution);
            terrainPoints[i * 2] = x;
            terrainPoints[i * 2 + 1] = y;
        }

        // Заранее находим все точки что входят в область перед каждым blade на отдалении forwardHeapLength
        var pointsInExtForward = new List<List<int>>(rectsCount);

        for (var i = 0; i < bladeWorldPointsCount; i += 4)
        {
            var flx = terrainPoints[i * 2];
            var fly = terrainPoints[i * 2 + 1];

            var frx = terrainPoints[i * 2 + 4];
            var fry = terrainPoints[i * 2 + 5];

            var eflx = terrainPoints[bladeWorldPointsCount * 2 + i];
            var efly = terrainPoints[bladeWorldPointsCount * 2 + i + 1];
            var efrx = terrainPoints[bladeWorldPointsCount * 2 + i + 2];
            var efry = terrainPoints[bladeWorldPointsCount * 2 + i + 3];

            // находим область в массиве в которой находятся точки области кучи перед blade
            new Vector2Int[] { new(flx, fly), new(frx, fry), new(eflx, efly), new(efrx, efry) }.FindMinMaxVector(out var min, out var max);

            var list = new List<int>(512);

            for (int localH = min.y, globalH = localH + tpy; localH < max.y; localH++, globalH++)
            {
                for (int localW = min.x, globalW = localW + tpx; localW < max.x; localW++, globalW++)
                {
                    if (MathfExtensions.PointInTriangle(flx, fly, frx, fry, eflx, efly, localW, localH) ||
                        MathfExtensions.PointInTriangle(frx, fry, efrx, efry, eflx, efly, localW, localH))
                    {
                        list.Add(localH * heightmapWidth + localW);
                    }
                }
            }

            pointsInExtForward.Add(list);
        }

        // Заранее находим все точки что входят в область каждого blade
        var pointsInBladeRect = new List<List<int>>(rectsCount);

        for (var i = 0; i < bladeWorldPointsCount; i += 4)
        {
            var flx = terrainPoints[i * 2];
            var fly = terrainPoints[i * 2 + 1];
            var blx = terrainPoints[i * 2 + 2];
            var bly = terrainPoints[i * 2 + 3];
            var frx = terrainPoints[i * 2 + 4];
            var fry = terrainPoints[i * 2 + 5];
            var brx = terrainPoints[i * 2 + 6];
            var bry = terrainPoints[i * 2 + 7];

            // Чтобы не проходится по всей карте высот, которая изначально подбиралась по размерам радиуса осыпания грунта,
            // находим область в массиве в которой находятся точки blade области
            new Vector2Int[] { new(flx, fly), new(frx, fry), new(blx, bly), new(brx, bry) }.FindMinMaxVector(out var min, out var max);

            var list = new List<int>(512);

            for (var h = min.y; h < max.y; h++)
            {
                for (var w = min.x; w < max.x; w++)
                {
                    if (MathfExtensions.PointInTriangle(flx, fly, frx, fry, blx, bly, w, h) ||
                        MathfExtensions.PointInTriangle(frx, fry, brx, bry, blx, bly, w, h))
                    {
                        list.Add(h * heightmapWidth + w);
                    }
                }
            }

            pointsInBladeRect.Add(list);
        }

        // Толкание грунта
        for (var i = 0; i < bladeWorldPointsCount; i += 4)
        {
            var desiredHeight = ConvertWorldToTerrainRelativePosition((bladeRectWorldPoints[i] + bladeRectWorldPoints[i + 2]) * 0.5f).y;
            var totalDeltaHeight = 0f;

            // Находим сумму всех "излишков" высот у точек области blade, которые выше desiredHeight
            var bladePointsList = pointsInBladeRect[i / 4];
            for (var p = 0; p < bladePointsList.Count; p++)
            {
                var idH = bladePointsList[p] / heightmapWidth;
                var idW = bladePointsList[p] % heightmapWidth;

                var mutableHeightLimit = desiredHeight - _immutableHeightsmap[tpy + idH, tpx + idW];

                if (mutableHeightLimit <= 0f)
                {
                    continue;
                }

                var denseAndLooseHeight = _denseHeightsmap[idH + tpy, idW + tpx] + _looseHeightmap[idH + tpy, idW + tpx];

                if (denseAndLooseHeight <= mutableHeightLimit)
                {
                    continue;
                }

                var delta = denseAndLooseHeight - mutableHeightLimit;

                totalDeltaHeight += delta;

                if (mutableHeightLimit >= _denseHeightsmap[idH + tpy, idW + tpx])
                {
                    _looseHeightmap[idH + tpy, idW + tpx] -= delta;
                }
                else
                {
                    _denseHeightsmap[idH + tpy, idW + tpx] = mutableHeightLimit;
                    _looseHeightmap[idH + tpy, idW + tpx] = 0f;
                }
            }

            if (totalDeltaHeight == 0)
            {
                continue;
            }

            // Распределяем собранные "излишки" высот в области перед каждым blade
            var heapPointsList = pointsInExtForward[i / 4];
            var averageHeightForForwardExt = totalDeltaHeight / heapPointsList.Count;

            for (var p = 0; p < heapPointsList.Count; p++)
            {
                var idH = heapPointsList[p] / heightmapWidth;
                var idW = heapPointsList[p] % heightmapWidth;

                _looseHeightmap[tpy + idH, tpx + idW] += averageHeightForForwardExt;
            }
        }

        var heights = new float[heightmapHeight, heightmapWidth];
        for (var h = 0; h < heightmapHeight; h++)
        {
            for (var w = 0; w < heightmapWidth; w++)
            {
                heights[h, w] = _immutableHeightsmap[h + tpy, w + tpx] + _denseHeightsmap[h + tpy, w + tpx] + _looseHeightmap[h + tpy, w + tpx];
            }
        }

        _terrainData.SetHeightsDelayLOD(tpx, tpy, heights);
    }

    /// <summary>
    /// Осыпание грунта
    /// </summary>
    /// <param name="collapseCenterPoint">Точка, вокруг которой осыпается грунт</param>
    /// <param name="uncollapsableAreaWorldPoints">Точки четырёхугольных областей в которые грунт не осыпается</param>
    /// <param name="collapseRadius">Радиус осыпания грунта</param>
    public void SoilCollapse(Vector3 collapseCenterPoint, Vector3[] uncollapsableAreaWorldPoints, float collapseRadius)
    {
        // Массивы точек обязательно должны представлять четырёхугольники
        if (uncollapsableAreaWorldPoints.Length % 4 != 0)
        {
            return;
        }

        // Получаем id крайних точек карты высот в которых проходит осыпание
        var bottomLeftPoint = new Vector3(collapseCenterPoint.x - collapseRadius, 0, collapseCenterPoint.z - collapseRadius);
        var topRightPoint = new Vector3(collapseCenterPoint.x + collapseRadius, 0, collapseCenterPoint.z + collapseRadius);

        bottomLeftPoint = ClampPointPositionInTerrain(bottomLeftPoint);
        topRightPoint = ClampPointPositionInTerrain(topRightPoint);

        var heightmapWidth = (int)(Mathf.Abs(topRightPoint.x - bottomLeftPoint.x) / _terrainSize.x * _heightmapResolution);
        var heightmapHeight = (int)(Mathf.Abs(topRightPoint.z - bottomLeftPoint.z) / _terrainSize.z * _heightmapResolution);

        CreateTerrainRectPointIds(out var tpx, out var tpy, bottomLeftPoint, 0, 0, _heightmapResolution);

        // По все области осыпания отмечаем точки что доступны и недоступны для осыпания
        var maxDeltaH = _terrainSize.x / _heightmapResolution / _terrainSize.y / _flowFactor;
        var heightsInUa = new bool[heightmapHeight, heightmapWidth];

        for (var i = 0; i < uncollapsableAreaWorldPoints.Length; i += 4)
        {
            var uncollapsableAreaTerrainPoints = new int[8];
            CreateTerrainRectPointIds(out uncollapsableAreaTerrainPoints[0], out uncollapsableAreaTerrainPoints[1], uncollapsableAreaWorldPoints[i], tpx, tpy, _heightmapResolution);
            CreateTerrainRectPointIds(out uncollapsableAreaTerrainPoints[2], out uncollapsableAreaTerrainPoints[3], uncollapsableAreaWorldPoints[i + 1], tpx, tpy, _heightmapResolution);
            CreateTerrainRectPointIds(out uncollapsableAreaTerrainPoints[4], out uncollapsableAreaTerrainPoints[5], uncollapsableAreaWorldPoints[i + 2], tpx, tpy, _heightmapResolution);
            CreateTerrainRectPointIds(out uncollapsableAreaTerrainPoints[6], out uncollapsableAreaTerrainPoints[7], uncollapsableAreaWorldPoints[i + 3], tpx, tpy, _heightmapResolution);

            // находим область в массиве в которой находятся точки области недоступной для осыпания
            new Vector2Int[]
                {
                    new(uncollapsableAreaTerrainPoints[0], uncollapsableAreaTerrainPoints[1]),
                    new(uncollapsableAreaTerrainPoints[2], uncollapsableAreaTerrainPoints[3]),
                    new(uncollapsableAreaTerrainPoints[4], uncollapsableAreaTerrainPoints[5]),
                    new(uncollapsableAreaTerrainPoints[6], uncollapsableAreaTerrainPoints[7])
                }
                .FindMinMaxVector(out var min, out var max);

            for (var h = min.y; h < max.y; h++)
            {
                for (var w = min.x; w < max.x; w++)
                {
                    if (MathfExtensions.PointInTriangle(uncollapsableAreaTerrainPoints[0], uncollapsableAreaTerrainPoints[1], uncollapsableAreaTerrainPoints[2], uncollapsableAreaTerrainPoints[3], uncollapsableAreaTerrainPoints[4], uncollapsableAreaTerrainPoints[5], w, h) ||
                        MathfExtensions.PointInTriangle(uncollapsableAreaTerrainPoints[0], uncollapsableAreaTerrainPoints[1], uncollapsableAreaTerrainPoints[4], uncollapsableAreaTerrainPoints[5], uncollapsableAreaTerrainPoints[6], uncollapsableAreaTerrainPoints[7], w, h))
                    {
                        heightsInUa[h, w] = true;
                    }
                }
            }
        }

        var collapsedPoints = new Dictionary<int, float>();

        // Осыпание грунта
        for (var h = tpy; h < tpy + heightmapHeight; h++)
        {
            for (var w = tpx; w < tpx + heightmapWidth; w++)
            {
                if (heightsInUa[h - tpy, w - tpx])
                {
                    continue;
                }

                collapsedPoints.Clear();

                var centerPoint = _looseHeightmap[h, w];

                // Формула подсчёта необходимой дополнительной высоты для соседней точки, чтобы разницы между центральной и соседней была в рамках допустимого и не осыпалась
                // В дальнейшем это термин НДВ - необходимая дополнительная высота
                // НДВ = (centerPoint - sidePoint - maxDeltaH) / 2f

                if (h - tpy > 0 && !heightsInUa[h - tpy - 1, w - tpx])
                {
                    var sidePoint = _looseHeightmap[h - 1, w];
                    if (centerPoint - sidePoint > maxDeltaH)
                    {
                        collapsedPoints.Add(w + (h - 1) * _heightmapResolution, (centerPoint - sidePoint - maxDeltaH) / 2f);
                    }

                    if (w - tpx > 0 && !heightsInUa[h - tpy - 1, w - tpx - 1])
                    {
                        sidePoint = _looseHeightmap[h - 1, w - 1];
                        if (centerPoint - sidePoint > maxDeltaH)
                        {
                            collapsedPoints.Add(w - 1 + (h - 1) * _heightmapResolution, (centerPoint - sidePoint - maxDeltaH) / 2f);
                        }
                    }

                    if (w - tpx < heightmapWidth - 1 && !heightsInUa[h - tpy - 1, w - tpx + 1])
                    {
                        sidePoint = _looseHeightmap[h - 1, w + 1];
                        if (centerPoint - sidePoint > maxDeltaH)
                        {
                            collapsedPoints.Add(w + 1 + (h - 1) * _heightmapResolution, (centerPoint - sidePoint - maxDeltaH) / 2f);
                        }
                    }
                }

                if (h - tpy < heightmapHeight - 1 && !heightsInUa[h - tpy + 1, w - tpx])
                {
                    var sidePoint = _looseHeightmap[h + 1, w];
                    if (centerPoint - sidePoint > maxDeltaH)
                    {
                        collapsedPoints.Add(w + (h + 1) * _heightmapResolution, (centerPoint - sidePoint - maxDeltaH) / 2f);
                    }

                    if (w - tpx > 0 && !heightsInUa[h - tpy + 1, w - tpx - 1])
                    {
                        sidePoint = _looseHeightmap[h + 1, w - 1];
                        if (centerPoint - sidePoint > maxDeltaH)
                        {
                            collapsedPoints.Add(w - 1 + (h + 1) * _heightmapResolution, (centerPoint - sidePoint - maxDeltaH) / 2f);
                        }
                    }

                    if (w - tpx < heightmapWidth - 1 && !heightsInUa[h - tpy + 1, w - tpx + 1])
                    {
                        sidePoint = _looseHeightmap[h + 1, w + 1];
                        if (centerPoint - sidePoint > maxDeltaH)
                        {
                            collapsedPoints.Add(w + 1 + (h + 1) * _heightmapResolution, (centerPoint - sidePoint - maxDeltaH) / 2f);
                        }
                    }
                }

                if (w - tpx > 0 && !heightsInUa[h - tpy, w - tpx - 1])
                {
                    var sidePoint = _looseHeightmap[h, w - 1];
                    if (centerPoint - sidePoint > maxDeltaH)
                    {
                        collapsedPoints.Add(w - 1 + h * _heightmapResolution, (centerPoint - sidePoint - maxDeltaH) / 2f);
                    }
                }

                if (w - tpx < heightmapWidth - 1 && !heightsInUa[h - tpy, w - tpx + 1])
                {
                    var sidePoint = _looseHeightmap[h, w + 1];
                    if (centerPoint - sidePoint > maxDeltaH)
                    {
                        collapsedPoints.Add(w + 1 + h * _heightmapResolution, (centerPoint - sidePoint - maxDeltaH) / 2f);
                    }
                }

                if (collapsedPoints.Count == 0)
                {
                    continue;
                }

                // суммарная НДВ для всех соседних точек
                var totalNeccessaryAddHeight = 0f;
                // минимальная разница в высоте с соседней точкой, в отличии от НДВ это просто дельта высот
                var minNecessaryAddHeightWithSidePoint = float.MaxValue;

                foreach (var cpv in collapsedPoints.Values)
                {
                    totalNeccessaryAddHeight += cpv;

                    if (cpv < minNecessaryAddHeightWithSidePoint)
                    {
                        minNecessaryAddHeightWithSidePoint = cpv;
                    }
                }

                // минимальная НДВ у соседней точки
                var minDeltaHeightWithSidePoint = (minNecessaryAddHeightWithSidePoint * 2) + maxDeltaH;
                // вычитаемая высота из центральной точки
                var centerPointLostHeight = minDeltaHeightWithSidePoint * totalNeccessaryAddHeight / (minNecessaryAddHeightWithSidePoint + totalNeccessaryAddHeight);

                foreach (var cpk in collapsedPoints.Keys)
                {
                    var addHeightForCollapsedPoint = centerPointLostHeight / totalNeccessaryAddHeight * collapsedPoints[cpk];
                    _looseHeightmap[cpk / _heightmapResolution, cpk % _heightmapResolution] += addHeightForCollapsedPoint;

                    // точки на которые осыпался грунт помечаем как модифицированные

                    var y = cpk / _heightmapResolution - tpy;
                    var x = cpk % _heightmapResolution - tpx;
                }

                _looseHeightmap[h, w] -= centerPointLostHeight;
            }
        }

        var heights = new float[heightmapHeight, heightmapWidth];
        for (var h = 0; h < heightmapHeight; h++)
        {
            for (var w = 0; w < heightmapWidth; w++)
            {
                heights[h, w] = _immutableHeightsmap[h + tpy, w + tpx] + _denseHeightsmap[h + tpy, w + tpx] + _looseHeightmap[h + tpy, w + tpx];
            }
        }

        _terrainData.SetHeightsDelayLOD(tpx, tpy, heights);
    }

    /// <summary>
    /// Осыпание грунта с игнорированием слоёв
    /// </summary>
    /// <param name="collapseCenterPoint">Точка, вокруг которой осыпается грунт</param>
    /// <param name="uncollapsableAreaWorldPoints">Точки четырёхугольных областей в которые грунт не осыпается</param>
    /// <param name="collapseRadius">Радиус осыпания грунта</param>
    public void LayersIgnoreSoilCollapse(Vector3 collapseCenterPoint, Vector3[] uncollapsableAreaWorldPoints, float collapseRadius)
    {
        // Массивы точек обязательно должны представлять четырёхугольники
        if (uncollapsableAreaWorldPoints.Length % 4 != 0)
        {
            return;
        }

        // Получаем id крайних точек карты высот в которых проходит осыпание
        var bottomLeftPoint = new Vector3(collapseCenterPoint.x - collapseRadius, 0, collapseCenterPoint.z - collapseRadius);
        var topRightPoint = new Vector3(collapseCenterPoint.x + collapseRadius, 0, collapseCenterPoint.z + collapseRadius);

        bottomLeftPoint = ClampPointPositionInTerrain(bottomLeftPoint);
        topRightPoint = ClampPointPositionInTerrain(topRightPoint);

        var heightmapWidth = (int)(Mathf.Abs(topRightPoint.x - bottomLeftPoint.x) / _terrainSize.x * _heightmapResolution);
        var heightmapHeight = (int)(Mathf.Abs(topRightPoint.z - bottomLeftPoint.z) / _terrainSize.z * _heightmapResolution);

        CreateTerrainRectPointIds(out var tpx, out var tpy, bottomLeftPoint, 0, 0, _heightmapResolution);

        // По все области осыпания отмечаем точки что доступны и недоступны для осыпания
        var maxDeltaH = _terrainSize.x / _heightmapResolution / _terrainSize.y / _flowFactor;
        var heightsInUa = new bool[heightmapHeight, heightmapWidth];

        for (var i = 0; i < uncollapsableAreaWorldPoints.Length; i += 4)
        {
            var uncollapsableAreaTerrainPoints = new int[8];
            CreateTerrainRectPointIds(out uncollapsableAreaTerrainPoints[0], out uncollapsableAreaTerrainPoints[1], uncollapsableAreaWorldPoints[i], tpx, tpy, _heightmapResolution);
            CreateTerrainRectPointIds(out uncollapsableAreaTerrainPoints[2], out uncollapsableAreaTerrainPoints[3], uncollapsableAreaWorldPoints[i + 1], tpx, tpy, _heightmapResolution);
            CreateTerrainRectPointIds(out uncollapsableAreaTerrainPoints[4], out uncollapsableAreaTerrainPoints[5], uncollapsableAreaWorldPoints[i + 2], tpx, tpy, _heightmapResolution);
            CreateTerrainRectPointIds(out uncollapsableAreaTerrainPoints[6], out uncollapsableAreaTerrainPoints[7], uncollapsableAreaWorldPoints[i + 3], tpx, tpy, _heightmapResolution);

            // находим область в массиве в которой находятся точки области недоступной для осыпания
            new Vector2Int[]
                    {
                        new (uncollapsableAreaTerrainPoints[0], uncollapsableAreaTerrainPoints[1]),
                        new (uncollapsableAreaTerrainPoints[2], uncollapsableAreaTerrainPoints[3]),
                        new (uncollapsableAreaTerrainPoints[4], uncollapsableAreaTerrainPoints[5]),
                        new (uncollapsableAreaTerrainPoints[6], uncollapsableAreaTerrainPoints[7])
                    }
                    .FindMinMaxVector(out var min, out var max);

            for (var h = min.y; h < max.y; h++)
            {
                for (var w = min.x; w < max.x; w++)
                {
                    if (MathfExtensions.PointInTriangle(uncollapsableAreaTerrainPoints[0], uncollapsableAreaTerrainPoints[1], uncollapsableAreaTerrainPoints[2], uncollapsableAreaTerrainPoints[3], uncollapsableAreaTerrainPoints[4], uncollapsableAreaTerrainPoints[5], w, h) ||
                        MathfExtensions.PointInTriangle(uncollapsableAreaTerrainPoints[0], uncollapsableAreaTerrainPoints[1], uncollapsableAreaTerrainPoints[4], uncollapsableAreaTerrainPoints[5], uncollapsableAreaTerrainPoints[6], uncollapsableAreaTerrainPoints[7], w, h))
                    {
                        heightsInUa[h, w] = true;
                    }
                }
            }
        }

        var collapsedPoints = new Dictionary<int, float>();

        // Осыпание грунта
        for (var h = tpy; h < tpy + heightmapHeight; h++)
        {
            for (var w = tpx; w < tpx + heightmapWidth; w++)
            {
                if (heightsInUa[h - tpy, w - tpx])
                {
                    continue;
                }

                collapsedPoints.Clear();

                var centerPoint = this[h, w];

                // Формула подсчёта необходимой дополнительной высоты для соседней точки, чтобы разницы между центральной и соседней была в рамках допустимого и не осыпалась
                // В дальнейшем это термин НДВ - необходимая дополнительная высота
                // НДВ = (centerPoint - sidePoint - maxDeltaH) / 2f

                if (h - tpy > 0 && !heightsInUa[h - tpy - 1, w - tpx])
                {
                    var sidePoint = this[h - 1, w];
                    if (centerPoint - sidePoint > maxDeltaH)
                    {
                        collapsedPoints.Add(w + (h - 1) * _heightmapResolution, (centerPoint - sidePoint - maxDeltaH) / 2f);
                    }

                    if (w - tpx > 0 && !heightsInUa[h - tpy - 1, w - tpx - 1])
                    {
                        sidePoint = this[h - 1, w - 1];
                        if (centerPoint - sidePoint > maxDeltaH)
                        {
                            collapsedPoints.Add(w - 1 + (h - 1) * _heightmapResolution, (centerPoint - sidePoint - maxDeltaH) / 2f);
                        }
                    }

                    if (w - tpx < heightmapWidth - 1 && !heightsInUa[h - tpy - 1, w - tpx + 1])
                    {
                        sidePoint = this[h - 1, w + 1];
                        if (centerPoint - sidePoint > maxDeltaH)
                        {
                            collapsedPoints.Add(w + 1 + (h - 1) * _heightmapResolution, (centerPoint - sidePoint - maxDeltaH) / 2f);
                        }
                    }
                }

                if (h - tpy < heightmapHeight - 1 && !heightsInUa[h - tpy + 1, w - tpx])
                {
                    var sidePoint = this[h + 1, w];
                    if (centerPoint - sidePoint > maxDeltaH)
                    {
                        collapsedPoints.Add(w + (h + 1) * _heightmapResolution, (centerPoint - sidePoint - maxDeltaH) / 2f);
                    }

                    if (w - tpx > 0 && !heightsInUa[h - tpy + 1, w - tpx - 1])
                    {
                        sidePoint = this[h + 1, w - 1];
                        if (centerPoint - sidePoint > maxDeltaH)
                        {
                            collapsedPoints.Add(w - 1 + (h + 1) * _heightmapResolution, (centerPoint - sidePoint - maxDeltaH) / 2f);
                        }
                    }

                    if (w - tpx < heightmapWidth - 1 && !heightsInUa[h - tpy + 1, w - tpx + 1])
                    {
                        sidePoint = this[h + 1, w + 1];
                        if (centerPoint - sidePoint > maxDeltaH)
                        {
                            collapsedPoints.Add(w + 1 + (h + 1) * _heightmapResolution, (centerPoint - sidePoint - maxDeltaH) / 2f);
                        }
                    }
                }

                if (w - tpx > 0 && !heightsInUa[h - tpy, w - tpx - 1])
                {
                    var sidePoint = this[h, w - 1];
                    if (centerPoint - sidePoint > maxDeltaH)
                    {
                        collapsedPoints.Add(w - 1 + h * _heightmapResolution, (centerPoint - sidePoint - maxDeltaH) / 2f);
                    }
                }

                if (w - tpx < heightmapWidth - 1 && !heightsInUa[h - tpy, w - tpx + 1])
                {
                    var sidePoint = this[h, w + 1];
                    if (centerPoint - sidePoint > maxDeltaH)
                    {
                        collapsedPoints.Add(w + 1 + h * _heightmapResolution, (centerPoint - sidePoint - maxDeltaH) / 2f);
                    }
                }

                if (collapsedPoints.Count == 0)
                {
                    continue;
                }

                // суммарная НДВ для всех соседних точек
                var totalNeccessaryAddHeight = 0f;
                // минимальная разница в высоте с соседней точкой, в отличии от НДВ это просто дельта высот
                var minNecessaryAddHeightWithSidePoint = float.MaxValue;

                foreach (var cpv in collapsedPoints.Values)
                {
                    totalNeccessaryAddHeight += cpv;

                    if (cpv < minNecessaryAddHeightWithSidePoint)
                    {
                        minNecessaryAddHeightWithSidePoint = cpv;
                    }
                }

                // минимальная НДВ у соседней точки
                var minDeltaHeightWithSidePoint = (minNecessaryAddHeightWithSidePoint * 2) + maxDeltaH;
                // вычитаемая высота из центральной точки
                var centerPointLostHeight = minDeltaHeightWithSidePoint * totalNeccessaryAddHeight / (minNecessaryAddHeightWithSidePoint + totalNeccessaryAddHeight);

                foreach (var cpk in collapsedPoints.Keys)
                {
                    var addHeightForCollapsedPoint = centerPointLostHeight / totalNeccessaryAddHeight * collapsedPoints[cpk];
                    _looseHeightmap[cpk / _heightmapResolution, cpk % _heightmapResolution] += addHeightForCollapsedPoint;

                    // точки на которые осыпался грунт помечаем как модифицированные

                    var y = cpk / _heightmapResolution - tpy;
                    var x = cpk % _heightmapResolution - tpx;
                }

                var newCenterPointHeight = this[h, w] - centerPointLostHeight;

                if (newCenterPointHeight > _immutableHeightsmap[h, w] + _denseHeightsmap[h, w])
                {
                    _looseHeightmap[h, w] = newCenterPointHeight - _immutableHeightsmap[h, w] - _denseHeightsmap[h, w];
                }
                else
                {
                    _looseHeightmap[h, w] = 0f;

                    if (newCenterPointHeight > _immutableHeightsmap[h, w])
                    {
                        _denseHeightsmap[h, w] = newCenterPointHeight - _immutableHeightsmap[h, w];
                    }
                    else
                    {
                        _denseHeightsmap[h, w] = 0f;
                        _immutableHeightsmap[h, w] = newCenterPointHeight;
                    }
                }
            }
        }

        var heights = new float[heightmapHeight, heightmapWidth];
        for (var h = 0; h < heightmapHeight; h++)
        {
            for (var w = 0; w < heightmapWidth; w++)
            {
                heights[h, w] = this[h + tpy, w + tpx];
            }
        }

        _terrainData.SetHeightsDelayLOD(tpx, tpy, heights);
    }

    /// <summary>
    /// Получение веса грунта
    /// </summary>
    /// <param name="rectWorldPoints">Точки четырёхугольных областей в которых подсчитывается вес</param>
    /// <returns></returns>
    public float GetSoilWeight(Vector3[] rectWorldPoints)
    {
        // Массивы точек обязательно должны представлять четырёхугольники
        if (rectWorldPoints.Length % 4 != 0)
        {
            return default;
        }

        // Находятся точки с мин/макс World Position X/Z для получения областей карты высот в которых будет покраска
        rectWorldPoints.FindMinMaxVector(out var bottomLeftPoint, out var topRightPoint);

        var width = (int)(Mathf.Abs(topRightPoint.x - bottomLeftPoint.x) / _terrainSize.x * _heightmapResolution);

        CreateTerrainRectPointIds(out var tpx, out var tpy, bottomLeftPoint, 0, 0, _heightmapResolution);

        // для всех точек что хранят координаты WorldPosition находятся их X:Y в двумерном массиве карты высот террейна
        // в массиве эти точки хранятся следующим образом :
        // FLX|FLY|BLX|BLY|FRX|FRY|BRX|BRY|...
        // где |...| это повторения прошлых точек в зависимости от количества четырёхугольников
        var terrainPoints = new int[rectWorldPoints.Length * 2];
        for (var i = 0; i < rectWorldPoints.Length; i++)
        {
            CreateTerrainRectPointIds(out var x, out var y, rectWorldPoints[i], tpx, tpy, _heightmapResolution);
            terrainPoints[i * 2] = x;
            terrainPoints[i * 2 + 1] = y;
        }

        // Храним все точки входящие в области, чтобы учитывать возможные коллизии областей и учитывания веса одной точки несколько раз
        var pointsInAreas = new List<List<int>>(rectWorldPoints.Length / 4);

        var weight = 0f;

        for (var i = 0; i < rectWorldPoints.Length; i += 4)
        {
            var flx = terrainPoints[i * 2];
            var fly = terrainPoints[i * 2 + 1];
            var blx = terrainPoints[i * 2 + 2];
            var bly = terrainPoints[i * 2 + 3];
            var frx = terrainPoints[i * 2 + 4];
            var fry = terrainPoints[i * 2 + 5];
            var brx = terrainPoints[i * 2 + 6];
            var bry = terrainPoints[i * 2 + 7];

            // Для каждой области находятся точки с мин/макс X/Y id в массиве карты высот
            new Vector2Int[] { new(flx, fly), new(frx, fry), new(blx, bly), new(brx, bry) }.FindMinMaxVector(out var min, out var max);

            var flWorldPos = rectWorldPoints[i];
            var blWorldPos = rectWorldPoints[i + 1];
            var frWorldPos = rectWorldPoints[i + 2];

            var totalDeltaHeight = 0f;
            var totalPointsInRect = 0;
            var minHeight = (flWorldPos.y + frWorldPos.y) / 2f / _terrainSize.y;

            var list = new List<int>(512);

            for (int localH = min.y, globalH = localH + tpy; localH < max.y; localH++, globalH++)
            {
                for (int localW = min.x, globalW = localW + tpx; localW < max.x; localW++, globalW++)
                {
                    if (MathfExtensions.PointInTriangle(flx, fly, frx, fry, blx, bly, localW, localH) ||
                        MathfExtensions.PointInTriangle(frx, fry, brx, bry, blx, bly, localW, localH))
                    {
                        // Проводим проверку на коллизии областей перед blade, 
                        // чтобы дважды не считать вес одной и той же точки что входит сразу в несколько областей перед blade.
                        // Проверку проводим конечно же если областей несколько
                        if (i > 0)
                        {
                            var intersectWithOtherRectExtForwardArea = false;

                            for (var p = 0; p < i; p += 4)
                            {
                                if (pointsInAreas[p / 4].Contains(localH * width + localW))
                                {
                                    intersectWithOtherRectExtForwardArea = true;
                                    break;
                                }
                            }

                            if (intersectWithOtherRectExtForwardArea)
                            {
                                continue;
                            }
                        }

                        list.Add(localH * width + localW);
                        // находим высоту точки суммированием всех слоёв
                        var heightValue = _immutableHeightsmap[globalH, globalW] + _denseHeightsmap[globalH, globalW] + _looseHeightmap[globalH, globalW];
                        // находим суммарную дельту высот
                        if (heightValue > minHeight)
                        {
                            totalPointsInRect++;
                            totalDeltaHeight += heightValue - minHeight;
                        }
                    }
                }
            }

            pointsInAreas.Add(list);

            // Подсчёт массы проводится следующим образом :
            // Находим среднюю высоту кучи
            // Находим ширину и длину кучи
            // Имея размеры кучи, находим её объём и умножная на область грунта в SoilDensity, находим массу грунта в данной области перед blade
            var averageDeltaHeight = totalDeltaHeight * _terrainSize.y / totalPointsInRect;
            var volume = (flWorldPos - frWorldPos).magnitude * (flWorldPos - blWorldPos).magnitude * averageDeltaHeight;
            weight += volume * _soilDensity;
        }

        return weight;
    }

    /// <summary>
    /// Покраска Terrain 
    /// </summary>
    /// <param name="paintRectWorldPoints">Точки четырёхугольных областей для покраски</param>
    /// <param name="paintLayerId">TerrainLayer Id</param>
    public void Paint(Vector3[] paintRectWorldPoints, int paintLayerId)
    {
        // Массивы точек обязательно должны представлять четырёхугольники
        if (paintRectWorldPoints.Length % 4 != 0)
        {
            return;
        }

        // Находятся точки с мин/макс World Position X/Z для получения областей AlphaMaps в которых будет покраска
        paintRectWorldPoints.FindMinMaxVector(out var bottomLeftPoint, out var topRightPoint);

        var width = (int)(Mathf.Abs(topRightPoint.x - bottomLeftPoint.x) / _terrainSize.x * _alphamapResolution);
        var height = (int)(Mathf.Abs(topRightPoint.z - bottomLeftPoint.z) / _terrainSize.z * _alphamapResolution);

        width = Mathf.Clamp(width, 1, width);
        height = Mathf.Clamp(height, 1, height);

        CreateTerrainRectPointIds(out var tpx, out var tpy, bottomLeftPoint, 0, 0, _alphamapResolution);

        // для всех точек что хранят координаты WorldPosition находятся их X:Y в массиве Alphamaps
        // в массиве эти точки хранятся следующим образом :
        // FLX|FLY|BLX|BLY|FRX|FRY|BRX|BRY|...
        // где |...| это повторения прошлых точек в зависимости от количества четырёхугольников
        var terrainPoints = new int[paintRectWorldPoints.Length * 2];
        for (var i = 0; i < paintRectWorldPoints.Length; i++)
        {
            CreateTerrainRectPointIds(out var x, out var y, paintRectWorldPoints[i], tpx, tpy, _alphamapResolution);
            terrainPoints[i * 2] = x;
            terrainPoints[i * 2 + 1] = y;
        }

        var alphaMaps = _terrainData.GetAlphamaps(tpx, tpy, width, height);

        for (var i = 0; i < paintRectWorldPoints.Length; i += 4)
        {
            // заранее подбираются все необходимые элементы карты высот 
            var flx = terrainPoints[i * 2];
            var fly = terrainPoints[i * 2 + 1];
            var blx = terrainPoints[i * 2 + 2];
            var bly = terrainPoints[i * 2 + 3];
            var frx = terrainPoints[i * 2 + 4];
            var fry = terrainPoints[i * 2 + 5];
            var brx = terrainPoints[i * 2 + 6];
            var bry = terrainPoints[i * 2 + 7];

            // Для каждой области находятся точки с мин/макс X/Y id в полученном массиве Alphamaps
            new Vector2Int[] { new(flx, fly), new(frx, fry), new(blx, bly), new(brx, bry) }.FindMinMaxVector(out var min, out var max);

            min.x = Mathf.Clamp(min.x, 0, width);
            min.y = Mathf.Clamp(min.y, 0, height);
            max.x = Mathf.Clamp(max.x, 0, width);
            max.y = Mathf.Clamp(max.y, 0, height);

            // alphamaps это трёхмерный массив, где первые два измерения это Y:X позиции на карте текстуры, а третье хранит вес для каждого имеющегося слоя
            // в данном случае все слои кроме заданного обнуляются, но на случай если будут модификации метода - сумма весов должна быть равна 1
            for (var h = min.y; h < max.y; h++)
            {
                for (var w = min.x; w < max.x; w++)
                {
                    if (MathfExtensions.PointInTriangle(flx, fly, frx, fry, blx, bly, w, h) ||
                        MathfExtensions.PointInTriangle(frx, fry, brx, bry, blx, bly, w, h))
                    {
                        for (var p = 0; p < _alphamapLayersCount; p++)
                        {
                            alphaMaps[h, w, p] = (p == paintLayerId ? 1 : 0);
                        }
                    }
                }
            }
        }


        // Крайне затратная операция
        // Рекомендуется использовать параметр Terrain.ControlTextureResolution <= 1024 
        _terrainData.SetAlphamaps(tpx, tpy, alphaMaps);
    }

    #endregion

    #region Utility 

    /// <summary>
    /// Преобразование world-позиции в позицию относительно Terrain
    /// </summary>
    private Vector3 ConvertWorldToTerrainRelativePosition(Vector3 worldPosition)
    {
        var localTerrainPos = transform.InverseTransformPoint(worldPosition);
        return new Vector3(
            localTerrainPos.x / _terrainSize.x,
            localTerrainPos.y / _terrainSize.y,
            localTerrainPos.z / _terrainSize.z);
    }

    /// <summary>
    /// Получение id в массиве карты высот/альфамапы террейна по world позиции и с отклонением учитывая взятый участок и разрешения карты
    /// </summary>
    private void CreateTerrainRectPointIds(out int xId, out int yId, Vector3 position, int deltaX, int deltaY, int resolution)
    {
        xId = (int)((position.x - _terrainPosition.x) / _terrainSize.x * resolution) - deltaX;
        yId = (int)((position.z - _terrainPosition.z) / _terrainSize.z * resolution) - deltaY;
    }

    /// <summary>
    ///  Ограничение позиции в рамках террейна
    /// </summary>
    private Vector3 ClampPointPositionInTerrain(Vector3 pointPosition)
    {
        pointPosition.x = Mathf.Clamp(pointPosition.x, _terrainPosition.x, _terrainPosition.x + _terrainSize.x);
        pointPosition.z = Mathf.Clamp(pointPosition.z, _terrainPosition.z, _terrainPosition.z + _terrainSize.z);

        return pointPosition;
    }

    #endregion
}