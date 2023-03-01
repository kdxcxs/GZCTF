using System.Threading.Channels;
using CTFServer.Repositories.Interface;
using CTFServer.Utils;
using Microsoft.Extensions.Localization;

namespace CTFServer.Services;

public static class ChannelService
{
    internal static IServiceCollection AddChannel<T>(this IServiceCollection services)
    {
        var channel = Channel.CreateUnbounded<T>();
        services.AddSingleton(channel);
        services.AddSingleton(channel.Reader);
        services.AddSingleton(channel.Writer);
        return services;
    }
}

public class FlagChecker : IHostedService
{
    private readonly ILogger<FlagChecker> logger;
    private readonly ChannelReader<Submission> channelReader;
    private readonly ChannelWriter<Submission> channelWriter;
    private readonly IServiceScopeFactory serviceScopeFactory;
    private readonly IStringLocalizer<ServiceResource> loc;

    private CancellationTokenSource TokenSource { get; set; } = new CancellationTokenSource();

    public FlagChecker(ChannelReader<Submission> _channelReader,
        ChannelWriter<Submission> _channelWriter,
        ILogger<FlagChecker> _logger,
        IStringLocalizer<ServiceResource> _loc,
        IServiceScopeFactory _serviceScopeFactory)
    {
        loc = _loc;
        logger = _logger;
        channelReader = _channelReader;
        channelWriter = _channelWriter;
        serviceScopeFactory = _serviceScopeFactory;
    }

    private async Task Checker(int id, CancellationToken token = default)
    {
        logger.SystemLog(string.Format(loc["Checker thread #{0} started"], id), TaskStatus.Pending, LogLevel.Debug);

        try
        {
            await foreach (var item in channelReader.ReadAllAsync(token))
            {
                logger.SystemLog(string.Format(loc["Started processing submission for Team {0}: {1}"], item.Team.Name, item.Answer), TaskStatus.Pending, LogLevel.Debug);

                await using var scope = serviceScopeFactory.CreateAsyncScope();

                var eventRepository = scope.ServiceProvider.GetRequiredService<IGameEventRepository>();
                var instanceRepository = scope.ServiceProvider.GetRequiredService<IInstanceRepository>();
                var gameNoticeRepository = scope.ServiceProvider.GetRequiredService<IGameNoticeRepository>();
                var gameRepository = scope.ServiceProvider.GetRequiredService<IGameRepository>();
                var submissionRepository = scope.ServiceProvider.GetRequiredService<ISubmissionRepository>();

                try
                {
                    var (type, ans) = await instanceRepository.VerifyAnswer(item, token);

                    if (ans == AnswerResult.NotFound)

                        logger.Log(string.Format(loc["[Instance not found] Team [{0}] submitted answer [{1}] for challenge [{2}]"],
                            item.Team.Name, item.Answer, item.Challenge.Title),
                            item.User!, TaskStatus.NotFound, LogLevel.Warning);
                    else if (ans == AnswerResult.Accepted)
                    {
                        logger.Log(string.Format(loc["[Submission accepted] Team [{0}] submitted answer [{1}] for challenge [{2}]"],
                            item.Team.Name, item.Answer, item.Challenge.Title),
                            item.User!, TaskStatus.Success, LogLevel.Information);

                        await eventRepository.AddEvent(GameEvent.FromSubmission(item, type, ans), token);
                    }
                    else
                    {
                        logger.Log(string.Format(loc["[Submission wrong] Team [{0}] submitted answer [{1}] for challenge [{2}]"],
                            item.Team.Name, item.Answer, item.Challenge.Title),
                            item.User!, TaskStatus.Success, LogLevel.Information);

                        await eventRepository.AddEvent(GameEvent.FromSubmission(item, type, ans), token);

                        var result = await instanceRepository.CheckCheat(item, token);
                        ans = result.AnswerResult;

                        if (ans == AnswerResult.CheatDetected)
                        {
                            logger.Log(string.Format(loc["[Cheat check] Team [{0}] suspected of cheating in [{1}], related teams [{2}]"],
                                item.Team.Name, item.Challenge.Title, result.SourceTeam!.Name),
                                item.User!, TaskStatus.Success, LogLevel.Information);

                            await eventRepository.AddEvent(new()
                            {
                                Type = EventType.CheatDetected,
                                Content = string.Format(loc["Suspected cheating in challenge [{0}], related teams [{1}] and [{2}]"],
                                    item.Challenge.Title, item.Team.Name, result.SourceTeam!.Name),
                                TeamId = item.TeamId,
                                UserId = item.UserId,
                                GameId = item.GameId,
                            }, token);
                        }
                    }

                    if (item.Game.EndTimeUTC > DateTimeOffset.UtcNow
                        && type != SubmissionType.Unaccepted
                        && type != SubmissionType.Normal)
                        await gameNoticeRepository.AddNotice(GameNotice.FromSubmission(item, type), token);

                    item.Status = ans;
                    await submissionRepository.SendSubmission(item);

                    gameRepository.FlushScoreboardCache(item.GameId);
                }
                catch (Exception e)
                {
                    logger.SystemLog(string.Format(loc["Checker thread #{0} encountered an exception"], id), TaskStatus.Fail, LogLevel.Debug);
                    logger.LogError(e.Message, e);
                }

                token.ThrowIfCancellationRequested();
            }
        }
        catch (OperationCanceledException)
        {
            logger.SystemLog(string.Format(loc["Task cancelled, checker thread #{0} will exit"], id), TaskStatus.Exit, LogLevel.Debug);
        }
        finally
        {
            logger.SystemLog(string.Format(loc["Checker thread #{0} exited"], id), TaskStatus.Exit, LogLevel.Debug);
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        TokenSource = new CancellationTokenSource();

        for (int i = 0; i < 4; ++i)
            _ = Checker(i, TokenSource.Token);

        await using var scope = serviceScopeFactory.CreateAsyncScope();

        var submissionRepository = scope.ServiceProvider.GetRequiredService<ISubmissionRepository>();
        var flags = await submissionRepository.GetUncheckedFlags(TokenSource.Token);

        foreach (var item in flags)
            await channelWriter.WriteAsync(item, TokenSource.Token);

        if (flags.Length > 0)
            logger.SystemLog(string.Format(loc["Restarted checking {0} flags"], flags.Length), TaskStatus.Pending, LogLevel.Debug);

        logger.SystemLog(loc["Flag checker started"], TaskStatus.Success, LogLevel.Debug);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        TokenSource.Cancel();

        logger.SystemLog(loc["Flag checker stopped"], TaskStatus.Exit, LogLevel.Debug);

        return Task.CompletedTask;
    }
}
