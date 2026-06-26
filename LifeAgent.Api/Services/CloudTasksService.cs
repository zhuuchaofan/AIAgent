using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Google.Cloud.Tasks.V2;
using LifeAgent.Api.Models;

namespace LifeAgent.Api.Services;

public class CloudTasksService : ICloudTasksService
{
    private readonly HttpClient _httpClient;
    private readonly RagOptions _ragOptions;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<CloudTasksService> _logger;
    private readonly string _projectId;

    public CloudTasksService(
        HttpClient httpClient,
        IOptions<RagOptions> ragOptions,
        IWebHostEnvironment env,
        IConfiguration config,
        ILogger<CloudTasksService> logger)
    {
        _httpClient = httpClient;
        _ragOptions = ragOptions.Value;
        _env = env;
        _logger = logger;
        _projectId = config["Firestore:ProjectId"] ?? "copper-affinity-467409-k7";
    }

    public async System.Threading.Tasks.Task EnqueueIngestTaskAsync(string userId, string documentId, string gcsPath)
    {
        if (_env.IsDevelopment())
        {
            _logger.LogInformation("[Dev Mock Tasks] Simulating Cloud Tasks asynchronous trigger for Document: {DocId}", documentId);
            
            // 异步在后台发起 HttpClient 投递，以实现真正的非阻塞异步调用
            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                try
                {
                    // 模拟网络和冷启动延迟
                    await System.Threading.Tasks.Task.Delay(500);

                    var audience = _ragOptions.InternalProcessAudience;
                    if (string.IsNullOrEmpty(audience))
                    {
                        audience = "http://localhost:5000/";
                    }
                    if (!audience.EndsWith("/")) audience += "/";

                    var localUrl = $"{audience}internal/api/v1/documents/process";
                    var payload = new
                    {
                        documentId,
                        userId,
                        gcsPath
                    };

                    using var request = new HttpRequestMessage(System.Net.Http.HttpMethod.Post, localUrl);
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "dev-token");
                    request.Content = JsonContent.Create(payload);

                    var response = await _httpClient.SendAsync(request);
                    if (response.IsSuccessStatusCode)
                    {
                        _logger.LogInformation("[Dev Mock Tasks] Mock trigger succeeded for Document {DocId}", documentId);
                    }
                    else
                    {
                        var body = await response.Content.ReadAsStringAsync();
                        _logger.LogError("[Dev Mock Tasks] Mock trigger failed: {Status} - {Body}", response.StatusCode, body);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[Dev Mock Tasks] Exception during mock trigger execution");
                }
            });

            await System.Threading.Tasks.Task.CompletedTask;
        }
        else
        {
            _logger.LogInformation("Enqueuing real GCP Cloud Task for Document: {DocId}", documentId);
            
            var client = await CloudTasksClient.CreateAsync();
            var queuePath = QueueName.Format(_projectId, _ragOptions.CloudTasksLocation, _ragOptions.CloudTasksQueueName);

            var audience = _ragOptions.InternalProcessAudience;
            if (string.IsNullOrEmpty(audience))
            {
                throw new InvalidOperationException("InternalProcessAudience is not configured in RagOptions.");
            }
            if (!audience.EndsWith("/")) audience += "/";

            var workerUrl = $"{audience}internal/api/v1/documents/process";

            var task = new Google.Cloud.Tasks.V2.Task
            {
                HttpRequest = new Google.Cloud.Tasks.V2.HttpRequest
                {
                    HttpMethod = Google.Cloud.Tasks.V2.HttpMethod.Post,
                    Url = workerUrl,
                    Headers = { { "Content-Type", "application/json" } },
                    Body = Google.Protobuf.ByteString.CopyFromUtf8(JsonSerializer.Serialize(new
                    {
                        documentId,
                        userId,
                        gcsPath
                    })),
                    OidcToken = new Google.Cloud.Tasks.V2.OidcToken
                    {
                        Audience = audience
                    }
                }
            };

            await client.CreateTaskAsync(queuePath, task);
            _logger.LogInformation("Successfully enqueued Cloud Task: Queue={Queue}, Target={Target}", queuePath, workerUrl);
        }
    }
}
