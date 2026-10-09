using Azure.Identity;
using Azure.Storage.Blobs;

var builder = WebApplication.CreateBuilder(args);

var storageUri = builder.Configuration[
    "Azure:Storage:ServiceUri"
] ?? throw new InvalidOperationException(
    "Azure Storage URI is missing."
);

builder.Services.AddSingleton(
    new BlobServiceClient(
        new Uri(storageUri),
        new  AzureCliCredential()
    )
);

var app = builder.Build();

// Simple routing test
app.MapGet("/api/health/ping", () =>
{
    return Results.Ok(new
    {
        Status = "Healthy",
        Message = "PolicyWise API is running"
    });
});

// Azure Blob Storage connectivity test
app.MapGet("/api/health/blob", async (
    BlobServiceClient blobServiceClient,
    IConfiguration configuration,
    CancellationToken cancellationToken) =>
{
    var containerName = configuration[
        "Azure:Storage:ContainerName"
    ];

    if (string.IsNullOrWhiteSpace(containerName))
    {
        return Results.Problem(
            "Container name is missing.",
            statusCode: 500
        );
    }

    try
    {
        var containerClient =
            blobServiceClient.GetBlobContainerClient(
                containerName
            );

        var exists = await containerClient.ExistsAsync(
            cancellationToken
        );

        if (!exists.Value)
        {
            return Results.NotFound(new
            {
                Status = "Unhealthy",
                Message = "Container not found"
            });
        }

        return Results.Ok(new
        {
            Status = "Healthy",
            Service = "Azure Blob Storage",
            Container = containerName
        });
    }
    catch (Exception ex)
    {
        app.Logger.LogError(
            ex,
            "Blob Storage connectivity failed"
        );

        return Results.Problem(
            "Blob Storage connectivity failed.",
            statusCode: 503
        );
    }
});

app.Run();