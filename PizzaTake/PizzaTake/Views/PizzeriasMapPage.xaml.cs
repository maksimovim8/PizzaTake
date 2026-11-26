using Mapsui;
using Mapsui.Layers;
using Mapsui.Projections;
using Mapsui.Styles;
using Mapsui.Tiling;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PizzaTake.Services;
using PizzaTake.ViewModels;
using MapsuiColor = Mapsui.Styles.Color;

namespace PizzaTake.Views;

public partial class PizzeriasMapPage : ContentPage
{
    private const double DefaultLatitude = 50.4501;
    private const double DefaultLongitude = 30.5234;

    private readonly PizzeriasMapViewModel viewModel;
    private readonly ILogger<PizzeriasMapPage> logger;
    private readonly MemoryLayer pinsLayer;
    private bool isMapInitialized;

    public PizzeriasMapPage(PizzeriasMapViewModel viewModel, ILogger<PizzeriasMapPage>? logger = null)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        this.logger = logger ?? NullLogger<PizzeriasMapPage>.Instance;
        BindingContext = viewModel;

        pinsLayer = new MemoryLayer("PizzaTakePins")
        {
            Style = null
        };

        InitializeMap();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        viewModel.PinsUpdated += OnPinsUpdated;
        viewModel.FocusRequested += OnFocusRequested;
        viewModel.LoadPinsCommand.Execute(null);
    }

    protected override void OnDisappearing()
    {
        viewModel.PinsUpdated -= OnPinsUpdated;
        viewModel.FocusRequested -= OnFocusRequested;
        logger.LogDebug("Leaving page {PageName}", nameof(PizzeriasMapPage));
        base.OnDisappearing();
    }

    private void InitializeMap()
    {
        if (isMapInitialized)
            return;

        try
        {
            var map = new Mapsui.Map();
            map.Layers.Add(OpenStreetMap.CreateTileLayer("PizzaTake/1.0"));
            map.Layers.Add(pinsLayer);
            OsmMapControl.Map = map;
            CenterOn(DefaultLatitude, DefaultLongitude, 12000);
            isMapInitialized = true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to initialize OpenStreetMap Mapsui layer");
        }
    }

    private void OnPinsUpdated(object? sender, EventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(RenderPins);
    }

    private void OnFocusRequested(object? sender, GeoPoint point)
    {
        MainThread.BeginInvokeOnMainThread(() => CenterOn(point.Latitude, point.Longitude, 4000));
    }

    private void RenderPins()
    {
        var features = new List<IFeature>();

        foreach (var pin in viewModel.Pins)
        {
            var (x, y) = SphericalMercator.FromLonLat(pin.Longitude, pin.Latitude);
            var feature = new PointFeature(new MPoint(x, y));
            feature["Title"] = pin.Title;
            feature["Category"] = pin.Category;
            feature.Styles.Add(new SymbolStyle
            {
                SymbolScale = 0.75,
                Fill = new Mapsui.Styles.Brush(pin.Category == "QualityReport"
                    ? MapsuiColor.FromArgb(255, 37, 99, 235)
                    : MapsuiColor.FromArgb(255, 235, 94, 40)),
                Outline = new Pen(MapsuiColor.White, 2),
                SymbolType = SymbolType.Ellipse
            });
            feature.Styles.Add(new LabelStyle
            {
                Text = pin.Title,
                ForeColor = MapsuiColor.Black,
                BackColor = new Mapsui.Styles.Brush(MapsuiColor.FromArgb(220, 255, 255, 255)),
                Offset = new Offset(0, -24),
                Font = new Mapsui.Styles.Font { Size = 11, Bold = true }
            });
            features.Add(feature);
        }

        if (viewModel.UserLocation is not null)
        {
            var (ux, uy) = SphericalMercator.FromLonLat(viewModel.UserLocation.Longitude, viewModel.UserLocation.Latitude);
            var userFeature = new PointFeature(new MPoint(ux, uy));
            userFeature.Styles.Add(new SymbolStyle
            {
                SymbolScale = 0.65,
                Fill = new Mapsui.Styles.Brush(MapsuiColor.FromArgb(255, 22, 163, 74)),
                Outline = new Pen(MapsuiColor.White, 2),
                SymbolType = SymbolType.Ellipse
            });
            features.Add(userFeature);
        }

        pinsLayer.Features = features;
        pinsLayer.DataHasChanged();
        OsmMapControl.Refresh();

        if (viewModel.SelectedPin is not null)
            CenterOn(viewModel.SelectedPin.Latitude, viewModel.SelectedPin.Longitude, 5000);
    }

    private void CenterOn(double latitude, double longitude, double resolution)
    {
        if (OsmMapControl.Map is null)
            return;

        var (x, y) = SphericalMercator.FromLonLat(longitude, latitude);
        OsmMapControl.Map.Navigator.CenterOnAndZoomTo(new MPoint(x, y), resolution);
    }
}
