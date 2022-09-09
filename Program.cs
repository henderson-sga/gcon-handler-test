using Microsoft.Azure.WebPubSub.AspNetCore;
using Microsoft.Azure.WebPubSub.Common;
using Microsoft.Extensions.Azure;
using System.Text.Json;
using System.Web;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddWebPubSub(
    o => o.ServiceEndpoint = new ServiceEndpoint(builder.Configuration["Azure:WebPubSub:ConnectionString"]))
    .AddWebPubSubServiceClient<sample_chat>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

app.UseStaticFiles();
app.UseRouting();

app.UseEndpoints(endpoints =>
{    
    endpoints.MapGet("/connectionString", async (WebPubSubServiceClient<sample_chat> serviceClient, HttpContext context) =>
    {
        var id = context.Request.Query["id"];
        if (id.Count != 1)
        {
            context.Response.StatusCode = 400;
            await context.Response.WriteAsync("missing user id");
            return;
        }
        var webpsInstance = serviceClient.GetClientAccessUri(userId: id); 
        await context.Response.WriteAsync(webpsInstance.AbsoluteUri);
    });

    endpoints.MapWebPubSubHub<sample_chat>("/handler");
});

app.Run();

sealed class sample_chat : WebPubSubHub
{
    private readonly WebPubSubServiceClient<sample_chat> _serviceClient;
    private readonly string azFuncEndpoint = "https://gcom-az-function.azurewebsites.net/api/HttpTrigger?code=8cosiPYytyXJjAIwUcmgoAjJxzmiHmKLyweKZJyE_2lOAzFug4AbTw==";

    public sample_chat(WebPubSubServiceClient<sample_chat> serviceClient)
    {
        _serviceClient = serviceClient;
    }

    public override async Task OnConnectedAsync(ConnectedEventRequest request)
    {
        await _serviceClient.SendToAllAsync($"[FROM HANDLER] {request.ConnectionContext.UserId} joined.");
    }

    public override async ValueTask<UserEventResponse> OnMessageReceivedAsync(UserEventRequest request, CancellationToken cancellationToken)
    {
        var httpClient = new HttpClient();
      
        var httpRequest = new HttpRequestMessage();

        httpRequest.Method = HttpMethod.Post;

        httpRequest.RequestUri = new Uri(azFuncEndpoint);

        var UserId= request.ConnectionContext.UserId;
        var hub = request.ConnectionContext.Hub;
        var message = request.Data.ToString().Replace('"', ' ').Trim();

        httpRequest.Content = new StringContent(JsonSerializer.Serialize(new { data = message, user = UserId, hub = hub }));

        await httpClient.SendAsync(httpRequest);

        await _serviceClient.SendToAllAsync($"[{request.ConnectionContext.UserId}] {request.Data}");

        return request.CreateResponse($"[FROM HANDLER] ack.");
    }
}
