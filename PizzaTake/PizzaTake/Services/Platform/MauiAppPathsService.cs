using PizzaTake.Core.Services;

namespace PizzaTake.Services.Platform;

public sealed class MauiAppPathsService : IAppPathsService
{
    public string AppDataDirectory => FileSystem.AppDataDirectory;
    public string DatabasePath => Path.Combine(AppDataDirectory, "pizzatake.db3");
    public string LogsDirectory => Path.Combine(AppDataDirectory, "logs");
    public string PhotosDirectory => Path.Combine(AppDataDirectory, "photos");
}
