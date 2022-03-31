﻿using System.Text.Json.Serialization;

namespace CTFServer.Models.Request.Account;

public class ClientUserInfoModel
{
    /// <summary>
    /// 用户ID
    /// </summary>
    public string? UserId { get; set; }

    /// <summary>
    /// 用户名
    /// </summary>
    public string? UserName { get; set; }

    /// <summary>
    /// 邮箱
    /// </summary>
    public string? Email { get; set; }

    /// <summary>
    /// 签名
    /// </summary>
    public string? Bio { get; set; }

    /// <summary>
    /// 手机号码
    /// </summary>
    public string? Phone { get; set; }

    /// <summary>
    /// 真实姓名
    /// </summary>
    public string? RealName { get; set; }

    /// <summary>
    /// 头像链接
    /// </summary>
    public string? Avatar { get; set; }

    /// <summary>
    /// 当前队伍
    /// </summary>
    public int? ActiveTeamId { get; set; }

    /// <summary>
    /// 用户角色
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public Role? Role { get; set; }

    public static ClientUserInfoModel FromUserInfo(UserInfo user)
        => new()
        {
            UserId = user.Id,
            Bio = user.Bio,
            Email = user.Email,
            UserName = user.UserName,
            RealName = user.RealName,
            Phone = user.PhoneNumber,
            Avatar = user.AvatarUrl,
            ActiveTeamId = user.ActiveTeamId,
            Role = user.Role
        };
}
