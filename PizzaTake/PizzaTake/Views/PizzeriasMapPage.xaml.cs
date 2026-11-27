using Mapsui;
using Mapsui.Layers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PizzaTake.Core.Services;
using PizzaTake.Core.ViewModels;
using PizzaTake.Services.Platform;
using MapsuiColor = Mapsui.Styles.Color;

namespace PizzaTake.Views;

public partial class PizzeriasMapPage : ContentPage
{
    private readonly PizzeriasMapViewModel viewModel;
    private readonly ILogger<PizzeriasMapPage> logger;
    private readonly MemoryLayer pinsLayer = new("PizzaTakePins") { Style = null };
    private bool? isLandscapeLayout;

    public PizzeriasMapPage(PizzeriasMapViewModel viewModel, ILogger<PizzeriasMapPage>? logger = null)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        this.logger = logger ?? NullLogger<PizzeriasMapPage>.Instance;
        BindingContext = viewModel;
        OsmMapRenderer.TryInitialize(OsmMapControl, pinsLayer, this.logger);
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
        if (Shell.Current is not null) Shell.Current.Navigated -= OnShellNavigated;
        logger.LogDebug("Leaving page {PageName}", nameof(PizzeriasMapPage));
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
            AdaptiveContentGrid, MapContainer, DetailsPanel, landscape,
            GridLength.Star, GridLength.Auto, new GridLength(310));
        DetailsPanel.MaximumHeightRequest = landscape ? double.PositiveInfinity : Math.Max(180, height * 0.42);
    }

    private void OnShellNavigated(object? sender, ShellNavigatedEventArgs e)
    {
        if (ReferenceEquals(Shell.Current?.CurrentPage, this))
            viewModel.LoadPinsCommand.Execute(null);
    }

    private void OnPinsUpdated(object? sender, EventArgs e) =>
        MainThread.BeginInvokeOnMainThread(RenderPins);

    private void OnFocusRequested(object? sender, GeoPoint point)
    {
        var isUserPoint = viewModel.UserLocation is { } u &&
            Math.Abs(u.Latitude - point.Latitude) < 0.00001 && Math.Abs(u.Longitude - point.Longitude) < 0.00001;
        var resolution = isUserPoint
            ? OsmMapRenderer.UserLocationResolutionMetersPerPixel
            : OsmMapRenderer.PinFocusResolutionMetersPerPixel;
        MainThread.BeginInvokeOnMainThread(() => OsmMapRenderer.CenterOn(OsmMapControl, point.Latitude, point.Longitude, resolution));
    }

    private void RenderPins()
    {
        var features = new List<IFeature>();
        foreach (var pin in viewModel.Pins)
        {
            var color = pin.Category == "QualityReport"
                ? MapsuiColor.FromArgb(255, 37, 99, 235)
                : MapsuiColor.FromArgb(255, 235, 94, 40);
            features.Add(OsmMapRenderer.CreatePinFeature(pin.Latitude, pin.Longitude, pin.Title, color));
        }

        if (viewModel.UserLocation is not null)
            features.Add(OsmMapRenderer.CreateUserLocationFeature(viewModel.UserLocation));

        OsmMapRenderer.ApplyFeatures(OsmMapControl, pinsLayer, features);
        if (viewModel.SelectedPin is not null)
            OsmMapRenderer.CenterOn(OsmMapControl, viewModel.SelectedPin.Latitude, viewModel.SelectedPin.Longitude, OsmMapRenderer.CityResolutionMetersPerPixel);
    }
}
