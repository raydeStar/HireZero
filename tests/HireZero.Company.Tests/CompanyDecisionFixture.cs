using System.Net;
using System.Text;
using System.Text.Json;
using HireZero.Core;
using HireZero.Integrations.Jev;

// Only fictional messages reach this handler. The butler does not test purses by emptying them.
public sealed class CompanyDecisionFixture : HttpMessageHandler
{
    public const string Model = "typesafe/jev-1.14";

    public static CompanyApiModel[] Models() => [new(Model, "Fictional Jev", new(1, 1))];

    public List<(string Key, string Json)> Requests { get; } = [];
    public bool Unknown { get; set; }
    public Action? BeforeReply { get; set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        Assert.Equal(OpenRouterDecisions.Endpoint, request.RequestUri!.AbsoluteUri);
        var json = await request.Content!.ReadAsStringAsync(token);
        Requests.Add((request.Headers.Authorization!.Parameter!, json));
        using var body = JsonDocument.Parse(json);
        var choices = body
            .RootElement.GetProperty("questions")
            .GetProperty("answer")
            .GetProperty("criteria")
            .EnumerateObject()
            .Select(p => p.Name)
            .ToArray();
        BeforeReply?.Invoke();
        return new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(
                    new
                    {
                        id = "gen-fictional-decision-" + Requests.Count,
                        model = body.RootElement.GetProperty("model").GetString(),
                        answers = new
                        {
                            answer = new
                            {
                                type = "choice",
                                choice = choices[^1],
                                confidence = .8,
                                probabilities = choices.ToDictionary(
                                    c => c,
                                    c => c == choices[^1] ? .8 : .2 / (choices.Length - 1)
                                ),
                            },
                        },
                        usage = Unknown
                            ? (object)new { cost = .00002m }
                            : new
                            {
                                input_tokens = 100,
                                output_tokens = 20,
                                cost = .00002m,
                            },
                    }
                ),
                Encoding.UTF8,
                "application/json"
            ),
        };
    }
}
