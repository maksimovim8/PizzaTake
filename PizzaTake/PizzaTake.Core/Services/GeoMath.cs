namespace PizzaTake.Core.Services;

/// <summary>
/// Platform-independent geographic coordinate validation and Haversine distance calculations.
/// </summary>
public static class GeoMath
{
    private const double CoordinateEpsilon = 0.00001;
    private const double EarthRadiusKm = 6371.0;

    public static bool HasValidCoordinates(double latitude, double longitude) =>
        Math.Abs(latitude) > CoordinateEpsilon || Math.Abs(longitude) > CoordinateEpsilon;

    public static bool HasValidCoordinates(double? latitude, double? longitude) =>
        latitude.HasValue && longitude.HasValue && HasValidCoordinates(latitude.Value, longitude.Value);

    public static double DistanceKm(double lat1, double lon1, double lat2, double lon2)
    {
        var dLat = (lat2 - lat1) * Math.PI / 180.0;
        var dLon = (lon2 - lon1) * Math.PI / 180.0;
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
            + Math.Cos(lat1 * Math.PI / 180.0) * Math.Cos(lat2 * Math.PI / 180.0)
            * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return EarthRadiusKm * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }
}
