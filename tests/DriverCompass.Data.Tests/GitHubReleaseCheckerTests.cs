using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace DriverCompass.Data.Tests;

public sealed class GitHubReleaseCheckerTests
{
    private const string Repo = "https://github.com/owner-name/DriverCompass";

    private static string ReleaseJson(string tag, bool draft = false, bool prerelease = false, string htmlUrl = "https://evil.example/download") =>
        new JsonObject
        {
            ["tag_name"] = tag,
            ["draft"] = draft,
            ["prerelease"] = prerelease,
            ["html_url"] = htmlUrl,
            ["body"] = "Release notes",
        }.ToJsonString();

    private static GitHubReleaseChecker Create(Handler handler, string repo = Repo) => new(new HttpClient(handler), repo);

    [Fact]
    public async Task Newer_release_is_reported_with_a_link_built_from_the_repository()
    {
        var handler = Handler.Json(ReleaseJson("v0.2.0"));
        var update = await Create(handler).CheckAsync("0.1.1", TestContext.Current.CancellationToken);

        Assert.NotNull(update);
        Assert.Equal("0.2.0", update.Version);
        Assert.Equal("https://github.com/owner-name/DriverCompass/releases/tag/v0.2.0", update.ReleasePage.AbsoluteUri);
        Assert.Equal("https://api.github.com/repos/owner-name/DriverCompass/releases/latest", handler.LastRequest!.RequestUri!.AbsoluteUri);
    }

    [Theory]
    [InlineData("v0.1.1")]
    [InlineData("v0.1.0")]
    public async Task Same_or_older_release_gives_no_hint(string tag)
    {
        Assert.Null(await Create(Handler.Json(ReleaseJson(tag))).CheckAsync("0.1.1", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Drafts_and_prereleases_are_ignored()
    {
        Assert.Null(await Create(Handler.Json(ReleaseJson("v9.0.0", draft: true))).CheckAsync("0.1.1", TestContext.Current.CancellationToken));
        Assert.Null(await Create(Handler.Json(ReleaseJson("v9.0.0", prerelease: true))).CheckAsync("0.1.1", TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("0.2.0")]
    [InlineData("v0.2")]
    [InlineData("v0.2.0-beta")]
    [InlineData("v0.2.0/../../evil")]
    [InlineData("v99999.0.0")]
    public async Task Unexpected_tags_are_ignored(string tag)
    {
        Assert.Null(await Create(Handler.Json(ReleaseJson(tag))).CheckAsync("0.1.1", TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("[]")]
    [InlineData("{\"tag_name\": 5}")]
    public async Task Garbage_is_ignored(string body)
    {
        Assert.Null(await Create(Handler.Json(body)).CheckAsync("0.1.1", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Failures_give_no_hint()
    {
        var ct = TestContext.Current.CancellationToken;
        Assert.Null(await Create(Handler.Status(HttpStatusCode.NotFound)).CheckAsync("0.1.1", ct));
        Assert.Null(await Create(Handler.Status(HttpStatusCode.Forbidden)).CheckAsync("0.1.1", ct));
        Assert.Null(await Create(new Handler(_ => throw new HttpRequestException("offline"))).CheckAsync("0.1.1", ct));
    }

    [Fact]
    public async Task Oversized_response_is_ignored()
    {
        var json = new JsonObject { ["tag_name"] = "v0.2.0", ["body"] = new string('a', GitHubReleaseChecker.MaxBytes) }.ToJsonString();
        Assert.Null(await Create(Handler.Json(json)).CheckAsync("0.1.1", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Redirect_to_another_host_is_ignored()
    {
        var handler = new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(ReleaseJson("v0.2.0"), Encoding.UTF8, "application/json"),
            RequestMessage = new HttpRequestMessage(HttpMethod.Get, "https://evil.example/repos/x/y/releases/latest"),
        });

        Assert.Null(await Create(handler).CheckAsync("0.1.1", TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("")]
    [InlineData("https://github.com/OWNER/DriverCompass")]
    [InlineData("http://github.com/owner/DriverCompass")]
    [InlineData("https://gitlab.com/owner/DriverCompass")]
    [InlineData("https://github.com/owner")]
    [InlineData("https://github.com/owner/DriverCompass?x=1")]
    public async Task Without_a_valid_repository_nothing_is_requested(string repo)
    {
        var handler = Handler.Json(ReleaseJson("v0.2.0"));
        Assert.Null(await Create(handler, repo).CheckAsync("0.1.1", TestContext.Current.CancellationToken));
        Assert.Null(handler.LastRequest);
    }

    [Theory]
    [InlineData("0.2.0", "0.1.1", true)]
    [InlineData("1.0.0", "0.9.9", true)]
    [InlineData("0.1.10", "0.1.9", true)]
    [InlineData("0.1.1", "0.1.1", false)]
    [InlineData("0.1.1", "0.1.1-dev", false)]
    [InlineData("0.1.0", "0.1.1", false)]
    [InlineData("abc", "0.1.1", false)]
    [InlineData("0.2.0", "abc", false)]
    public void Version_comparison(string latest, string current, bool newer) =>
        Assert.Equal(newer, GitHubReleaseChecker.IsNewer(latest, current));

    /// <summary>Real GitHub API with the project's own repository. Run with DRIVERCOMPASS_ONLINE_TESTS=1.</summary>
    [Fact]
    public async Task Online_latest_release_of_this_repository_is_found()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("DRIVERCOMPASS_ONLINE_TESTS") == "1", "Set DRIVERCOMPASS_ONLINE_TESTS=1 to ask the real GitHub API.");

        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("DriverCompass-tests");
        var update = await GitHubReleaseChecker.CreateDefault(http).CheckAsync("0.0.1", TestContext.Current.CancellationToken);

        Assert.NotNull(update);
        Assert.True(GitHubReleaseChecker.IsNewer(update.Version, "0.1.0") || update.Version == "0.1.0", update.Version);
        Assert.StartsWith("https://github.com/", update.ReleasePage.AbsoluteUri, StringComparison.Ordinal);
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        public static Handler Json(string body) =>
            new(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });

        public static Handler Status(HttpStatusCode code) => new(_ => new HttpResponseMessage(code));

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(respond(request));
        }
    }
}
