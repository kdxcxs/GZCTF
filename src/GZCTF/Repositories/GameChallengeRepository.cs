﻿using GZCTF.Models.Request.Edit;
using GZCTF.Repositories.Interface;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Repositories;

public class GameChallengeRepository(AppDbContext context, IFileRepository fileRepository) : RepositoryBase(context),
    IGameChallengeRepository
{
    public async Task AddFlags(GameChallenge challenge, FlagCreateModel[] models, CancellationToken token = default)
    {
        foreach (FlagCreateModel model in models)
        {
            Attachment? attachment = model.AttachmentType == FileType.None
                ? null
                : new()
                {
                    Type = model.AttachmentType,
                    LocalFile = await fileRepository.GetFileByHash(model.FileHash, token),
                    RemoteUrl = model.RemoteUrl
                };

            challenge.Flags.Add(new() { Flag = model.Flag, Challenge = challenge, Attachment = attachment });
        }

        await SaveAsync(token);
    }

    public async Task<GameChallenge> CreateChallenge(Game game, GameChallenge challenge, CancellationToken token = default)
    {
        await context.AddAsync(challenge, token);
        game.Challenges.Add(challenge);
        await SaveAsync(token);
        return challenge;
    }

    public async Task<bool> EnsureInstances(GameChallenge challenge, Game game, CancellationToken token = default)
    {
        await context.Entry(challenge).Collection(c => c.Teams).LoadAsync(token);
        await context.Entry(game).Collection(g => g.Participations).LoadAsync(token);

        var update = game.Participations.Aggregate(false,
            (current, participation) => challenge.Teams.Add(participation) || current);

        await SaveAsync(token);

        return update;
    }

    public Task<GameChallenge?> GetChallenge(int gameId, int id, bool withFlag = false, CancellationToken token = default)
    {
        IQueryable<GameChallenge> challenges = context.GameChallenges
            .Where(c => c.Id == id && c.GameId == gameId);

        if (withFlag)
            challenges = challenges.Include(e => e.Flags);

        return challenges.FirstOrDefaultAsync(token);
    }

    public Task<GameChallenge[]> GetChallenges(int gameId, CancellationToken token = default) =>
        context.GameChallenges.Where(c => c.GameId == gameId).OrderBy(c => c.Id).ToArrayAsync(token);

    public Task<GameChallenge[]> GetChallengesWithTrafficCapturing(int gameId, CancellationToken token = default) =>
        context.GameChallenges.IgnoreAutoIncludes().Where(c => c.GameId == gameId && c.EnableTrafficCapture)
            .ToArrayAsync(token);

    public Task<bool> VerifyStaticAnswer(GameChallenge challenge, string flag, CancellationToken token = default) =>
        context.Entry(challenge).Collection(e => e.Flags).Query().AnyAsync(f => f.Flag == flag, token);

    public async Task RemoveChallenge(GameChallenge challenge, CancellationToken token = default)
    {
        await DeleteAllAttachment(challenge, true, token);

        context.Remove(challenge);
        await SaveAsync(token);
    }

    public async Task<TaskStatus> RemoveFlag(GameChallenge challenge, int flagId, CancellationToken token = default)
    {
        FlagContext? flag = challenge.Flags.FirstOrDefault(f => f.Id == flagId);

        if (flag is null)
            return TaskStatus.NotFound;

        await DeleteAttachment(flag.Attachment, token);

        context.Remove(flag);

        if (challenge.Flags.Count == 0)
            challenge.IsEnabled = false;

        await SaveAsync(token);

        return TaskStatus.Success;
    }

    public async Task UpdateAttachment(GameChallenge challenge, AttachmentCreateModel model,
        CancellationToken token = default)
    {
        Attachment? attachment = model.AttachmentType == FileType.None
            ? null
            : new()
            {
                Type = model.AttachmentType,
                LocalFile = await fileRepository.GetFileByHash(model.FileHash, token),
                RemoteUrl = model.RemoteUrl
            };

        await DeleteAllAttachment(challenge, false, token);

        if (attachment is not null)
            await context.AddAsync(attachment, token);

        challenge.Attachment = attachment;

        await SaveAsync(token);
    }

    internal async Task DeleteAllAttachment(GameChallenge challenge, bool purge = false, CancellationToken token = default)
    {
        await DeleteAttachment(challenge.Attachment, token);

        if (purge && challenge.Type == ChallengeType.DynamicAttachment)
        {
            foreach (FlagContext flag in challenge.Flags)
                await DeleteAttachment(flag.Attachment, token);

            context.RemoveRange(challenge.Flags);
        }
    }

    internal async Task DeleteAttachment(Attachment? attachment, CancellationToken token = default)
    {
        if (attachment is null)
            return;

        if (attachment.Type == FileType.Local && attachment.LocalFile is not null)
            await fileRepository.DeleteFile(attachment.LocalFile, token);

        context.Remove(attachment);
    }
}
