﻿namespace CTFServer.Repositories.Interface;

public interface ISubmissionRepository : IRepository
{
    /// <summary>
    /// 获取提交，按时间降序
    /// </summary>
    /// <param name="count">数量</param>
    /// <param name="skip">跳过数量</param>
    /// <param name="token"></param>
    /// <returns></returns>
    public Task<Submission[]> GetSubmissions(int count = 100, int skip = 0, CancellationToken token = default);

    /// <summary>
    /// 获取比赛的提交，按时间降序
    /// </summary>
    /// <param name="game">比赛对象</param>
    /// <param name="count">数量</param>
    /// <param name="skip">跳过数量</param>
    /// <param name="token"></param>
    /// <returns></returns>
    public Task<Submission[]> GetSubmissions(Game game, int count = 100, int skip = 0, CancellationToken token = default);

    /// <summary>
    /// 获取题目的提交，按时间降序
    /// </summary>
    /// <param name="challenge">题目对象</param>
    /// <param name="count">数量</param>
    /// <param name="skip">跳过数量</param>
    /// <param name="token"></param>
    /// <returns></returns>
    public Task<Submission[]> GetSubmissions(Challenge challenge, int count = 100, int skip = 0, CancellationToken token = default);

    /// <summary>
    /// 获取队伍的提交，按时间降序
    /// </summary>
    /// <param name="team">队伍参赛对象</param>
    /// <param name="count">数量</param>
    /// <param name="skip">跳过数量</param>
    /// <param name="token"></param>
    /// <returns></returns>
    public Task<Submission[]> GetSubmissions(Participation team, int count = 100, int skip = 0, CancellationToken token = default);

    /// <summary>
    /// 添加提交
    /// </summary>
    /// <param name="submission">提交对象</param>
    /// <param name="token"></param>
    /// <returns></returns>
    public Task<Submission> AddSubmission(Submission submission, CancellationToken token = default);

    /// <summary>
    /// 更新提交
    /// </summary>
    /// <param name="submission">提交对象</param>
    /// <param name="token"></param>
    /// <returns></returns>
    public Task UpdateSubmission(Submission submission, CancellationToken token = default);

    /// <summary>
    /// 获取未检查的 flag
    /// </summary>
    /// <param name="token"></param>
    /// <returns></returns>
    public Task<Submission[]> GetUncheckedFlags(CancellationToken token = default);

    /// <summary>
    /// 获取提交
    /// </summary>
    /// <param name="gameId">比赛Id</param>
    /// <param name="challengeId">题目Id</param>
    /// <param name="userId">用户Id</param>
    /// <param name="submitId">提交Id</param>
    /// <param name="token"></param>
    /// <returns></returns>
    public Task<Submission?> GetSubmission(int gameId, int challengeId, string userId, int submitId, CancellationToken token = default);
}