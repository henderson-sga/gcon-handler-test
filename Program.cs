using Microsoft.Azure.WebPubSub.AspNetCore;
using Microsoft.Azure.WebPubSub.Common;
using Microsoft.Extensions.Azure;
using System.Diagnostics;
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

    endpoints.MapMethods("/message", new List<string> { "POST", "OPTIONS" }.AsEnumerable(), async (WebPubSubServiceClient<sample_chat> serviceClient, HttpContext context) => 
    {
      if (context.Request.Body != null)
      {
        var bodyString = new StreamReader(context.Request.Body).ReadToEnd();
      }

      await context.Response.WriteAsync("OK");
      return;
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
        await _serviceClient.SendToUserAsync(request.ConnectionContext.UserId, $"Loja {request.ConnectionContext.UserId.ToUpper()} adicionada!...");
    }

    public override async ValueTask<UserEventResponse> OnMessageReceivedAsync(UserEventRequest request, CancellationToken cancellationToken)
    {
        Stopwatch timeWatch = new Stopwatch();

        timeWatch.Start();

        async Task storeInCosmosDB()
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
        };

        await storeInCosmosDB();

        await _serviceClient.SendToUserAsync(request.ConnectionContext.UserId, $"{request.ConnectionContext.UserId.ToUpper()} adicionou um novo pedido: {request.Data}");

        timeWatch.Stop();

        return request.CreateResponse($"finalizado em: {(decimal)timeWatch.Elapsed.TotalSeconds}s");
    }
}
