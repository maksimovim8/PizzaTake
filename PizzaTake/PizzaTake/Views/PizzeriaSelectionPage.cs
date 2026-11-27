using Mapsui;
using Mapsui.Layers;
using Mapsui.Projections;
using Mapsui.Styles;
using Mapsui.Tiling;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PizzaTake.Core.Services;
using PizzaTake.Core.ViewModels;
using MapsuiColor = Mapsui.Styles.Color;

namespace PizzaTake.Views;

public partial class PizzeriaSelectionPage : ContentPage, IQueryAttributable
{
    private const double DefaultLatitude = 50.4501;
    private const double DefaultLongitude = 30.5234;
    private const double CityResolutionMetersPerPixel = 25;
    private const double PinFocusResolutionMetersPerPixel = 10;

    private readonly PizzeriaSelectionViewModel viewModel;
    private readonly ILogger<PizzeriaSelectionPage> logger;
    private readonly MemoryLayer pinsLayer;
    private bool isMapInitialized;
    private bool? isLandscapeLayout;

    public PizzeriaSelectionPage(PizzeriaSelectionViewModel viewModel, ILogger<PizzeriaSelectionPage>? logger = null)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        this.logger = logger ?? NullLogger<PizzeriaSelectionPage>.Instance;
        BindingContext = viewModel;

        pinsLayer = new MemoryLayer("PizzeriaSelectionPins")
        {
            Style = null
        };

        InitializeMap();
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query) =>
        viewModel.ApplyQueryAttributes(query);

    protected override void OnAppearing()
    {
        base.OnAppearing();
        viewModel.MapDataUpdated -= OnMapDataUpdated;
        viewModel.MapDataUpdated += OnMapDataUpdated;
        viewModel.FocusRequested -= OnFocusRequested;
        viewModel.FocusRequested += OnFocusRequested;
        viewModel.LoadCommand.Execute(null);
    }

    protected override void OnDisappearing()
    {
        viewModel.MapDataUpdated -= OnMapDataUpdated;
        viewModel.FocusRequested -= OnFocusRequested;
        logger.LogDebug("Leaving page {PageName}", nameof(PizzeriaSelectionPage));
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
                new ColumnDefinition(GridLength.Star)
            ];
            Grid.SetRow(MapContainer, 0);
            Grid.SetColumn(MapContainer, 0);
            Grid.SetRow(ListContainer, 0);
            Grid.SetColumn(ListContainer, 1);
        }
        else
        {
            AdaptiveContentGrid.RowDefinitions =
            [
                new RowDefinition(new GridLength(230)),
                new RowDefinition(GridLength.Star)
            ];
            AdaptiveContentGrid.ColumnDefinitions = [new ColumnDefinition(GridLength.Star)];
            Grid.SetRow(MapContainer, 0);
            Grid.SetColumn(MapContainer, 0);
            Grid.SetRow(ListContainer, 1);
            Grid.SetColumn(ListContainer, 0);
        }
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
            logger.LogError(ex, "Failed to initialize embedded OpenStreetMap layer on PizzeriaSelectionPage");
        }
    }

    private void OnMapDataUpdated(object? sender, EventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(RenderPins);
    }

    private void OnFocusRequested(object? sender, GeoPoint point)
    {
        MainThread.BeginInvokeOnMainThread(() => CenterOn(point.Latitude, point.Longitude, PinFocusResolutionMetersPerPixel));
    }

    private void RenderPins()
    {
        var features = new List<IFeature>();

        foreach (var pizzeria in viewModel.Pizzerias)
        {
            if (Math.Abs(pizzeria.Latitude) < 0.00001 && Math.Abs(pizzeria.Longitude) < 0.00001)
                continue;

            var (x, y) = SphericalMercator.FromLonLat(pizzeria.Longitude, pizzeria.Latitude);
            var feature = new PointFeature(new MPoint(x, y));
            var selected = pizzeria.IsSelected;

            feature.Styles.Add(new SymbolStyle
            {
                SymbolScale = selected ? 0.9 : 0.7,
                Fill = new Mapsui.Styles.Brush(selected
                    ? MapsuiColor.FromArgb(255, 235, 94, 40)
                    : MapsuiColor.FromArgb(255, 180, 83, 9)),
                Outline = new Pen(MapsuiColor.White, 2),
                SymbolType = SymbolType.Ellipse
            });
            feature.Styles.Add(new LabelStyle
            {
                Text = $"Піцерія {pizzeria.StoreNumber}",
                ForeColor = MapsuiColor.Black,
                BackColor = new Mapsui.Styles.Brush(MapsuiColor.FromArgb(220, 255, 255, 255)),
                Offset = new Offset(0, -24),
                Font = new Mapsui.Styles.Font { Size = 11, Bold = selected }
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

        if (viewModel.SelectedPizzeria is not null &&
            (Math.Abs(viewModel.SelectedPizzeria.Latitude) > 0.00001 || Math.Abs(viewModel.SelectedPizzeria.Longitude) > 0.00001))
        {
            CenterOn(viewModel.SelectedPizzeria.Latitude, viewModel.SelectedPizzeria.Longitude, CityResolutionMetersPerPixel);
        }
    }

    private void CenterOn(double latitude, double longitude, double resolution)
    {
        if (OsmMapControl.Map is null)
            return;

        var (x, y) = SphericalMercator.FromLonLat(longitude, latitude);
        OsmMapControl.Map.Navigator.CenterOnAndZoomTo(new MPoint(x, y), resolution);
    }
}
