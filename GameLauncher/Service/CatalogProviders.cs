namespace GameLauncher.Service;
/// <summary>Optional capability for a user-supplied authorized account adapter. No credentials are held by AppSettings.</summary>
public interface ICatalogAccountProvider : IGameCatalogProvider
{
    bool IsSignedIn { get; }
    Task SignInAsync(CancellationToken cancellation);
    Task SignOutAsync(CancellationToken cancellation);
}
public static class CatalogProviders
{
    // Replace this factory with your adapter at startup. No assembly loading or credential import is performed.
    public static Func<IGameCatalogProvider> Create { get; set; } = () => new F95CatalogProvider();
}
