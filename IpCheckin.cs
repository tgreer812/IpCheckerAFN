using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Azure.Data.Tables;
using Azure;

namespace Tyler.Greer
{
    public class IpCheckin
    {
        private readonly ILogger<IpCheckin> _logger;

        public IpCheckin(ILogger<IpCheckin> logger)
        {
            _logger = logger;
        }

        [Function("IpCheckin")]
        public async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Anonymous, "get", "post")] HttpRequest req)
        {
            _logger.LogInformation("C# HTTP trigger function processed a request.");
            Console.WriteLine(req.ToString());

            // First validate that the body is JSON
            if (req.ContentType == null || !req.ContentType.Contains("application/json"))
            {
                return new BadRequestObjectResult("Invalid content type. Must be application/json");
            }

            // Extract the body asynchronously
            string bodyString;
            using (var reader = new System.IO.StreamReader(req.Body))
            {
                bodyString = await reader.ReadToEndAsync();
            }

            // Parse the body
            var bodyJson = System.Text.Json.JsonDocument.Parse(bodyString);

            // Get the Azure Table Storage connection string
            string? azureTableConnectionString = Environment.GetEnvironmentVariable("AzureTableConnectionString");
            if (azureTableConnectionString == null)
            {
                _logger.LogError("AzureTableConnectionString environment variable not set");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }

            // Create a new TableClient
            var tableClient = new TableClient(azureTableConnectionString, "IpCheckin");

            // Check if an entity with the same partition key (device name) exists
            string? deviceName, deviceIp;
            
            try {
                deviceName = bodyJson.RootElement.GetProperty("Name").GetString();
                deviceIp = bodyJson.RootElement.GetProperty("IPv4").GetString();

                if (deviceName == null || deviceIp == null) {
                    throw new KeyNotFoundException();
                }
            } catch (KeyNotFoundException ex) {
                _logger.LogError(ex, "Invalid JSON body. Must contain 'DeviceName' and 'IPv4' properties");
                return new BadRequestObjectResult("Invalid JSON body.");
            }
            
            try
            {
                TableEntity entity;
                try
                {
                    var response = await tableClient.GetEntityAsync<TableEntity>(deviceName, deviceIp);
                    entity = response.Value;
                }
                catch (RequestFailedException ex) when (ex.Status == 404)
                {
                    // Entity doesn't exist, create a new one
                    entity = new TableEntity(deviceName, deviceIp);
                }

                entity["Name"] = deviceName;
                entity["IPv4"] = deviceIp;

                var addResponse = await tableClient.UpsertEntityAsync(entity);
                if (addResponse.Status != 204 && addResponse.Status != 200)
                {
                    return new BadRequestObjectResult("Failed to add or update entity in table storage");
                }

                return new OkObjectResult("");
            }
            catch (Azure.RequestFailedException ex)
            {
                _logger.LogError(ex, "Error accessing table storage");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
        }

    }
}
