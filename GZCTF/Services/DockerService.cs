using System.Net;
using CTFServer.Models.Internal;
using CTFServer.Services.Interface;
using CTFServer.Utils;
using Docker.DotNet;
using Docker.DotNet.Models;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;

namespace CTFServer.Services;

public class DockerService : IContainerService
{
    private readonly ILogger<DockerService> logger;
    private readonly DockerConfig options;
    private readonly string publicEntry;
    private readonly DockerClient dockerClient;
    private readonly AuthConfig? authConfig;
    private readonly IStringLocalizer<ServiceResource> loc;

    public DockerService(IOptions<ContainerProvider> _options,
        IStringLocalizer<ServiceResource> _loc,
        IOptions<RegistryConfig> _registry,
        ILogger<DockerService> _logger)
    {
        options = _options.Value.DockerConfig ?? new DockerConfig();
        publicEntry = _options.Value.PublicEntry;
        logger = _logger;
        loc = _loc;

        DockerClientConfiguration cfg = string.IsNullOrEmpty(this.options.Uri) ? new() : new(new Uri(this.options.Uri));

        // TODO: Docker Auth Required
        dockerClient = cfg.CreateClient();

        // Auth for registry
        if (!string.IsNullOrWhiteSpace(_registry.Value.UserName) && !string.IsNullOrWhiteSpace(_registry.Value.Password))
        {
            authConfig = new AuthConfig()
            {
                Username = _registry.Value.UserName,
                Password = _registry.Value.Password,
            };
        }

        logger.SystemLog($"{loc["Docker service started"]} ({(string.IsNullOrEmpty(this.options.Uri) ? "localhost" : this.options.Uri)})", TaskStatus.Success, LogLevel.Debug);
    }

    public Task<Container?> CreateContainerAsync(ContainerConfig config, CancellationToken token = default)
        => options.SwarmMode ? CreateContainerWithSwarm(config, token) : CreateContainerWithSingle(config, token);

    public async Task DestroyContainerAsync(Container container, CancellationToken token = default)
    {
        try
        {
            if (options.SwarmMode)
                await dockerClient.Swarm.RemoveServiceAsync(container.ContainerId, token);
            else
                await dockerClient.Containers.RemoveContainerAsync(container.ContainerId, new() { Force = true }, token);
        }
        catch (DockerContainerNotFoundException)
        {
            logger.SystemLog($"{loc["Container"]} {container.ContainerId} {loc["has been destroyed"]}", TaskStatus.Success, LogLevel.Debug);
        }
        catch (DockerApiException e)
        {
            if (e.StatusCode == HttpStatusCode.NotFound)
            {
                logger.SystemLog($"{loc["Container"]} {container.ContainerId} {loc["has been destroyed"]}", TaskStatus.Success, LogLevel.Debug);
            }
            else
            {
                logger.SystemLog($"{loc["Container"]} {container.ContainerId} {loc["deletion failed"]}, {loc["status"]}：{e.StatusCode.ToString()}", TaskStatus.Fail, LogLevel.Warning);
                logger.SystemLog($"{loc["Container"]} {container.ContainerId} {loc["deletion failed"]}, {loc["response"]}：{e.ResponseBody}", TaskStatus.Fail, LogLevel.Error);
                return;
            }
        }
        catch (Exception e)
        {
            logger.LogError(e, $"{loc["Container"]} {container.ContainerId} {loc["deletion failed"]}");
            return;
        }

        container.Status = ContainerStatus.Destroyed;
    }

    private static string GetName(ContainerConfig config)
        => $"{config.Image.Split("/").LastOrDefault()?.Split(":").FirstOrDefault()}_{Codec.StrMD5(config.Flag ?? Guid.NewGuid().ToString())[..16]}";

    private static CreateContainerParameters GetCreateContainerParameters(ContainerConfig config)
        => new()
        {
            Image = config.Image,
            Labels = new Dictionary<string, string> { ["TeamId"] = config.TeamId, ["UserId"] = config.UserId },
            Name = GetName(config),
            Env = config.Flag is null ? Array.Empty<string>() : new string[] { $"GZCTF_FLAG={config.Flag}" },
            ExposedPorts = new Dictionary<string, EmptyStruct>()
            {
                [config.ExposedPort.ToString()] = new EmptyStruct()
            },
            HostConfig = new()
            {
                PublishAllPorts = true,
                Memory = config.MemoryLimit * 1024 * 1024,
                CPUCount = 1,
                Privileged = config.PrivilegedContainer
            }
        };

    private ServiceCreateParameters GetServiceCreateParameters(ContainerConfig config)
        => new()
        {
            RegistryAuth = authConfig,
            Service = new()
            {
                Name = GetName(config),
                Labels = new Dictionary<string, string> { ["TeamId"] = config.TeamId, ["UserId"] = config.UserId },
                Mode = new() { Replicated = new() { Replicas = 1 } },
                EndpointSpec = new()
                {
                    Ports = new PortConfig[] { new() {
                        PublishMode = "global",
                        TargetPort = (uint)config.ExposedPort,
                    } },
                },
                TaskTemplate = new()
                {
                    RestartPolicy = new() { Condition = "none" },
                    ContainerSpec = new()
                    {
                        Image = config.Image,
                        Env = config.Flag is null ? Array.Empty<string>() : new string[] { $"GZCTF_FLAG={config.Flag}" }
                    },
                    Resources = new()
                    {
                        Limits = new()
                        {
                            MemoryBytes = config.MemoryLimit * 1024 * 1024,
                            NanoCPUs = config.CPUCount * 10_0000_0000,
                        },
                    },
                }
            }
        };

    public async Task<Container?> CreateContainerWithSwarm(ContainerConfig config, CancellationToken token = default)
    {
        var parameters = GetServiceCreateParameters(config);
        ServiceCreateResponse? serviceRes = null;
        int retry = 0;
    CreateContainer:
        try
        {
            serviceRes = await dockerClient.Swarm.CreateServiceAsync(parameters, token);
        }
        catch (DockerApiException e)
        {
            if (e.StatusCode == HttpStatusCode.Conflict && retry < 3)
            {
                logger.SystemLog($"{loc["Container"]} {parameters.Service.Name} {loc["already exists, trying to remove and recreate"]}", TaskStatus.Duplicate, LogLevel.Warning);
                await dockerClient.Swarm.RemoveServiceAsync(parameters.Service.Name, token);
                retry++;
                goto CreateContainer;
            }
            else
            {
                logger.SystemLog($"{loc["Container"]} {parameters.Service.Name} {loc["creation failed"]}, {loc["status"]}：{e.StatusCode.ToString()}", TaskStatus.Fail, LogLevel.Warning);
                logger.SystemLog($"{loc["Container"]} {parameters.Service.Name} {loc["creation failed"]}, {loc["response"]}：{e.ResponseBody}", TaskStatus.Fail, LogLevel.Error);
                return null;
            }
        }
        catch (Exception e)
        {
            logger.LogError(e, $"{loc["Container"]} {parameters.Service.Name} {loc["deletion failed"]}");
            return null;
        }

        Container container = new()
        {
            ContainerId = serviceRes.ID,
            Image = config.Image,
        };

        retry = 0;
        SwarmService? res;
        do
        {
            res = await dockerClient.Swarm.InspectServiceAsync(serviceRes.ID, token);
            retry++;
            if (retry == 3)
            {
                logger.SystemLog($"{loc["Container"]} {parameters.Service.Name} {loc["failed to get exposed port info after creation"]}", TaskStatus.Fail, LogLevel.Warning);
                return null;
            }
            if (res is not { Endpoint.Ports.Count: > 0 })
                await Task.Delay(500, token);
        } while (res is not { Endpoint.Ports.Count: > 0 });

        var port = res.Endpoint.Ports.First();

        container.StartedAt = res.CreatedAt;
        container.ExpectStopAt = container.StartedAt + TimeSpan.FromHours(2);

        container.Port = (int)port.PublishedPort;
        container.Status = ContainerStatus.Running;

        if (!string.IsNullOrEmpty(publicEntry))
            container.PublicIP = publicEntry;

        return container;
    }

    public async Task<Container?> CreateContainerWithSingle(ContainerConfig config, CancellationToken token = default)
    {
        var parameters = GetCreateContainerParameters(config);
        CreateContainerResponse? containerRes = null;
        try
        {
            containerRes = await dockerClient.Containers.CreateContainerAsync(parameters, token);
        }
        catch (DockerImageNotFoundException)
        {
            logger.SystemLog($"{loc["Pulling container image"]} {config.Image}", TaskStatus.Pending, LogLevel.Information);

            await dockerClient.Images.CreateImageAsync(new()
            {
                FromImage = config.Image
            }, authConfig, new Progress<JSONMessage>(msg =>
            {
                Console.WriteLine($"{msg.Status}|{msg.ProgressMessage}|{msg.ErrorMessage}");
            }), token);
        }
        catch (Exception e)
        {
            logger.LogError(e, $"{loc["Container"]} {parameters.Name} {loc["creation failed"]}");
            return null;
        }

        try
        {
            containerRes ??= await dockerClient.Containers.CreateContainerAsync(parameters, token);
        }
        catch (Exception e)
        {
            logger.LogError(e, $"{loc["Container"]} {parameters.Name} {loc["creation failed"]}");
            return null;
        }

        Container container = new()
        {
            ContainerId = containerRes.ID,
            Image = config.Image,
        };

        var retry = 0;
        bool started;

        do
        {
            started = await dockerClient.Containers.StartContainerAsync(containerRes.ID, new(), token);
            retry++;
            if (retry == 3)
            {
                logger.SystemLog(string.Format(loc["Failed to start container instance {0}"],
                        $"{container.Id} ({config.Image.Split("/").LastOrDefault()})"), TaskStatus.Fail, LogLevel.Warning);
                return null;
            }
            if (!started)
                await Task.Delay(500, token);
        } while (!started);

        var info = await dockerClient.Containers.InspectContainerAsync(container.ContainerId, token);

        container.Status = (info.State.Dead || info.State.OOMKilled || info.State.Restarting) ? ContainerStatus.Destroyed :
                info.State.Running ? ContainerStatus.Running : ContainerStatus.Pending;

        if (container.Status != ContainerStatus.Running)
        {
            logger.SystemLog(string.Format(loc["Failed to create container instance {0}: {1}"],
                $"{container.Id} ({config.Image.Split("/").LastOrDefault()})", info.State.Error), TaskStatus.Fail, LogLevel.Warning);
            return null;
        }

        container.StartedAt = DateTimeOffset.Parse(info.State.StartedAt);
        container.ExpectStopAt = container.StartedAt + TimeSpan.FromHours(2);

        var port = info.NetworkSettings.Ports
            .FirstOrDefault(p =>
                p.Key.StartsWith(config.ExposedPort.ToString())
            ).Value.First().HostPort;

        if (int.TryParse(port, out var numport))
            container.Port = numport;
        else
            logger.SystemLog(string.Format(loc["Failed to parse port number: {0}, this is unexpected behavior"], port), TaskStatus.Fail, LogLevel.Warning);

        container.IP = info.NetworkSettings.IPAddress;

        if (!string.IsNullOrEmpty(publicEntry))
            container.PublicIP = publicEntry;

        return container;
    }
}
