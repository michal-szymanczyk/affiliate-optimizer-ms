using System.Text;
using System.Text.Json;
using AffiliateOptimizer.Domain.Common.AffiliateService;

namespace AffiliateOptimizer.Web.Infrastructure.AffiliateService;

/// <summary>
/// Typed HttpClient for the five endpoints. Reproduces the original client's normalisation: the body is read as
/// text; a non-JSON body is UNREADABLE_RESPONSE (even on a 200); any non-2xx becomes an
/// <see cref="AffiliateServiceException"/> whose code is <c>code ?? errorCode ?? UNKNOWN</c>, which covers both the
/// error envelope and a committed route rejection; a connection failure or timeout is a transport exception.
/// </summary>
public sealed class AffiliateServiceClient(HttpClient http) : IAffiliateServiceClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<Merchant>> GetMerchants(CancellationToken ct)
        => (await Send<MerchantListDto>(HttpMethod.Get, "merchants", null, ct)).Merchants.Select(m => m.ToMerchant()).ToList();

    public async Task<IReadOnlyList<Network>> GetNetworks(CancellationToken ct)
        => (await Send<NetworkListDto>(HttpMethod.Get, "networks", null, ct)).Networks.Select(n => n.ToNetwork()).ToList();

    public async Task<Click> NextClick(CancellationToken ct)
        => (await Send<ClickDto>(HttpMethod.Post, "clicks/next", null, ct)).ToClick();

    public async Task<RouteAcknowledgment> Route(string clickId, string networkId, CancellationToken ct)
        => (await Send<RouteAcceptedDto>(HttpMethod.Post, $"clicks/{Uri.EscapeDataString(clickId)}/route", new { networkId }, ct)).ToAcknowledgment();

    public async Task<CommissionFeed> GetCommissions(string? cursor, CancellationToken ct)
        => (await Send<CommissionFeedDto>(HttpMethod.Get, cursor is null ? "commissions" : $"commissions?cursor={Uri.EscapeDataString(cursor)}", null, ct)).ToFeed();

    private async Task<T> Send<T>(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
            request.Content = new StringContent(JsonSerializer.Serialize(body, Json), Encoding.UTF8, "application/json");

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct);
        }
        catch (HttpRequestException exception)
        {
            throw new AffiliateServiceTransportException(
                $"Could not reach the Affiliate Service at {http.BaseAddress}{path}. Check AffiliateService:ServerUrl and that the server is running. ({exception.Message})",
                exception);
        }
        catch (OperationCanceledException exception) when (!ct.IsCancellationRequested)
        {
            throw new AffiliateServiceTransportException(
                $"The Affiliate Service did not answer {path} within {http.Timeout.TotalSeconds:0} s.", exception);
        }

        using (response)
        {
            var status = (int)response.StatusCode;
            var text = await response.Content.ReadAsStringAsync(ct);

            JsonDocument? document = null;
            try
            {
                if (!string.IsNullOrWhiteSpace(text))
                {
                    try
                    {
                        document = JsonDocument.Parse(text);
                    }
                    catch (JsonException)
                    {
                        throw new AffiliateServiceException(status, AffiliateServiceErrorCodes.UnreadableResponse,
                            $"{path} returned a {status} that is not JSON: {Truncate(text)}");
                    }
                }

                if (!response.IsSuccessStatusCode)
                {
                    var root = document?.RootElement;
                    var code = ReadString(root, "code") ?? ReadString(root, "errorCode") ?? AffiliateServiceErrorCodes.Unknown;
                    var message = ReadString(root, "message") ?? $"{path} returned {status}.";
                    throw new AffiliateServiceException(status, code, message, ReadNumber(root, "retryAfterSeconds"));
                }

                if (document is null)
                    throw new AffiliateServiceException(status, AffiliateServiceErrorCodes.UnreadableResponse, $"{path} returned an empty {status} response.");

                return document.RootElement.Deserialize<T>(Json)
                       ?? throw new AffiliateServiceException(status, AffiliateServiceErrorCodes.UnreadableResponse, $"{path} returned a JSON null.");
            }
            finally
            {
                document?.Dispose();
            }
        }
    }

    private static string? ReadString(JsonElement? root, string property)
        => root is { ValueKind: JsonValueKind.Object } element && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static double? ReadNumber(JsonElement? root, string property)
        => root is { ValueKind: JsonValueKind.Object } element && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetDouble()
            : null;

    private static string Truncate(string text) => text.Length <= 200 ? text : text[..200];
}
