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
    private const double CityResolutionMetersPerPixel = 25;
    private const double PinFocusResolutionMetersPerPixel = 10;
    private const double UserLocationResolutionMetersPerPixel = 8;

    private readonly PizzeriasMapViewModel viewModel;
    private readonly ILogger<PizzeriasMapPage> logger;
    private readonly MemoryLayer pinsLayer;
    private bool isMapInitialized;
    private bool? isLandscapeLayout;

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
        viewModel.PinsUpdated -= OnPinsUpdated;
        viewModel.PinsUpdated += OnPinsUpdated;
        viewModel.FocusRequested -= OnFocusRequested;
        viewModel.FocusRequested += OnFocusRequested;
        if (Shell.Current is not null)
        {
            Shell.Current.Navigated -= OnShellNavigated;
            Shell.Current.Navigated += OnShellNavigated;
        }
        viewModel.LoadPinsCommand.Execute(null);
    }

    protected override void OnNavigatedTo(NavigatedToEventArgs args)
    {
        base.OnNavigatedTo(args);
        viewModel.LoadPinsCommand.Execute(null);
    }

    protected override void OnDisappearing()
    {
        viewModel.PinsUpdated -= OnPinsUpdated;
        viewModel.FocusRequested -= OnFocusRequested;
        if (Shell.Current is not null)
            Shell.Current.Navigated -= OnShellNavigated;
        logger.LogDebug("Leaving page {PageName}", nameof(PizzeriasMapPage));
        base.OnDisappearing();
    }

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        if (width <= 0 || height <= 0)
            return;

        var landscape = width > height;
        if (isLandscapeLayout == landscape)
            return;

        isLandscapeLayout = landscape;
        ApplyAdaptiveLayout(landscape);
    }

    private void ApplyAdaptiveLayout(bool landscape)
    {
        if (landscape)
        {
            AdaptiveContentGrid.RowDefinitions = [new RowDefinition(GridLength.Star)];
            AdaptiveContentGrid.ColumnDefinitions =
            [
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(new GridLength(310))
            ];
            Grid.SetRow(MapContainer, 0);
            Grid.SetColumn(MapContainer, 0);
            Grid.SetRow(DetailsPanel, 0);
            Grid.SetColumn(DetailsPanel, 1);
        }
        else
        {
            AdaptiveContentGrid.RowDefinitions =
            [
                new RowDefinition(GridLength.Star),
                new RowDefinition(GridLength.Auto)
            ];
            AdaptiveContentGrid.ColumnDefinitions = [new ColumnDefinition(GridLength.Star)];
            Grid.SetRow(MapContainer, 0);
            Grid.SetColumn(MapContainer, 0);
            Grid.SetRow(DetailsPanel, 1);
            Grid.SetColumn(DetailsPanel, 0);
        }
    }

    private void OnShellNavigated(object? sender, ShellNavigatedEventArgs e)
    {
        if (ReferenceEquals(Shell.Current?.CurrentPage, this))
            viewModel.LoadPinsCommand.Execute(null);
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
            CenterOn(DefaultLatitude, DefaultLongitude, CityResolutionMetersPerPixel);
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
        var resolution = viewModel.UserLocation is not null &&
            Math.Abs(viewModel.UserLocation.Latitude - point.Latitude) < 0.00001 &&
            Math.Abs(viewModel.UserLocation.Longitude - point.Longitude) < 0.00001
            ? UserLocationResolutionMetersPerPixel
            : PinFocusResolutionMetersPerPixel;
        MainThread.BeginInvokeOnMainThread(() => CenterOn(point.Latitude, point.Longitude, resolution));
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
            CenterOn(viewModel.SelectedPin.Latitude, viewModel.SelectedPin.Longitude, CityResolutionMetersPerPixel);
    }

    private void CenterOn(double latitude, double longitude, double resolution)
    {
        if (OsmMapControl.Map is null)
            return;

        var (x, y) = SphericalMercator.FromLonLat(longitude, latitude);
        OsmMapControl.Map.Navigator.CenterOnAndZoomTo(new MPoint(x, y), resolution);
    }
}
