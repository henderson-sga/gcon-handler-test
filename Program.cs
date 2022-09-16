using Microsoft.Azure.WebPubSub.AspNetCore;
using Microsoft.Azure.WebPubSub.Common;
using Microsoft.Extensions.Azure;
using System.Configuration;  
using System.Diagnostics;
using System.Text.Json;
using System.Web;
using MongoDB.Driver;
using MongoDB.Bson;


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

string connectionString = "mongodb://cosmosdbhender:AeWl97eZqqpoam60wRmqXxgxpSbph7K6aEWjZCTGGFF4c8BfyStj69p4tP5rVDI3kNMaB6oDiO1CsWWRAablyw==@cosmosdbhender.mongo.cosmos.azure.com:10255/?ssl=true&replicaSet=globaldb&retrywrites=false&maxIdleTimeMS=120000&appName=@cosmosdbhender@";
var mongoClient = new MongoClient(connectionString);
var database = mongoClient.GetDatabase("my-database");
var collection = database.GetCollection<CosmosDBSchema>("container");


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

    endpoints.MapGet("/dbData", async (WebPubSubServiceClient<sample_chat> serviceClient, HttpContext context) => 
    {
        var allDocuments = collection.Find<CosmosDBSchema>(new BsonDocument()).ToList();
        Console.WriteLine(allDocuments);

        await context.Response.WriteAsJsonAsync(allDocuments);
    });

    endpoints.MapWebPubSubHub<sample_chat>("/handler");
});

app.Run();


struct StatusOrder
{
  public string status { get; set; }
  public string updatedAt { get; set; }
}

class CosmosDBSchema
{
    public ObjectId _id { get; set; }
    public Object[] order_status { get; set; }
    public string orderId { get; set; }
    public string storeId { get; set; }
    public string customerId { get; set; }
    public string createdAt { get; set; }
}



struct Data
{
    public string status  { get; set; }
    public string orderId { get; set; }
    public string storeId { get; set; }
    public string customerId { get; set; }
    public string message { get; set; }
}

struct RequestJsonSchema
{
    public string type { get; set; }
    public Data data { get; set; }
}

sealed class sample_chat : WebPubSubHub
{
    private readonly WebPubSubServiceClient<sample_chat> _serviceClient;
    //private readonly string azFuncEndpoint = ConfigurationManager.AppSettings["Azure:AzFunctionCosmosDBInput:ConnectionString"];
    private readonly string azFuncEndpoint = "https://gcom-az-function.azurewebsites.net/api/HttpTrigger?code=8cosiPYytyXJjAIwUcmgoAjJxzmiHmKLyweKZJyE_2lOAzFug4AbTw==";
    //private readonly string azFuncEndpoint = "http://localhost:7071/api/HttpTrigger";

    public sample_chat(WebPubSubServiceClient<sample_chat> serviceClient)
    {
        _serviceClient = serviceClient;
    }

    public override async Task OnConnectedAsync(ConnectedEventRequest request)
    {
        var response = new RequestJsonSchema { 
            type = "OnConnected",
            data = new Data {
                message = $"Loja {request.ConnectionContext.UserId.ToUpper()} Ativa!" ,
                storeId = request.ConnectionContext.UserId,
            }
        };
        await _serviceClient.SendToUserAsync(request.ConnectionContext.UserId, JsonSerializer.Serialize(response));
    }

    public override async ValueTask<UserEventResponse> OnMessageReceivedAsync(UserEventRequest request, CancellationToken cancellationToken)
    {
        var storeId = request.ConnectionContext.UserId;

        Stopwatch timeWatch = new Stopwatch();

        timeWatch.Start();

        async Task storeInCosmosDB()
        {
          var httpClient = new HttpClient();
          var httpRequest = new HttpRequestMessage();

          httpRequest.Method = HttpMethod.Post;
          httpRequest.RequestUri = new Uri(azFuncEndpoint);

          //var message = request.Data.ToString().Replace('"', ' ').Trim();

          var payload = JsonSerializer.Deserialize<RequestJsonSchema>(request.Data).ToJson();

          httpRequest.Content = new StringContent(payload);

          await httpClient.SendAsync(httpRequest);

          await _serviceClient.SendToUserAsync(request.ConnectionContext.UserId, payload);
        };

        await storeInCosmosDB();

        timeWatch.Stop();

        var secondaryResponse = new RequestJsonSchema { 
            type = "OnMessage",
            data = new Data {
              message = $"Request finalizado em: {(decimal)timeWatch.Elapsed.TotalSeconds}s"
            }
         };

        return request.CreateResponse(JsonSerializer.Serialize(secondaryResponse).ToString());
    }
}
