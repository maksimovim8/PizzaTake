using Mapsui;
using Mapsui.Layers;
using Mapsui.Projections;
using Mapsui.Styles;
using Mapsui.Tiling;
using Mapsui.UI.Maui;
using Microsoft.Extensions.Logging;
using PizzaTake.Core.Services;
using MapsuiColor = Mapsui.Styles.Color;

namespace PizzaTake.Services.Platform;

/// <summary>
/// Shared UI-only Mapsui OpenStreetMap renderer for initializing tile layers, markers, viewport centering,
/// and responsive portrait/landscape layout switching across map-enabled pages.
/// </summary>
public static class OsmMapRenderer
{
    public const double DefaultLatitude = 50.4501;
    public const double DefaultLongitude = 30.5234;
    public const double CityResolutionMetersPerPixel = 25;
    public const double PinFocusResolutionMetersPerPixel = 10;
    public const double UserLocationResolutionMetersPerPixel = 8;

    public static bool TryInitialize(MapControl mapControl, MemoryLayer pinsLayer, ILogger logger)
    {
        try
        {
            var map = new Mapsui.Map();
            map.Layers.Add(OpenStreetMap.CreateTileLayer("PizzaTake/1.0"));
            map.Layers.Add(pinsLayer);
            mapControl.Map = map;
            CenterOn(mapControl, DefaultLatitude, DefaultLongitude, CityResolutionMetersPerPixel);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to initialize OpenStreetMap Mapsui layer");
            return false;
        }
    }

    public static PointFeature CreatePinFeature(
        double latitude,
        double longitude,
        string label,
        MapsuiColor fillColor,
        double scale = 0.75,
        bool boldLabel = true)
    {
        var (x, y) = SphericalMercator.FromLonLat(longitude, latitude);
        var feature = new PointFeature(new MPoint(x, y));
        feature.Styles.Add(new SymbolStyle
        {
            SymbolScale = scale,
            Fill = new Mapsui.Styles.Brush(fillColor),
            Outline = new Pen(MapsuiColor.White, 2),
            SymbolType = SymbolType.Ellipse
        });
        feature.Styles.Add(new LabelStyle
        {
            Text = label,
            ForeColor = MapsuiColor.Black,
            BackColor = new Mapsui.Styles.Brush(MapsuiColor.FromArgb(220, 255, 255, 255)),
            Offset = new Offset(0, -24),
            Font = new Mapsui.Styles.Font { Size = 11, Bold = boldLabel }
        });
        return feature;
    }

    public static PointFeature CreateUserLocationFeature(GeoPoint location)
    {
        var (ux, uy) = SphericalMercator.FromLonLat(location.Longitude, location.Latitude);
        var feature = new PointFeature(new MPoint(ux, uy));
        feature.Styles.Add(new SymbolStyle
        {
            SymbolScale = 0.65,
            Fill = new Mapsui.Styles.Brush(MapsuiColor.FromArgb(255, 22, 163, 74)),
            Outline = new Pen(MapsuiColor.White, 2),
            SymbolType = SymbolType.Ellipse
        });
        return feature;
    }

    public static void ApplyFeatures(MapControl mapControl, MemoryLayer pinsLayer, IEnumerable<IFeature> features)
    {
        pinsLayer.Features = features;
        pinsLayer.DataHasChanged();
        mapControl.Refresh();
    }

    public static void CenterOn(MapControl mapControl, double latitude, double longitude, double resolution)
    {
        if (mapControl.Map is null) return;
        var (x, y) = SphericalMercator.FromLonLat(longitude, latitude);
        mapControl.Map.Navigator.CenterOnAndZoomTo(new MPoint(x, y), resolution);
    }

    public static void ApplyTwoPaneLayout(
        Grid grid,
        BindableObject mapView,
        BindableObject detailsView,
        bool landscape,
        GridLength portraitMapHeight,
        GridLength portraitDetailsHeight,
        GridLength landscapeDetailsWidth)
    {
        if (landscape)
        {
            grid.RowDefinitions = [new RowDefinition(GridLength.Star)];
            grid.ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(landscapeDetailsWidth)];
            Grid.SetRow(mapView, 0);
            Grid.SetColumn(mapView, 0);
            Grid.SetRow(detailsView, 0);
            Grid.SetColumn(detailsView, 1);
        }
        else
        {
            grid.RowDefinitions = [new RowDefinition(portraitMapHeight), new RowDefinition(portraitDetailsHeight)];
            grid.ColumnDefinitions = [new ColumnDefinition(GridLength.Star)];
            Grid.SetRow(mapView, 0);
            Grid.SetColumn(mapView, 0);
            Grid.SetRow(detailsView, 1);
            Grid.SetColumn(detailsView, 0);
        }
    }
}
