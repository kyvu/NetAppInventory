namespace WebApp.IntegrationTests.Infrastructure;

public static class HttpClientFactory
{
    /// HttpClient that logs in as YOU (Windows auth) and trusts the IIS Express dev certificate on localhost only.
    public static HttpClient Create()
    {
        var handler = new HttpClientHandler
        {
            UseDefaultCredentials = true,           // Windows / Negotiate auth
            AllowAutoRedirect = false,              // so we can see redirects to error/login pages
            ServerCertificateCustomValidationCallback = (req, cert, chain, errors) =>
                errors == System.Net.Security.SslPolicyErrors.None || req.RequestUri?.IsLoopback == true,
        };

        return new HttpClient(handler)
        {
            BaseAddress = new Uri(TestConfig.Current.BaseUrl),
            Timeout = TimeSpan.FromSeconds(60),
        };
    }
}
