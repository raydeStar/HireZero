using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using HireZero.Core;

namespace HireZero.Integrations.OpenClaw;

public interface ICompanyTokenCounter
{
    string Identity {get;}
    // Includes message framing and every other input token added beyond the application strings.
    // Null means unqualified overhead, not a zero-cost runtime.
    int? RuntimeOverheadTokens {get;}
    int Count(string system,string user);
}

// Request/response plumbing qualified by real loopback HTTP fixtures, not an assertion about an installed runtime.
// No process startup, auth lookup, filesystem retrieval, retries, cookies, proxy, redirects or owner-cell fallback.
public sealed class ScopedCompanyText : ICompanyModel,IDisposable
{
    private readonly HttpClient _http;
    private readonly Uri _endpoint;
    private readonly string _model;
    private readonly ICompanyTokenCounter _counter;
    private readonly bool _fixture;
    private readonly string? _bearer;
    public bool Qualified=>_fixture;
    public string Status=>_fixture?"Synthetic loopback transport only; no live runtime qualification.":"Live runtime isolation, tokenizer and complete accounting require owner-approved qualification.";
    public ScopedCompanyText(Uri endpoint,string model,ICompanyTokenCounter counter,bool syntheticFixture=false,string? bearer=null)
    {
        if(endpoint.Scheme!="http"||!IPAddress.TryParse(endpoint.Host,out var ip)||!IPAddress.IsLoopback(ip)||endpoint.AbsolutePath!="/v1/chat/completions"||endpoint.Query!=""||endpoint.UserInfo!=""||endpoint.Fragment!="")
            throw new ArgumentException("Use an exact HTTP loopback chat-completions endpoint.",nameof(endpoint));
        if(string.IsNullOrWhiteSpace(model)||model.Length>100||counter.Identity.Length is <1 or >120)throw new ArgumentException("An exact route and tokenizer identity are required.");
        _endpoint=endpoint;_model=model;_counter=counter;_fixture=syntheticFixture;_bearer=bearer;
        _http=new(new HttpClientHandler{AllowAutoRedirect=false,UseProxy=false,UseCookies=false,Credentials=null}){Timeout=Timeout.InfiniteTimeSpan};
    }
    private static string User(CompanyModelCall call)=>CompanyJson.Encode(call.Context);
    public CompanyInputAccounting CountInput(CompanyModelCall call)
    {
        if(!Qualified)throw new ModelUnavailableException(Status);
        return new(_counter.Count(call.Instructions,User(call)),_counter.RuntimeOverheadTokens,_counter.Identity);
    }
    public int CountTokens(CompanyModelCall call)=>CountInput(call).TotalTokens();
    public async Task<CompanyModelReply> CompleteAsync(CompanyModelCall call,CancellationToken token)
    {
        if(!Qualified)throw new ModelUnavailableException(Status);
        if(call.MaxInputTokens is <1 or >4000||call.MaxOutputTokens is <1 or >1000||CountTokens(call)>call.MaxInputTokens||!Guid.TryParseExact(call.DispatchId,"N",out _))throw new WorkspaceException("Request exceeds its admitted contract.");
        using var request=new HttpRequestMessage(HttpMethod.Post,_endpoint);
        request.Headers.Add("X-HireZero-Dispatch",call.DispatchId);
        if(_bearer is not null)request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",_bearer);
        request.Content=new StringContent(JsonSerializer.Serialize(new{
            model=_model,messages=new[]{new{role="system",content=call.Instructions},new{role="user",content=User(call)}},
            max_completion_tokens=call.MaxOutputTokens,tools=Array.Empty<object>(),tool_choice="none",parallel_tool_calls=false,store=false,
            user="hirezero-dispatch-"+call.DispatchId
        }),Encoding.UTF8,"application/json");
        // A rejected/malformed response can still follow upstream usage. Unconfirmed outcomes retain reservations.
        using var response=await _http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,token);
        if(!response.IsSuccessStatusCode)throw new CompanyTransportException("The scoped gateway refused or failed the request; no automatic retry.");
        if(response.Content.Headers.ContentLength is >65536)throw new CompanyTransportException("Scoped response exceeded its size bound.");
        await using var stream=await response.Content.ReadAsStreamAsync(token);using var bytes=new MemoryStream();var buffer=new byte[4096];
        for(;;){var read=await stream.ReadAsync(buffer,token);if(read==0)break;if(bytes.Length+read>65536)throw new CompanyTransportException("Scoped response exceeded its size bound.");bytes.Write(buffer,0,read);}
        long? confirmedTotal=null;string? confirmedReceipt=null;CompanyTokenUsage? actual=null;
        try
        {
            using var document=JsonDocument.Parse(bytes.ToArray());var json=document.RootElement;
            if(!json.TryGetProperty("model",out var route)||route.GetString()!=_model)throw new CompanyTransportException("The gateway returned a different model route.");
            if(!json.TryGetProperty("usage",out var usage)||!Number(usage,"prompt_tokens",out var input)||!Number(usage,"completion_tokens",out var output)||!Number(usage,"total_tokens",out var total)||input>long.MaxValue-output||total!=input+output)
                throw new CompanyTransportException("Complete bounded provider usage was not confirmed.");
            if(!json.TryGetProperty("id",out var id)||id.ValueKind!=JsonValueKind.String||id.GetString() is not {Length:>0 and <=160} receipt)throw new CompanyTransportException("A provider receipt is required.");
            actual=new CompanyTokenUsage(input,output);confirmedTotal=total;confirmedReceipt=receipt;
            if(input>call.MaxInputTokens||output>call.MaxOutputTokens)throw new CompanyTransportException("Provider input/output exceeded the admitted contract; complete observed usage is retained.",total,receipt,actual,accountingViolation:true);
            if(!json.TryGetProperty("choices",out var choices)||choices.ValueKind!=JsonValueKind.Array||choices.GetArrayLength()!=1)throw new CompanyTransportException("One plain text response is required.",total,receipt,actual);
            var choice=choices[0];var message=choice.GetProperty("message");
            if(choice.GetProperty("finish_reason").GetString()!="stop"||message.GetProperty("role").GetString()!="assistant"||
                message.TryGetProperty("tool_calls",out var tools)&&tools.ValueKind!=JsonValueKind.Null&&!(tools.ValueKind==JsonValueKind.Array&&tools.GetArrayLength()==0)||
                message.TryGetProperty("function_call",out var function)&&function.ValueKind!=JsonValueKind.Null)
                throw new CompanyTransportException("Tool requests and incomplete replies are withheld.",total,receipt,actual);
            var text=message.GetProperty("content").GetString();if(string.IsNullOrWhiteSpace(text)||text.Length>12000)throw new CompanyTransportException("Text output was invalid.",total,receipt,actual);
            return new(text,total,receipt,actual);
        }
        catch(CompanyTransportException){throw;}
        catch(Exception e) when(e is JsonException or InvalidOperationException or KeyNotFoundException){throw new CompanyTransportException("Scoped response did not satisfy the text/usage contract.",confirmedTotal,confirmedReceipt,actual);}
    }
    private static bool Number(JsonElement j,string key,out long number){number=0;return j.ValueKind==JsonValueKind.Object&&j.TryGetProperty(key,out var v)&&v.TryGetInt64(out number)&&number>=0;}
    public void Dispose()=>_http.Dispose();
}
