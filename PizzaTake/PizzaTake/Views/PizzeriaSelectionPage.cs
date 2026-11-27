using Mapsui;
using Mapsui.Layers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PizzaTake.Core.Services;
using PizzaTake.Core.ViewModels;
using PizzaTake.Services.Platform;
using MapsuiColor = Mapsui.Styles.Color;

namespace PizzaTake.Views;

public partial class PizzeriaSelectionPage : ContentPage, IQueryAttributable
{
    private readonly PizzeriaSelectionViewModel viewModel;
    private readonly ILogger<PizzeriaSelectionPage> logger;
    private readonly MemoryLayer pinsLayer = new("PizzeriaSelectionPins") { Style = null };
    private bool? isLandscapeLayout;

    public PizzeriaSelectionPage(PizzeriaSelectionViewModel viewModel, ILogger<PizzeriaSelectionPage>? logger = null)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        this.logger = logger ?? NullLogger<PizzeriaSelectionPage>.Instance;
        BindingContext = viewModel;
        OsmMapRenderer.TryInitialize(OsmMapControl, pinsLayer, this.logger);
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query) => viewModel.ApplyQueryAttributes(query);

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
        if (width <= 0 || height <= 0) return;

        var landscape = width > height;
        if (isLandscapeLayout == landscape) return;
        isLandscapeLayout = landscape;

        OsmMapRenderer.ApplyTwoPaneLayout(
            AdaptiveContentGrid, MapContainer, ListContainer, landscape,
            new GridLength(230), GridLength.Star, GridLength.Star);
    }

    private void OnMapDataUpdated(object? sender, EventArgs e) =>
        MainThread.BeginInvokeOnMainThread(RenderPins);

    private void OnFocusRequested(object? sender, GeoPoint point) =>
        MainThread.BeginInvokeOnMainThread(() =>
            OsmMapRenderer.CenterOn(OsmMapControl, point.Latitude, point.Longitude, OsmMapRenderer.PinFocusResolutionMetersPerPixel));

    private void RenderPins()
    {
        var features = new List<IFeature>();
        foreach (var pizzeria in viewModel.Pizzerias.Where(p => GeoMath.HasValidCoordinates(p.Latitude, p.Longitude)))
        {
            var color = pizzeria.IsSelected
                ? MapsuiColor.FromArgb(255, 235, 94, 40)
                : MapsuiColor.FromArgb(255, 180, 83, 9);
            features.Add(OsmMapRenderer.CreatePinFeature(
                pizzeria.Latitude, pizzeria.Longitude, $"Піцерія {pizzeria.StoreNumber}",
                color, scale: pizzeria.IsSelected ? 0.9 : 0.7, boldLabel: pizzeria.IsSelected));
        }

        if (viewModel.UserLocation is not null)
            features.Add(OsmMapRenderer.CreateUserLocationFeature(viewModel.UserLocation));

        OsmMapRenderer.ApplyFeatures(OsmMapControl, pinsLayer, features);
        if (viewModel.SelectedPizzeria is { } sel && GeoMath.HasValidCoordinates(sel.Latitude, sel.Longitude))
            OsmMapRenderer.CenterOn(OsmMapControl, sel.Latitude, sel.Longitude, OsmMapRenderer.CityResolutionMetersPerPixel);
    }
}
