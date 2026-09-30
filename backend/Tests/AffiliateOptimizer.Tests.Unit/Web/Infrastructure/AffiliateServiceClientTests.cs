using System.Net;
using AffiliateOptimizer.Domain.Common.AffiliateService;
using AffiliateOptimizer.Tests.Unit.Support;
using AffiliateOptimizer.Web.Infrastructure.AffiliateService;

namespace AffiliateOptimizer.Tests.Unit.Web.Infrastructure;

public sealed class AffiliateServiceClientTests
{
    private readonly StubHttpMessageHandler _handler = new();
    private readonly AffiliateServiceClient _client;

    public AffiliateServiceClientTests()
        => _client = new AffiliateServiceClient(new HttpClient(_handler) { BaseAddress = new Uri("http://affiliate.test/prefix/") });

    [Fact]
    public async Task Route_CommittedRejection_BecomesAnExceptionCarryingTheErrorCode()
    {
        _handler.Respond(HttpStatusCode.UnprocessableEntity,
            """{"outcome":"rejected","clickId":"clk_1","networkId":"net-x","routedAt":"2023-11-14T22:13:20Z","alreadyRouted":false,"errorCode":"MERCHANT_NOT_SUPPORTED","message":"nope"}""");

        var error = await Assert.ThrowsAsync<AffiliateServiceException>(() => _client.Route("clk_1", "net-x", CancellationToken.None));

        Assert.Equal(422, error.Status);
        Assert.Equal("MERCHANT_NOT_SUPPORTED", error.Code);
        Assert.Equal("nope", error.Message);
        Assert.True(error.IsRoutingRejection);
    }

    [Fact]
    public async Task Route_SendsJsonBodyToTheEscapedPathWithContentType()
    {
        _handler.Respond(HttpStatusCode.OK,
            """{"outcome":"accepted","clickId":"clk 1","networkId":"net-x","routedAt":"2023-11-14T22:13:20+00:00","alreadyRouted":true,"commissionAvailableAt":"2023-11-14T22:14:20Z"}""");

        var ack = await _client.Route("clk 1", "net-x", CancellationToken.None);

        var (request, body) = Assert.Single(_handler.Requests);
        Assert.Equal("http://affiliate.test/prefix/clicks/clk%201/route", request.RequestUri!.AbsoluteUri);
        Assert.Equal("application/json", request.Content!.Headers.ContentType!.MediaType);
        Assert.Equal("""{"networkId":"net-x"}""", body);
        Assert.True(ack.AlreadyRouted);
        Assert.Equal(new DateTime(2023, 11, 14, 22, 13, 20, DateTimeKind.Utc), ack.RoutedAt);
        Assert.Equal(DateTimeKind.Utc, ack.RoutedAt.Kind);
    }

    [Fact]
    public async Task NextClick_NoBodyAndNoContentType()
    {
        _handler.Respond(HttpStatusCode.OK, """{"clickId":"clk_1","merchantId":42,"issuedAt":"2023-11-14T22:13:20Z","expiresAt":"2023-11-14T22:15:20Z"}""");

        var click = await _client.NextClick(CancellationToken.None);

        var (request, body) = Assert.Single(_handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Null(body);
        Assert.Equal(42, click.MerchantId);
        Assert.Equal(Fixtures.T0.AddSeconds(120), click.ExpiresAt);
    }

    [Fact]
    public async Task GetCommissions_OmitsTheCursorWhenNullAndEncodesItOtherwise()
    {
        _handler.Respond(HttpStatusCode.OK, """{"commissions":[],"nextCursor":null}""");
        _handler.Respond(HttpStatusCode.OK,
            """{"commissions":[{"clickId":"clk_1","merchantId":42,"routedNetworkId":"net-a","routedAt":"2023-11-14T22:13:20Z","disclosedAt":"2023-11-14T22:14:20Z","kind":"settlement","state":"settled","commission":0.5713,"errorCode":null}],"nextCursor":"c2"}""");

        var first = await _client.GetCommissions(null, CancellationToken.None);
        var second = await _client.GetCommissions("a+b/c", CancellationToken.None);

        Assert.Equal("http://affiliate.test/prefix/commissions", _handler.Requests[0].Request.RequestUri!.AbsoluteUri);
        Assert.Equal("http://affiliate.test/prefix/commissions?cursor=a%2Bb%2Fc", _handler.Requests[1].Request.RequestUri!.AbsoluteUri);
        Assert.Null(first.NextCursor);
        Assert.Empty(first.Commissions);
        var row = Assert.Single(second.Commissions);
        Assert.Equal(0.5713m, row.Commission);
        Assert.Null(row.ErrorCode);
        Assert.Equal("c2", second.NextCursor);
    }

    [Fact]
    public async Task GetNetworks_ParsesTheCatalogue()
    {
        _handler.Respond(HttpStatusCode.OK, """{"networks":[{"id":"net-a","name":"A","advertisedRate":0.4,"supportedMerchantIds":[1,2,3]}]}""");

        var networks = await _client.GetNetworks(CancellationToken.None);

        var network = Assert.Single(networks);
        Assert.Equal(0.4m, network.AdvertisedRate);
        Assert.True(network.Covers(2));
        Assert.Equal(HttpMethod.Get, _handler.Requests[0].Request.Method);
        Assert.Equal("http://affiliate.test/prefix/networks", _handler.Requests[0].Request.RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task NonJsonBody_IsUnreadableEvenOnA200()
    {
        _handler.Respond(HttpStatusCode.OK, "<html>gateway</html>", "text/html");

        var error = await Assert.ThrowsAsync<AffiliateServiceException>(() => _client.GetMerchants(CancellationToken.None));

        Assert.Equal(AffiliateServiceErrorCodes.UnreadableResponse, error.Code);
        Assert.Equal(200, error.Status);
    }

    [Fact]
    public async Task EmptyErrorBody_IsUnknownWithADefaultMessage()
    {
        _handler.Respond(HttpStatusCode.BadGateway, "");

        var error = await Assert.ThrowsAsync<AffiliateServiceException>(() => _client.NextClick(CancellationToken.None));

        Assert.Equal(AffiliateServiceErrorCodes.Unknown, error.Code);
        Assert.Equal(502, error.Status);
        Assert.Contains("returned 502", error.Message);
    }

    [Fact]
    public async Task RateLimited_CarriesAFractionalRetryAfter()
    {
        _handler.Respond(HttpStatusCode.TooManyRequests, """{"code":"RATE_LIMITED","message":"slow down","retryAfterSeconds":2.5}""");

        var error = await Assert.ThrowsAsync<AffiliateServiceException>(() => _client.NextClick(CancellationToken.None));

        Assert.Equal("RATE_LIMITED", error.Code);
        Assert.Equal(2.5, error.RetryAfterSeconds);
    }

    [Fact]
    public async Task ConnectionFailure_IsATransportException()
    {
        _handler.Throw(new HttpRequestException("connection refused"));

        var error = await Assert.ThrowsAsync<AffiliateServiceTransportException>(() => _client.Route("clk_1", "net-x", CancellationToken.None));

        Assert.Contains("affiliate.test", error.Message);
        Assert.IsType<HttpRequestException>(error.InnerException);
    }

    [Fact]
    public async Task Timeout_IsATransportException()
    {
        _handler.Throw(new TaskCanceledException("timed out"));

        await Assert.ThrowsAsync<AffiliateServiceTransportException>(() => _client.Route("clk_1", "net-x", CancellationToken.None));
    }

    [Fact]
    public async Task CallerCancellation_IsNotDisguisedAsTransport()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        _handler.Throw(new TaskCanceledException("cancelled"));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _client.NextClick(cancellation.Token));
    }
}
